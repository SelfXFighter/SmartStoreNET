﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using SmartStore.Collections;
using SmartStore.ComponentModel;
using SmartStore.Core.Events;
using SmartStore.Core.Infrastructure.DependencyManagement;

namespace SmartStore.Services.Events
{
	public class ConsumerRegistry : IConsumerRegistry
	{
		private readonly static Multimap<Type, ConsumerDescriptor> _descriptorMap
			= new Multimap<Type, ConsumerDescriptor>();

		public ConsumerRegistry(IEnumerable<Lazy<IConsumer, EventConsumerMetadata>> consumers)
		{
			foreach (var consumer in consumers)
			{
				var metadata = consumer.Metadata;

				if (!metadata.IsActive)
					continue;

				var methods = FindMethods(metadata);

				foreach (var method in methods)
				{
					var descriptor = new ConsumerDescriptor(metadata)
					{
						IsAsync = method.ReturnType == typeof(Task),
						FireForget = method.HasAttribute<FireForgetAttribute>(false)
					};

					if (descriptor.IsAsync && descriptor.FireForget)
					{
						// TODO: better message
						throw new NotSupportedException("An asynchronous message consumer method cannot be called as fire & forget.");
					}

					if (method.ReturnType != typeof(Task) && method.ReturnType != typeof(void))
					{
						// TODO: better message
						throw new NotSupportedException("A message consumer method's return type must either be 'void' or '{0}'. Method: {1}".FormatInvariant(typeof(Task).FullName));
					}

					if (method.Name.EndsWith("Async") && !descriptor.IsAsync)
					{
						// TODO: better message
						throw new NotSupportedException("A synchronous message consumer method name should not end on 'Async'.");
					}

					var parameters = method.GetParameters();
					if (parameters.Length == 0)
					{
						// TODO: better message
						throw new NotSupportedException("A message consumer method must have at least one parameter identifying the message to consume.");
					}

					if (parameters.Any(x => x.IsRetval || x.IsOut || x.IsOptional))
					{
						// TODO: better message
						throw new NotSupportedException("'out', 'ref' and optional parameters are not allowed in consumer methods.");
					}

					var p = parameters[0];
					var messageType = p.ParameterType;

					if (messageType.IsGenericType && messageType.GetGenericTypeDefinition() == typeof(ConsumeContext<>))
					{
						messageType = messageType.GetGenericArguments()[0];
						descriptor.WithEnvelope = true;
					}

					// TODO: MyEvent and ConsumeContext<MyEvent> must throw "ambigous" exception

					if (messageType.IsPublic && (messageType.IsClass || messageType.IsInterface))
					{
						// The method signature is valid: add to dictionary.
						descriptor.MessageParameter = p;
						descriptor.Parameters = parameters.Skip(1).ToArray();
						descriptor.MessageType = messageType;
						descriptor.Method = method;

						_descriptorMap.Add(messageType, descriptor);
					}
					else
					{
						// TODO: message
						throw new NotSupportedException();
					}
				}	
			}
		}

		private IEnumerable<MethodInfo> FindMethods(EventConsumerMetadata metadata)
		{
			var methods = metadata.ContainerType.GetMethods(BindingFlags.Public | BindingFlags.Instance);

			var validNames = new HashSet<string>(new[] { "Handle", "HandleEvent", "Consume", "HandleAsync", "HandleEventAsync", "ConsumeAsync" });

			foreach (var method in methods)
			{
				if (validNames.Contains(method.Name))
				{
					yield return method;
				}
			}
		}

		public virtual IEnumerable<ConsumerDescriptor> GetConsumers(object message)
		{
			Guard.NotNull(message, nameof(message));

			var type = message.GetType();
			if (_descriptorMap.ContainsKey(type))
			{
				return _descriptorMap[type];
			}

			return Enumerable.Empty<ConsumerDescriptor>();
		}
	}
}
