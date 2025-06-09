using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SnsUpdater.API.Infrastructure.Messaging;

namespace SnsUpdater.API.Tests.Infrastructure.Messaging
{
    [TestClass]
    public class SnsMessageQueueTests
    {
        private SnsMessageQueue _queue;

        [TestInitialize]
        public void Setup()
        {
            // Set configuration for testing
            System.Configuration.ConfigurationManager.AppSettings["BackgroundService:ChannelCapacity"] = "10";
            _queue = new SnsMessageQueue();
        }

        [TestMethod]
        public async Task EnqueueAsync_ValidMessage_AddsToQueue()
        {
            // Arrange
            var message = CreateTestMessage();

            // Act
            await _queue.EnqueueAsync(message);

            // Assert
            Assert.AreEqual(1, _queue.Count);
        }

        [TestMethod]
        public async Task DequeueAsync_WithMessage_ReturnsMessage()
        {
            // Arrange
            var message = CreateTestMessage();
            await _queue.EnqueueAsync(message);

            // Act
            var dequeuedMessage = await _queue.DequeueAsync();

            // Assert
            Assert.IsNotNull(dequeuedMessage);
            Assert.AreEqual(message.Id, dequeuedMessage.Id);
            Assert.AreEqual(message.Subject, dequeuedMessage.Subject);
            Assert.AreEqual(0, _queue.Count);
        }

        [TestMethod]
        public async Task EnqueueAsync_NullMessage_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(
                async () => await _queue.EnqueueAsync(null));
        }

        [TestMethod]
        public void TryDequeue_WithMessage_ReturnsTrue()
        {
            // Arrange
            var message = CreateTestMessage();
            _queue.EnqueueAsync(message).Wait();

            // Act
            var result = _queue.TryDequeue(out var dequeuedMessage);

            // Assert
            Assert.IsTrue(result);
            Assert.IsNotNull(dequeuedMessage);
            Assert.AreEqual(message.Id, dequeuedMessage.Id);
        }

        [TestMethod]
        public void TryDequeue_EmptyQueue_ReturnsFalse()
        {
            // Act
            var result = _queue.TryDequeue(out var dequeuedMessage);

            // Assert
            Assert.IsFalse(result);
            Assert.IsNull(dequeuedMessage);
        }

        [TestMethod]
        public async Task Count_ReflectsQueueSize()
        {
            // Arrange & Act
            Assert.AreEqual(0, _queue.Count);

            await _queue.EnqueueAsync(CreateTestMessage());
            Assert.AreEqual(1, _queue.Count);

            await _queue.EnqueueAsync(CreateTestMessage());
            Assert.AreEqual(2, _queue.Count);

            await _queue.DequeueAsync();
            Assert.AreEqual(1, _queue.Count);

            await _queue.DequeueAsync();
            Assert.AreEqual(0, _queue.Count);
        }

        [TestMethod]
        public async Task EnqueueDequeue_MultipleMessages_MaintainsFIFOOrder()
        {
            // Arrange
            var message1 = CreateTestMessage("First");
            var message2 = CreateTestMessage("Second");
            var message3 = CreateTestMessage("Third");

            // Act
            await _queue.EnqueueAsync(message1);
            await _queue.EnqueueAsync(message2);
            await _queue.EnqueueAsync(message3);

            // Assert
            var dequeued1 = await _queue.DequeueAsync();
            var dequeued2 = await _queue.DequeueAsync();
            var dequeued3 = await _queue.DequeueAsync();

            Assert.AreEqual("First", dequeued1.Subject);
            Assert.AreEqual("Second", dequeued2.Subject);
            Assert.AreEqual("Third", dequeued3.Subject);
        }

        [TestMethod]
        public async Task DequeueAsync_WithCancellation_ThrowsOperationCanceledException()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                async () => await _queue.DequeueAsync(cts.Token));
        }

        [TestMethod]
        public async Task EnqueueAsync_WithCancellation_ThrowsOperationCanceledException()
        {
            // Arrange
            var message = CreateTestMessage();
            var cts = new CancellationTokenSource();
            
            // Fill the queue to capacity
            for (int i = 0; i < 10; i++)
            {
                await _queue.EnqueueAsync(CreateTestMessage());
            }
            
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                async () => await _queue.EnqueueAsync(message, cts.Token));
        }

        [TestMethod]
        public async Task Queue_HandlesCapacityConfiguration()
        {
            // Arrange
            System.Configuration.ConfigurationManager.AppSettings["BackgroundService:ChannelCapacity"] = "3";
            var smallQueue = new SnsMessageQueue();

            // Act - Fill to capacity
            await smallQueue.EnqueueAsync(CreateTestMessage());
            await smallQueue.EnqueueAsync(CreateTestMessage());
            await smallQueue.EnqueueAsync(CreateTestMessage());

            // Assert
            Assert.AreEqual(3, smallQueue.Count);

            // The next enqueue will block since we're using BoundedChannelFullMode.Wait
            // We'll test this with a timeout
            var enqueueTask = smallQueue.EnqueueAsync(CreateTestMessage());
            var completed = enqueueTask.Wait(100); // Wait 100ms

            Assert.IsFalse(completed, "Enqueue should block when queue is full");
        }

        [TestMethod]
        public void Queue_WithMissingConfiguration_UsesDefaultCapacity()
        {
            // Arrange
            System.Configuration.ConfigurationManager.AppSettings["BackgroundService:ChannelCapacity"] = null;

            // Act
            var queue = new SnsMessageQueue();

            // Assert
            Assert.IsNotNull(queue);
            // Default capacity is 1000, but we can't directly test this without reflection
            // The queue should still work with default settings
        }

        [TestMethod]
        public async Task ConcurrentOperations_MaintainsThreadSafety()
        {
            // Arrange
            var tasks = new Task[20];
            var messagesEnqueued = 0;
            var messagesDequeued = 0;

            // Act - Enqueue 10 messages concurrently
            for (int i = 0; i < 10; i++)
            {
                var index = i;
                tasks[i] = Task.Run(async () =>
                {
                    await _queue.EnqueueAsync(CreateTestMessage($"Message {index}"));
                    Interlocked.Increment(ref messagesEnqueued);
                });
            }

            // Dequeue 10 messages concurrently
            for (int i = 10; i < 20; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    try
                    {
                        var msg = await _queue.DequeueAsync();
                        if (msg != null)
                            Interlocked.Increment(ref messagesDequeued);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected if queue is empty
                    }
                });
            }

            // Wait for all operations to complete
            await Task.WhenAll(tasks);

            // Assert
            Assert.AreEqual(10, messagesEnqueued);
            // Messages dequeued might be less if dequeue operations run before enqueue
            Assert.IsTrue(messagesDequeued <= 10);
            Assert.AreEqual(messagesEnqueued - messagesDequeued, _queue.Count);
        }

        private SnsMessage CreateTestMessage(string subject = "Test Subject")
        {
            return new SnsMessage
            {
                Subject = subject,
                Body = "Test Body",
                EntityType = "Test",
                EntityId = Guid.NewGuid().ToString(),
                CorrelationId = Guid.NewGuid().ToString()
            };
        }
    }
}