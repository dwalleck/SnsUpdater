using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Unity;
using Unity.Lifetime;
using MediatR;
using Moq;
using SnsUpdater.API.Controllers;
using SnsUpdater.API.Models;
using SnsUpdater.API.Infrastructure.Messaging;
using SnsUpdater.API.Infrastructure.Aws;
using SnsUpdater.API.Infrastructure.Logging;
using SnsUpdater.API.Commands;
using SnsUpdater.API.Events;
using System.Reflection;

namespace SnsUpdater.API.Tests.Integration
{
    [TestClass]
    public class PersonCreationIntegrationTests
    {
        private UnityContainer _container;
        private PeopleController _controller;
        private Mock<ISnsClient> _snsClientMock;
        private ISnsMessageQueue _messageQueue;

        [TestInitialize]
        public void Setup()
        {
            _container = new UnityContainer();
            
            // Register MediatR
            _container.RegisterType<IMediator, Mediator>(new ContainerControlledLifetimeManager());
            _container.RegisterInstance<ServiceFactory>(type =>
            {
                var enumerableType = type
                    .GetInterfaces()
                    .Concat(new[] { type })
                    .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IEnumerable<>));

                return enumerableType != null
                    ? _container.ResolveAll(enumerableType.GetGenericArguments()[0])
                    : _container.IsRegistered(type)
                        ? _container.Resolve(type)
                        : null;
            });

            // Register handlers
            RegisterMediatRHandlers(_container);

            // Register real queue implementation
            _messageQueue = new SnsMessageQueue();
            _container.RegisterInstance<ISnsMessageQueue>(_messageQueue);

            // Mock SNS client
            _snsClientMock = new Mock<ISnsClient>();
            _container.RegisterInstance<ISnsClient>(_snsClientMock.Object);

            // Mock dead letter logger
            var deadLetterLoggerMock = new Mock<IDeadLetterLogger>();
            _container.RegisterInstance<IDeadLetterLogger>(deadLetterLoggerMock.Object);

            // Setup configuration
            System.Configuration.ConfigurationManager.AppSettings["AWS:TopicArn"] = "arn:aws:sns:us-east-1:123456789012:test-topic";
            System.Configuration.ConfigurationManager.AppSettings["BackgroundService:ChannelCapacity"] = "100";

            // Create controller
            _controller = _container.Resolve<PeopleController>();
        }

        [TestMethod]
        public async Task PostPerson_ValidData_QueuesMessageSuccessfully()
        {
            // Arrange
            var person = new Person
            {
                FirstName = "Integration",
                LastName = "Test",
                PhoneNumber = "555-0123"
            };

            // Act
            var result = await _controller.Post(person);

            // Assert
            Assert.IsNotNull(result);
            var createdResult = result as System.Web.Http.Results.CreatedNegotiatedContentResult<CreatePersonResult>;
            Assert.IsNotNull(createdResult);
            Assert.IsTrue(createdResult.Content.Success);
            Assert.AreEqual("Integration", createdResult.Content.FirstName);
            Assert.AreEqual("Test", createdResult.Content.LastName);

            // Verify message was queued
            await Task.Delay(100); // Allow async operations to complete
            Assert.AreEqual(1, _messageQueue.Count);

            // Dequeue and verify message content
            var queuedMessage = await _messageQueue.DequeueAsync();
            Assert.IsNotNull(queuedMessage);
            Assert.AreEqual("Person Created: Integration Test", queuedMessage.Subject);
            Assert.AreEqual("Person", queuedMessage.EntityType);
            StringAssert.Contains(queuedMessage.Body, "Integration");
            StringAssert.Contains(queuedMessage.Body, "Test");
            StringAssert.Contains(queuedMessage.Body, "555-0123");
        }

        [TestMethod]
        public async Task PostPerson_InvalidData_DoesNotQueueMessage()
        {
            // Arrange
            var person = new Person
            {
                FirstName = "",
                LastName = "Test",
                PhoneNumber = "555-0123"
            };

            // Act
            var result = await _controller.Post(person);

            // Assert
            Assert.IsNotNull(result);
            var badRequestResult = result as System.Web.Http.Results.BadRequestErrorMessageResult;
            Assert.IsNotNull(badRequestResult);
            StringAssert.Contains(badRequestResult.Message, "First name and last name are required");

            // Verify no message was queued
            Assert.AreEqual(0, _messageQueue.Count);
        }

        [TestMethod]
        public async Task PostPerson_MultipleConcurrentRequests_QueuesAllMessages()
        {
            // Arrange
            var tasks = new Task<IHttpActionResult>[10];
            
            // Act - Create 10 people concurrently
            for (int i = 0; i < 10; i++)
            {
                var index = i;
                tasks[i] = Task.Run(async () =>
                {
                    var person = new Person
                    {
                        FirstName = $"User{index}",
                        LastName = $"Test{index}",
                        PhoneNumber = $"555-{index:D4}"
                    };
                    return await _controller.Post(person);
                });
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.AreEqual(10, results.Length);
            foreach (var result in results)
            {
                var createdResult = result as System.Web.Http.Results.CreatedNegotiatedContentResult<CreatePersonResult>;
                Assert.IsNotNull(createdResult);
                Assert.IsTrue(createdResult.Content.Success);
            }

            // Allow time for all messages to be queued
            await Task.Delay(200);
            
            // Verify all messages were queued
            Assert.AreEqual(10, _messageQueue.Count);

            // Verify each message has unique content
            var subjects = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < 10; i++)
            {
                var message = await _messageQueue.DequeueAsync();
                subjects.Add(message.Subject);
            }
            Assert.AreEqual(10, subjects.Count); // All subjects should be unique
        }

        [TestMethod]
        public async Task PostPerson_WithCorrelationId_PropagatesCorrelationId()
        {
            // Arrange
            var correlationId = Guid.NewGuid().ToString();
            var person = new Person
            {
                FirstName = "Correlation",
                LastName = "Test",
                PhoneNumber = "555-9999"
            };

            // Mock HTTP context with correlation ID
            System.Web.HttpContext.Current = new System.Web.HttpContext(
                new System.Web.HttpRequest("", "http://test.com", ""),
                new System.Web.HttpResponse(new System.IO.StringWriter())
            );
            System.Web.HttpContext.Current.Items[Infrastructure.Filters.CorrelationIdActionFilter.CorrelationIdKey] = correlationId;

            // Act
            var result = await _controller.Post(person);

            // Assert
            Assert.IsNotNull(result);
            await Task.Delay(100);
            
            var queuedMessage = await _messageQueue.DequeueAsync();
            Assert.IsNotNull(queuedMessage);
            Assert.AreEqual(correlationId, queuedMessage.CorrelationId);

            // Cleanup
            System.Web.HttpContext.Current = null;
        }

        [TestMethod]
        public async Task FullFlow_MessageQueued_CanBeProcessedByBackgroundService()
        {
            // Arrange
            var person = new Person
            {
                FirstName = "Background",
                LastName = "Processing",
                PhoneNumber = "555-7777"
            };

            var processedMessage = (SnsMessage)null;
            _snsClientMock.Setup(c => c.PublishAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .Callback<SnsMessage, CancellationToken>((msg, ct) => processedMessage = msg)
                .ReturnsAsync("message-id");

            // Create and start background service
            var backgroundService = _container.Resolve<Infrastructure.BackgroundServices.SnsBackgroundService>();
            backgroundService.Start();

            // Act
            var result = await _controller.Post(person);

            // Wait for background processing
            await Task.Delay(500);

            // Assert
            Assert.IsNotNull(result);
            var createdResult = result as System.Web.Http.Results.CreatedNegotiatedContentResult<CreatePersonResult>;
            Assert.IsNotNull(createdResult);
            Assert.IsTrue(createdResult.Content.Success);

            // Verify message was processed
            _snsClientMock.Verify(c => c.PublishAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.IsNotNull(processedMessage);
            Assert.AreEqual("Person Created: Background Processing", processedMessage.Subject);
            
            // Cleanup
            backgroundService.Stop(true);
        }

        [TestMethod]
        public async Task PostPerson_NullPerson_ReturnsBadRequest()
        {
            // Act
            var result = await _controller.Post(null);

            // Assert
            Assert.IsNotNull(result);
            var badRequestResult = result as System.Web.Http.Results.BadRequestErrorMessageResult;
            Assert.IsNotNull(badRequestResult);
            StringAssert.Contains(badRequestResult.Message, "Person data is required");

            // Verify no message was queued
            Assert.AreEqual(0, _messageQueue.Count);
        }

        private void RegisterMediatRHandlers(IUnityContainer container)
        {
            var assembly = Assembly.GetAssembly(typeof(CreatePersonCommand));

            // Register IRequestHandler<,> handlers
            var classTypes = assembly.ExportedTypes.Select(t => new { t, info = t.GetTypeInfo() })
                .Where(x => x.info.IsClass && !x.info.IsAbstract && !x.info.IsGenericTypeDefinition);

            foreach (var handlerType in classTypes.Where(x => x.info.ImplementedInterfaces.Any(IsHandlerInterface)))
            {
                var implementedInterfaces = handlerType.info.ImplementedInterfaces.Where(IsHandlerInterface);
                foreach (var implementedInterface in implementedInterfaces)
                {
                    container.RegisterType(implementedInterface, handlerType.t);
                }
            }

            // Register INotificationHandler<> handlers (for events)
            foreach (var handlerType in classTypes.Where(x => x.info.ImplementedInterfaces.Any(IsNotificationHandlerInterface)))
            {
                var implementedInterfaces = handlerType.info.ImplementedInterfaces.Where(IsNotificationHandlerInterface);
                foreach (var implementedInterface in implementedInterfaces)
                {
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
}