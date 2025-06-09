using System;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SnsUpdater.API.Events;
using SnsUpdater.API.Infrastructure.Filters;
using SnsUpdater.API.Infrastructure.Messaging;

namespace SnsUpdater.API.Tests.Events
{
    [TestClass]
    public class PersonCreatedEventHandlerTests
    {
        private Mock<ISnsMessageQueue> _messageQueueMock;
        private PersonCreatedEventHandler _handler;
        private const string TestTopicArn = "arn:aws:sns:us-east-1:123456789012:test-topic";

        [TestInitialize]
        public void Setup()
        {
            _messageQueueMock = new Mock<ISnsMessageQueue>();
            
            // Mock ConfigurationManager for testing
            System.Configuration.ConfigurationManager.AppSettings["AWS:TopicArn"] = TestTopicArn;
            
            _handler = new PersonCreatedEventHandler(_messageQueueMock.Object);
        }

        [TestMethod]
        public async Task Handle_ValidEvent_QueuesMessageSuccessfully()
        {
            // Arrange
            var personCreatedEvent = new PersonCreatedEvent
            {
                PersonId = 1234,
                FirstName = "John",
                LastName = "Doe",
                PhoneNumber = "123-456-7890",
                CreatedAt = DateTime.UtcNow
            };

            SnsMessage queuedMessage = null;
            _messageQueueMock.Setup(q => q.EnqueueAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .Callback<SnsMessage, CancellationToken>((msg, ct) => queuedMessage = msg)
                .Returns(Task.CompletedTask);

            // Act
            await _handler.Handle(personCreatedEvent, CancellationToken.None);

            // Assert
            _messageQueueMock.Verify(q => q.EnqueueAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()), Times.Once);
            
            Assert.IsNotNull(queuedMessage);
            Assert.AreEqual("Person Created: John Doe", queuedMessage.Subject);
            Assert.AreEqual("Person", queuedMessage.EntityType);
            Assert.AreEqual("1234", queuedMessage.EntityId);
            Assert.IsNotNull(queuedMessage.CorrelationId);
            Assert.IsTrue(Guid.TryParse(queuedMessage.CorrelationId, out _));
        }

        [TestMethod]
        public async Task Handle_ValidEvent_MessageBodyContainsCorrectData()
        {
            // Arrange
            var createdAt = DateTime.UtcNow;
            var personCreatedEvent = new PersonCreatedEvent
            {
                PersonId = 5678,
                FirstName = "Jane",
                LastName = "Smith",
                PhoneNumber = "555-123-4567",
                CreatedAt = createdAt
            };

            SnsMessage queuedMessage = null;
            _messageQueueMock.Setup(q => q.EnqueueAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .Callback<SnsMessage, CancellationToken>((msg, ct) => queuedMessage = msg)
                .Returns(Task.CompletedTask);

            // Act
            await _handler.Handle(personCreatedEvent, CancellationToken.None);

            // Assert
            Assert.IsNotNull(queuedMessage);
            
            var body = JObject.Parse(queuedMessage.Body);
            Assert.AreEqual("PersonCreated", body["EventType"].Value<string>());
            Assert.AreEqual(5678, body["PersonId"].Value<int>());
            Assert.AreEqual("Jane", body["FirstName"].Value<string>());
            Assert.AreEqual("Smith", body["LastName"].Value<string>());
            Assert.AreEqual("555-123-4567", body["PhoneNumber"].Value<string>());
            Assert.AreEqual(createdAt, body["CreatedAt"].Value<DateTime>());
            Assert.IsNotNull(body["Timestamp"]);
        }

        [TestMethod]
        public async Task Handle_EventWithNullPhoneNumber_HandlesCorrectly()
        {
            // Arrange
            var personCreatedEvent = new PersonCreatedEvent
            {
                PersonId = 9999,
                FirstName = "Test",
                LastName = "User",
                PhoneNumber = null,
                CreatedAt = DateTime.UtcNow
            };

            SnsMessage queuedMessage = null;
            _messageQueueMock.Setup(q => q.EnqueueAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .Callback<SnsMessage, CancellationToken>((msg, ct) => queuedMessage = msg)
                .Returns(Task.CompletedTask);

            // Act
            await _handler.Handle(personCreatedEvent, CancellationToken.None);

            // Assert
            Assert.IsNotNull(queuedMessage);
            var body = JObject.Parse(queuedMessage.Body);
            Assert.IsNull(body["PhoneNumber"].Value<string>());
        }

        [TestMethod]
        public async Task Handle_ValidEvent_MessageAttributesAreCorrectlyFormatted()
        {
            // Arrange
            var personCreatedEvent = new PersonCreatedEvent
            {
                PersonId = 4321,
                FirstName = "Test",
                LastName = "Person",
                PhoneNumber = "111-222-3333",
                CreatedAt = DateTime.UtcNow
            };

            SnsMessage queuedMessage = null;
            _messageQueueMock.Setup(q => q.EnqueueAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .Callback<SnsMessage, CancellationToken>((msg, ct) => queuedMessage = msg)
                .Returns(Task.CompletedTask);

            // Act
            await _handler.Handle(personCreatedEvent, CancellationToken.None);

            // Assert
            Assert.IsNotNull(queuedMessage);
            Assert.IsNotNull(queuedMessage.MessageAttributes);
            
            dynamic attributes = JsonConvert.DeserializeObject(queuedMessage.MessageAttributes);
            Assert.AreEqual("String", (string)attributes.eventType.DataType);
            Assert.AreEqual("PersonCreated", (string)attributes.eventType.StringValue);
            Assert.AreEqual("Number", (string)attributes.personId.DataType);
            Assert.AreEqual("4321", (string)attributes.personId.StringValue);
        }

        [TestMethod]
        public async Task Handle_WithHttpContextAndCorrelationId_UsesExistingCorrelationId()
        {
            // Arrange
            var expectedCorrelationId = Guid.NewGuid().ToString();
            HttpContext.Current = new HttpContext(
                new HttpRequest("", "http://test.com", ""),
                new HttpResponse(new System.IO.StringWriter())
            );
            HttpContext.Current.Items[CorrelationIdActionFilter.CorrelationIdKey] = expectedCorrelationId;

            var personCreatedEvent = new PersonCreatedEvent
            {
                PersonId = 1111,
                FirstName = "Correlation",
                LastName = "Test",
                PhoneNumber = "999-888-7777",
                CreatedAt = DateTime.UtcNow
            };

            SnsMessage queuedMessage = null;
            _messageQueueMock.Setup(q => q.EnqueueAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .Callback<SnsMessage, CancellationToken>((msg, ct) => queuedMessage = msg)
                .Returns(Task.CompletedTask);

            // Act
            await _handler.Handle(personCreatedEvent, CancellationToken.None);

            // Assert
            Assert.IsNotNull(queuedMessage);
            Assert.AreEqual(expectedCorrelationId, queuedMessage.CorrelationId);

            // Cleanup
            HttpContext.Current = null;
        }

        [TestMethod]
        public async Task Handle_WithoutHttpContext_GeneratesNewCorrelationId()
        {
            // Arrange
            HttpContext.Current = null;

            var personCreatedEvent = new PersonCreatedEvent
            {
                PersonId = 2222,
                FirstName = "No",
                LastName = "Context",
                PhoneNumber = "111-111-1111",
                CreatedAt = DateTime.UtcNow
            };

            SnsMessage queuedMessage = null;
            _messageQueueMock.Setup(q => q.EnqueueAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .Callback<SnsMessage, CancellationToken>((msg, ct) => queuedMessage = msg)
                .Returns(Task.CompletedTask);

            // Act
            await _handler.Handle(personCreatedEvent, CancellationToken.None);

            // Assert
            Assert.IsNotNull(queuedMessage);
            Assert.IsNotNull(queuedMessage.CorrelationId);
            Assert.IsTrue(Guid.TryParse(queuedMessage.CorrelationId, out _));
        }

        [TestMethod]
        public async Task Handle_CancellationTokenIsPropagated()
        {
            // Arrange
            var personCreatedEvent = new PersonCreatedEvent
            {
                PersonId = 3333,
                FirstName = "Cancel",
                LastName = "Test",
                PhoneNumber = "222-333-4444",
                CreatedAt = DateTime.UtcNow
            };

            var cts = new CancellationTokenSource();
            CancellationToken capturedToken = default;

            _messageQueueMock.Setup(q => q.EnqueueAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .Callback<SnsMessage, CancellationToken>((msg, ct) => capturedToken = ct)
                .Returns(Task.CompletedTask);

            // Act
            await _handler.Handle(personCreatedEvent, cts.Token);

            // Assert
            Assert.AreEqual(cts.Token, capturedToken);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMessageQueue_ThrowsArgumentNullException()
        {
            // Act
            new PersonCreatedEventHandler(null);
        }

        [TestMethod]
        public async Task Handle_MessagePropertiesAreSetCorrectly()
        {
            // Arrange
            var personCreatedEvent = new PersonCreatedEvent
            {
                PersonId = 7777,
                FirstName = "Properties",
                LastName = "Test",
                PhoneNumber = "777-777-7777",
                CreatedAt = DateTime.UtcNow
            };

            SnsMessage queuedMessage = null;
            _messageQueueMock.Setup(q => q.EnqueueAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .Callback<SnsMessage, CancellationToken>((msg, ct) => queuedMessage = msg)
                .Returns(Task.CompletedTask);

            // Act
            await _handler.Handle(personCreatedEvent, CancellationToken.None);

            // Assert
            Assert.IsNotNull(queuedMessage);
            Assert.IsNotNull(queuedMessage.Id);
            Assert.AreNotEqual(Guid.Empty, queuedMessage.Id);
            Assert.IsTrue(queuedMessage.CreatedAt <= DateTime.UtcNow);
            Assert.IsTrue(queuedMessage.CreatedAt >= DateTime.UtcNow.AddSeconds(-5));
            Assert.AreEqual(0, queuedMessage.RetryCount);
            Assert.IsNull(queuedMessage.LastRetryAt);
        }

        [TestMethod]
        public async Task Handle_SpecialCharactersInNames_HandlesCorrectly()
        {
            // Arrange
            var personCreatedEvent = new PersonCreatedEvent
            {
                PersonId = 8888,
                FirstName = "Jean-Pierre",
                LastName = "O'Connor",
                PhoneNumber = "+1 (555) 123-4567",
                CreatedAt = DateTime.UtcNow
            };

            SnsMessage queuedMessage = null;
            _messageQueueMock.Setup(q => q.EnqueueAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .Callback<SnsMessage, CancellationToken>((msg, ct) => queuedMessage = msg)
                .Returns(Task.CompletedTask);

            // Act
            await _handler.Handle(personCreatedEvent, CancellationToken.None);

            // Assert
            Assert.IsNotNull(queuedMessage);
            Assert.AreEqual("Person Created: Jean-Pierre O'Connor", queuedMessage.Subject);
            
            var body = JObject.Parse(queuedMessage.Body);
            Assert.AreEqual("Jean-Pierre", body["FirstName"].Value<string>());
            Assert.AreEqual("O'Connor", body["LastName"].Value<string>());
            Assert.AreEqual("+1 (555) 123-4567", body["PhoneNumber"].Value<string>());
        }
    }
}