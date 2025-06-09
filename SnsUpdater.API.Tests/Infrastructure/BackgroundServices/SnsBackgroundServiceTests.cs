using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SnsUpdater.API.Infrastructure.Aws;
using SnsUpdater.API.Infrastructure.BackgroundServices;
using SnsUpdater.API.Infrastructure.Logging;
using SnsUpdater.API.Infrastructure.Messaging;

namespace SnsUpdater.API.Tests.Infrastructure.BackgroundServices
{
    [TestClass]
    public class SnsBackgroundServiceTests
    {
        private Mock<ISnsMessageQueue> _messageQueueMock;
        private Mock<ISnsClient> _snsClientMock;
        private Mock<IDeadLetterLogger> _deadLetterLoggerMock;
        private SnsBackgroundService _service;

        [TestInitialize]
        public void Setup()
        {
            _messageQueueMock = new Mock<ISnsMessageQueue>();
            _snsClientMock = new Mock<ISnsClient>();
            _deadLetterLoggerMock = new Mock<IDeadLetterLogger>();

            // Set test configuration
            System.Configuration.ConfigurationManager.AppSettings["BackgroundService:MaxRetryAttempts"] = "3";
            System.Configuration.ConfigurationManager.AppSettings["BackgroundService:InitialRetryDelayMs"] = "100";

            _service = new SnsBackgroundService(
                _messageQueueMock.Object,
                _snsClientMock.Object,
                _deadLetterLoggerMock.Object);
        }

        [TestMethod]
        public void Constructor_ValidParameters_CreatesInstance()
        {
            // Assert
            Assert.IsNotNull(_service);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMessageQueue_ThrowsArgumentNullException()
        {
            // Act
            new SnsBackgroundService(null, _snsClientMock.Object, _deadLetterLoggerMock.Object);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullSnsClient_ThrowsArgumentNullException()
        {
            // Act
            new SnsBackgroundService(_messageQueueMock.Object, null, _deadLetterLoggerMock.Object);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullDeadLetterLogger_ThrowsArgumentNullException()
        {
            // Act
            new SnsBackgroundService(_messageQueueMock.Object, _snsClientMock.Object, null);
        }

        [TestMethod]
        public async Task ProcessMessage_SuccessfulPublish_DoesNotRetry()
        {
            // Arrange
            var message = CreateTestMessage();
            var cancellationToken = new CancellationTokenSource(1000).Token;

            _messageQueueMock.SetupSequence(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(message)
                .ThrowsAsync(new OperationCanceledException());

            _snsClientMock.Setup(c => c.PublishAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("message-id");

            // Act
            _service.Start();
            await Task.Delay(200); // Allow processing time
            _service.Stop(true);

            // Assert
            _snsClientMock.Verify(c => c.PublishAsync(message, It.IsAny<CancellationToken>()), Times.Once);
            _deadLetterLoggerMock.Verify(d => d.LogFailedMessage(It.IsAny<SnsMessage>(), It.IsAny<Exception>()), Times.Never);
        }

        [TestMethod]
        public async Task ProcessMessage_FailureWithRetry_RetriesCorrectNumberOfTimes()
        {
            // Arrange
            var message = CreateTestMessage();
            var exception = new InvalidOperationException("SNS publish failed");

            _messageQueueMock.SetupSequence(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(message)
                .ThrowsAsync(new OperationCanceledException());

            _snsClientMock.Setup(c => c.PublishAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);

            // Act
            _service.Start();
            await Task.Delay(1000); // Allow time for retries
            _service.Stop(true);

            // Assert
            _snsClientMock.Verify(c => c.PublishAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
            _deadLetterLoggerMock.Verify(d => d.LogFailedMessage(message, exception), Times.Once);
        }

        [TestMethod]
        public async Task ProcessMessage_PartialRetryThenSuccess_StopsRetrying()
        {
            // Arrange
            var message = CreateTestMessage();
            var exception = new InvalidOperationException("SNS publish failed");

            _messageQueueMock.SetupSequence(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(message)
                .ThrowsAsync(new OperationCanceledException());

            _snsClientMock.SetupSequence(c => c.PublishAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception)
                .ThrowsAsync(exception)
                .ReturnsAsync("message-id"); // Success on third try

            // Act
            _service.Start();
            await Task.Delay(500); // Allow time for retries
            _service.Stop(true);

            // Assert
            _snsClientMock.Verify(c => c.PublishAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
            _deadLetterLoggerMock.Verify(d => d.LogFailedMessage(It.IsAny<SnsMessage>(), It.IsAny<Exception>()), Times.Never);
        }

        [TestMethod]
        public void Stop_GracefulShutdown_StopsProcessing()
        {
            // Arrange
            _messageQueueMock.Setup(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken ct) =>
                {
                    await Task.Delay(Timeout.Infinite, ct);
                    return CreateTestMessage();
                });

            // Act
            _service.Start();
            Thread.Sleep(100); // Let it start
            _service.Stop(false); // Graceful shutdown

            // Assert - should complete without hanging
            Assert.IsTrue(true);
        }

        [TestMethod]
        public void Stop_ImmediateShutdown_StopsImmediately()
        {
            // Arrange
            _messageQueueMock.Setup(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken ct) =>
                {
                    await Task.Delay(Timeout.Infinite, ct);
                    return CreateTestMessage();
                });

            // Act
            _service.Start();
            Thread.Sleep(100); // Let it start
            _service.Stop(true); // Immediate shutdown

            // Assert - should complete without hanging
            Assert.IsTrue(true);
        }

        [TestMethod]
        public async Task ProcessMessage_UpdatesRetryCountAndLastRetryAt()
        {
            // Arrange
            var message = CreateTestMessage();
            var capturedMessages = new System.Collections.Generic.List<SnsMessage>();

            _messageQueueMock.SetupSequence(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(message)
                .ThrowsAsync(new OperationCanceledException());

            _snsClientMock.Setup(c => c.PublishAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .Callback<SnsMessage, CancellationToken>((msg, ct) => capturedMessages.Add(msg.Clone()))
                .ThrowsAsync(new InvalidOperationException("Failed"));

            // Act
            _service.Start();
            await Task.Delay(500); // Allow time for retries
            _service.Stop(true);

            // Assert
            Assert.AreEqual(3, capturedMessages.Count);
            Assert.AreEqual(1, capturedMessages[0].RetryCount);
            Assert.AreEqual(2, capturedMessages[1].RetryCount);
            Assert.AreEqual(3, capturedMessages[2].RetryCount);
            Assert.IsNotNull(capturedMessages[0].LastRetryAt);
            Assert.IsNotNull(capturedMessages[1].LastRetryAt);
            Assert.IsNotNull(capturedMessages[2].LastRetryAt);
        }

        [TestMethod]
        public async Task ProcessMessage_ExponentialBackoff_DelaysCorrectly()
        {
            // Arrange
            var message = CreateTestMessage();
            var startTime = DateTime.UtcNow;
            var publishTimes = new System.Collections.Generic.List<DateTime>();

            _messageQueueMock.SetupSequence(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(message)
                .ThrowsAsync(new OperationCanceledException());

            _snsClientMock.Setup(c => c.PublishAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .Callback<SnsMessage, CancellationToken>((msg, ct) => publishTimes.Add(DateTime.UtcNow))
                .ThrowsAsync(new InvalidOperationException("Failed"));

            // Act
            _service.Start();
            await Task.Delay(1000); // Allow time for retries with delays
            _service.Stop(true);

            // Assert
            Assert.AreEqual(3, publishTimes.Count);
            
            // First retry after ~100ms
            var firstRetryDelay = (publishTimes[1] - publishTimes[0]).TotalMilliseconds;
            Assert.IsTrue(firstRetryDelay >= 90 && firstRetryDelay <= 150, $"First retry delay was {firstRetryDelay}ms");
            
            // Second retry after ~200ms (exponential backoff)
            var secondRetryDelay = (publishTimes[2] - publishTimes[1]).TotalMilliseconds;
            Assert.IsTrue(secondRetryDelay >= 180 && secondRetryDelay <= 250, $"Second retry delay was {secondRetryDelay}ms");
        }

        [TestMethod]
        public void Configuration_UsesDefaultsWhenMissing()
        {
            // Arrange
            System.Configuration.ConfigurationManager.AppSettings["BackgroundService:MaxRetryAttempts"] = null;
            System.Configuration.ConfigurationManager.AppSettings["BackgroundService:InitialRetryDelayMs"] = null;

            // Act
            var service = new SnsBackgroundService(
                _messageQueueMock.Object,
                _snsClientMock.Object,
                _deadLetterLoggerMock.Object);

            // Assert - Service should create successfully with defaults
            Assert.IsNotNull(service);
        }

        private SnsMessage CreateTestMessage()
        {
            return new SnsMessage
            {
                Id = Guid.NewGuid(),
                Subject = "Test Subject",
                Body = "Test Body",
                EntityType = "Test",
                EntityId = "123",
                CorrelationId = Guid.NewGuid().ToString(),
                CreatedAt = DateTime.UtcNow,
                RetryCount = 0
            };
        }
    }

    // Extension to clone message for testing
    public static class SnsMessageExtensions
    {
        public static SnsMessage Clone(this SnsMessage original)
        {
            return new SnsMessage
            {
                Id = original.Id,
                Subject = original.Subject,
                Body = original.Body,
                MessageAttributes = original.MessageAttributes,
                CreatedAt = original.CreatedAt,
                RetryCount = original.RetryCount,
                LastRetryAt = original.LastRetryAt,
                CorrelationId = original.CorrelationId,
                EntityType = original.EntityType,
                EntityId = original.EntityId
            };
        }
    }
}