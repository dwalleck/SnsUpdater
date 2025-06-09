using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SnsUpdater.API.Infrastructure.Aws;
using SnsUpdater.API.Infrastructure.BackgroundServices;
using SnsUpdater.API.Infrastructure.Logging;
using SnsUpdater.API.Infrastructure.Messaging;

namespace SnsUpdater.API.Tests.Integration
{
    [TestClass]
    public class RetryAndCircuitBreakerTests
    {
        private Mock<ISnsClient> _snsClientMock;
        private Mock<IDeadLetterLogger> _deadLetterLoggerMock;
        private SnsMessageQueue _messageQueue;
        private SnsBackgroundService _backgroundService;

        [TestInitialize]
        public void Setup()
        {
            // Configure retry settings for faster testing
            System.Configuration.ConfigurationManager.AppSettings["BackgroundService:MaxRetryAttempts"] = "3";
            System.Configuration.ConfigurationManager.AppSettings["BackgroundService:InitialRetryDelayMs"] = "50";
            System.Configuration.ConfigurationManager.AppSettings["BackgroundService:ChannelCapacity"] = "100";

            _snsClientMock = new Mock<ISnsClient>();
            _deadLetterLoggerMock = new Mock<IDeadLetterLogger>();
            _messageQueue = new SnsMessageQueue();
            
            _backgroundService = new SnsBackgroundService(
                _messageQueue,
                _snsClientMock.Object,
                _deadLetterLoggerMock.Object);
        }

        [TestMethod]
        public async Task RetryLogic_FailsThreeTimes_LogsToDeadLetter()
        {
            // Arrange
            var message = CreateTestMessage();
            var exception = new InvalidOperationException("SNS publish failed");
            var publishAttempts = 0;

            _snsClientMock.Setup(c => c.PublishAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .Callback(() => publishAttempts++)
                .ThrowsAsync(exception);

            // Act
            await _messageQueue.EnqueueAsync(message);
            _backgroundService.Start();
            
            // Wait for retries to complete (50ms * 1 + 100ms * 2 + 200ms * 4 + processing time)
            await Task.Delay(600);
            _backgroundService.Stop(true);

            // Assert
            Assert.AreEqual(3, publishAttempts, "Should retry exactly 3 times");
            _deadLetterLoggerMock.Verify(
                d => d.LogFailedMessage(It.Is<SnsMessage>(m => m.Id == message.Id), exception), 
                Times.Once,
                "Should log to dead letter after max retries");
        }

        [TestMethod]
        public async Task RetryLogic_SucceedsOnSecondAttempt_DoesNotLogToDeadLetter()
        {
            // Arrange
            var message = CreateTestMessage();
            var publishAttempts = 0;

            _snsClientMock.Setup(c => c.PublishAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    publishAttempts++;
                    if (publishAttempts < 2)
                        throw new InvalidOperationException("SNS publish failed");
                    return Task.FromResult("message-id");
                });

            // Act
            await _messageQueue.EnqueueAsync(message);
            _backgroundService.Start();
            await Task.Delay(300);
            _backgroundService.Stop(true);

            // Assert
            Assert.AreEqual(2, publishAttempts, "Should succeed on second attempt");
            _deadLetterLoggerMock.Verify(
                d => d.LogFailedMessage(It.IsAny<SnsMessage>(), It.IsAny<Exception>()), 
                Times.Never,
                "Should not log to dead letter on success");
        }

        [TestMethod]
        public async Task RetryLogic_ExponentialBackoff_DelaysCorrectly()
        {
            // Arrange
            var message = CreateTestMessage();
            var publishTimes = new List<DateTime>();

            _snsClientMock.Setup(c => c.PublishAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .Callback(() => publishTimes.Add(DateTime.UtcNow))
                .ThrowsAsync(new InvalidOperationException("Failed"));

            // Act
            await _messageQueue.EnqueueAsync(message);
            _backgroundService.Start();
            await Task.Delay(600);
            _backgroundService.Stop(true);

            // Assert
            Assert.AreEqual(3, publishTimes.Count);
            
            // First retry after ~50ms
            var firstDelay = (publishTimes[1] - publishTimes[0]).TotalMilliseconds;
            Assert.IsTrue(firstDelay >= 40 && firstDelay <= 80, 
                $"First retry delay was {firstDelay}ms, expected ~50ms");
            
            // Second retry after ~100ms (exponential backoff)
            var secondDelay = (publishTimes[2] - publishTimes[1]).TotalMilliseconds;
            Assert.IsTrue(secondDelay >= 80 && secondDelay <= 140, 
                $"Second retry delay was {secondDelay}ms, expected ~100ms");
        }

        [TestMethod]
        public async Task RetryLogic_UpdatesMessageRetryInfo()
        {
            // Arrange
            var message = CreateTestMessage();
            var capturedMessages = new List<SnsMessage>();

            _snsClientMock.Setup(c => c.PublishAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .Callback<SnsMessage, CancellationToken>((msg, ct) => 
                {
                    capturedMessages.Add(new SnsMessage
                    {
                        Id = msg.Id,
                        RetryCount = msg.RetryCount,
                        LastRetryAt = msg.LastRetryAt
                    });
                })
                .ThrowsAsync(new InvalidOperationException("Failed"));

            // Act
            await _messageQueue.EnqueueAsync(message);
            _backgroundService.Start();
            await Task.Delay(600);
            _backgroundService.Stop(true);

            // Assert
            Assert.AreEqual(3, capturedMessages.Count);
            Assert.AreEqual(1, capturedMessages[0].RetryCount);
            Assert.AreEqual(2, capturedMessages[1].RetryCount);
            Assert.AreEqual(3, capturedMessages[2].RetryCount);
            
            Assert.IsNotNull(capturedMessages[0].LastRetryAt);
            Assert.IsNotNull(capturedMessages[1].LastRetryAt);
            Assert.IsNotNull(capturedMessages[2].LastRetryAt);
            
            // Verify retry timestamps are increasing
            Assert.IsTrue(capturedMessages[1].LastRetryAt > capturedMessages[0].LastRetryAt);
            Assert.IsTrue(capturedMessages[2].LastRetryAt > capturedMessages[1].LastRetryAt);
        }

        [TestMethod]
        public async Task CircuitBreaker_WhenOpen_MessageFailsImmediately()
        {
            // Arrange
            var message = CreateTestMessage();
            
            // Set circuit to open state
            _snsClientMock.Setup(c => c.IsCircuitOpen).Returns(true);
            _snsClientMock.Setup(c => c.PublishAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Circuit breaker is open"));

            // Act
            await _messageQueue.EnqueueAsync(message);
            _backgroundService.Start();
            await Task.Delay(600);
            _backgroundService.Stop(true);

            // Assert
            _snsClientMock.Verify(
                c => c.PublishAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()), 
                Times.Exactly(3),
                "Should still retry even with circuit open");
            
            _deadLetterLoggerMock.Verify(
                d => d.LogFailedMessage(It.IsAny<SnsMessage>(), It.IsAny<Exception>()), 
                Times.Once,
                "Should log to dead letter when circuit is open");
        }

        [TestMethod]
        public async Task MultipleMessages_ProcessedInOrder()
        {
            // Arrange
            var messages = new List<SnsMessage>
            {
                CreateTestMessage("First"),
                CreateTestMessage("Second"),
                CreateTestMessage("Third")
            };
            
            var processedMessages = new List<string>();
            _snsClientMock.Setup(c => c.PublishAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .Callback<SnsMessage, CancellationToken>((msg, ct) => processedMessages.Add(msg.Subject))
                .ReturnsAsync("message-id");

            // Act
            foreach (var message in messages)
            {
                await _messageQueue.EnqueueAsync(message);
            }
            
            _backgroundService.Start();
            await Task.Delay(300);
            _backgroundService.Stop(true);

            // Assert
            Assert.AreEqual(3, processedMessages.Count);
            Assert.AreEqual("First", processedMessages[0]);
            Assert.AreEqual("Second", processedMessages[1]);
            Assert.AreEqual("Third", processedMessages[2]);
        }

        [TestMethod]
        public async Task CancellationDuringRetry_StopsProcessing()
        {
            // Arrange
            var message = CreateTestMessage();
            var publishAttempts = 0;
            
            _snsClientMock.Setup(c => c.PublishAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .Callback(() => 
                {
                    publishAttempts++;
                    if (publishAttempts == 2)
                    {
                        // Stop service during retry
                        Task.Run(() => _backgroundService.Stop(true));
                    }
                })
                .ThrowsAsync(new InvalidOperationException("Failed"));

            // Act
            await _messageQueue.EnqueueAsync(message);
            _backgroundService.Start();
            await Task.Delay(500);

            // Assert
            Assert.IsTrue(publishAttempts >= 2 && publishAttempts <= 3, 
                $"Should stop during retry process, attempts: {publishAttempts}");
        }

        [TestMethod]
        public async Task DeadLetterLogging_ContainsAllRequiredInformation()
        {
            // Arrange
            var message = CreateTestMessage();
            message.CorrelationId = "test-correlation-id";
            message.EntityType = "Person";
            message.EntityId = "12345";
            
            var exception = new InvalidOperationException("Test failure");
            SnsMessage loggedMessage = null;
            Exception loggedException = null;

            _snsClientMock.Setup(c => c.PublishAsync(It.IsAny<SnsMessage>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);

            _deadLetterLoggerMock.Setup(d => d.LogFailedMessage(It.IsAny<SnsMessage>(), It.IsAny<Exception>()))
                .Callback<SnsMessage, Exception>((msg, ex) =>
                {
                    loggedMessage = msg;
                    loggedException = ex;
                });

            // Act
            await _messageQueue.EnqueueAsync(message);
            _backgroundService.Start();
            await Task.Delay(600);
            _backgroundService.Stop(true);

            // Assert
            Assert.IsNotNull(loggedMessage);
            Assert.AreEqual(message.Id, loggedMessage.Id);
            Assert.AreEqual(message.CorrelationId, loggedMessage.CorrelationId);
            Assert.AreEqual(message.EntityType, loggedMessage.EntityType);
            Assert.AreEqual(message.EntityId, loggedMessage.EntityId);
            Assert.AreEqual(3, loggedMessage.RetryCount);
            Assert.IsNotNull(loggedMessage.LastRetryAt);
            
            Assert.IsNotNull(loggedException);
            Assert.AreEqual(exception.Message, loggedException.Message);
        }

        private SnsMessage CreateTestMessage(string subject = "Test Message")
        {
            return new SnsMessage
            {
                Id = Guid.NewGuid(),
                Subject = subject,
                Body = "Test Body",
                EntityType = "Test",
                EntityId = Guid.NewGuid().ToString(),
                CorrelationId = Guid.NewGuid().ToString(),
                CreatedAt = DateTime.UtcNow,
                RetryCount = 0
            };
        }
    }
}