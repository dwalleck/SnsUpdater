using System;

namespace SnsUpdater.API.Infrastructure.Messaging
{
    public class SnsMessage
    {
        public Guid Id { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        public string MessageAttributes { get; set; }
        public DateTime CreatedAt { get; set; }
        public int RetryCount { get; set; }
        public DateTime? LastRetryAt { get; set; }
        public string CorrelationId { get; set; }
        public string EntityType { get; set; }
        public string EntityId { get; set; }

        public SnsMessage()
        {
            Id = Guid.NewGuid();
            CreatedAt = DateTime.UtcNow;
            RetryCount = 0;
            CorrelationId = Guid.NewGuid().ToString();
        }
    }
}