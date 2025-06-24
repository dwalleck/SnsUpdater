using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using Unity;
using Unity.WebApi;
using MediatR;
using System.Reflection;
using Unity.Lifetime;
using Unity.AspNet.Mvc;

namespace SnsUpdater.API
{
    public static class UnityConfig
    {
        private static IUnityContainer _container;

        public static IUnityContainer Container => _container;

        public static void RegisterComponents()
        {
            _container = new UnityContainer();

            // Register MediatR 12.x
            // First register IServiceProvider adapter for Unity
            _container.RegisterInstance<IServiceProvider>(new UnityServiceProvider(_container));
            
            // Then register MediatR with the service provider
            _container.RegisterType<IMediator, Mediator>(new ContainerControlledLifetimeManager());
            
            // Note: MediatR 12 will use its default INotificationPublisher internally

            // Register all MediatR handlers from current assembly
            RegisterMediatRHandlers(_container);

            // Register infrastructure services
            _container.RegisterType<Infrastructure.Messaging.ISnsMessageQueue, Infrastructure.Messaging.SnsMessageQueue>(new ContainerControlledLifetimeManager());
            _container.RegisterType<Infrastructure.Aws.ISnsClient, Infrastructure.Aws.SnsClient>(new ContainerControlledLifetimeManager());
            _container.RegisterType<Infrastructure.Logging.IDeadLetterLogger, Infrastructure.Logging.DeadLetterLogger>(new ContainerControlledLifetimeManager());
            _container.RegisterType<Infrastructure.BackgroundServices.SnsBackgroundService>(new ContainerControlledLifetimeManager());

            // Set the dependency resolver
            GlobalConfiguration.Configuration.DependencyResolver = new Unity.WebApi.UnityDependencyResolver(_container);
        }

        private static void RegisterMediatRHandlers(IUnityContainer container)
        {
            var assembly = Assembly.GetExecutingAssembly();

            // MediatR handler registration via reflection
            // This automatically discovers and registers all command/query handlers and event handlers
            
            // Get all public, non-abstract classes from the current assembly
            var classTypes = assembly.ExportedTypes.Select(t => new { t, info = t.GetTypeInfo() })
                .Where(x => x.info.IsClass && !x.info.IsAbstract && !x.info.IsGenericTypeDefinition);

            // Register IRequestHandler<TRequest,TResponse> implementations (Commands/Queries)
            // These handle commands like CreatePersonCommand and return a response
            foreach (var handlerType in classTypes.Where(x => x.info.ImplementedInterfaces.Any(IsHandlerInterface)))
            {
                var implementedInterfaces = handlerType.info.ImplementedInterfaces.Where(IsHandlerInterface);
                foreach (var implementedInterface in implementedInterfaces)
                {
                    // Register as transient - new instance per request
                    container.RegisterType(implementedInterface, handlerType.t);
                }
            }

            // Register INotificationHandler<TNotification> implementations (Event Handlers)
            // These handle domain events like PersonCreatedEvent with no return value
            // Multiple handlers can handle the same event (pub-sub pattern)
            foreach (var handlerType in classTypes.Where(x => x.info.ImplementedInterfaces.Any(IsNotificationHandlerInterface)))
            {
                var implementedInterfaces = handlerType.info.ImplementedInterfaces.Where(IsNotificationHandlerInterface);
                foreach (var implementedInterface in implementedInterfaces)
                {
                    // Named registration allows multiple handlers for same notification
                    // Unity will resolve all handlers when MediatR publishes an event
                    container.RegisterType(implementedInterface, handlerType.t, handlerType.t.Name);
                }
            }
        }

        private static bool IsHandlerInterface(Type type)
        {
            if (!type.IsGenericType)
                return false;

            var typeDefinition = type.GetGenericTypeDefinition();
            return typeDefinition == typeof(IRequestHandler<,>) || typeDefinition == typeof(IRequestHandler<>);
        }

        private static bool IsNotificationHandlerInterface(Type type)
        {
            if (!type.IsGenericType)
                return false;

            var typeDefinition = type.GetGenericTypeDefinition();
            return typeDefinition == typeof(INotificationHandler<>);
        }
    }

    /// <summary>
    /// Unity ServiceProvider adapter for MediatR 12.x compatibility
    /// MediatR 12 removed ServiceFactory delegate in favor of IServiceProvider
    /// </summary>
    public class UnityServiceProvider : IServiceProvider
    {
        private readonly IUnityContainer _container;

        public UnityServiceProvider(IUnityContainer container)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
        }

        public object GetService(Type serviceType)
        {
            try
            {
                // Handle IEnumerable<T> requests (for multiple handlers)
                if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    var elementType = serviceType.GetGenericArguments()[0];
                    var instances = _container.ResolveAll(elementType);
                    
                    // Convert to array using LINQ
                    var instanceArray = instances.Cast<object>().ToArray();
                    
                    // Create a typed array and populate it
                    var typedArray = Array.CreateInstance(elementType, instanceArray.Length);
                    for (int i = 0; i < instanceArray.Length; i++)
                    {
                        typedArray.SetValue(instanceArray[i], i);
                    }
                    return typedArray;
                }

                // For single service resolution
                return _container.IsRegistered(serviceType) 
                    ? _container.Resolve(serviceType) 
                    : null;
            }
            catch (Exception)
            {
                // Unity throws exceptions for unregistered types
                // Return null as per IServiceProvider contract
                return null;
            }
        }
    }
}