using System;
using System.Configuration;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using MediatR;
using Newtonsoft.Json;
using SnsUpdater.API.Infrastructure.Filters;
using SnsUpdater.API.Infrastructure.Messaging;
using SnsUpdater.API.Infrastructure.Telemetry;

namespace SnsUpdater.API.Events
{
    public class PersonCreatedEventHandler : INotificationHandler<PersonCreatedEvent>
    {
        private readonly ISnsMessageQueue _messageQueue;
        private readonly string _topicArn;

        public PersonCreatedEventHandler(ISnsMessageQueue messageQueue)
        {
            _messageQueue = messageQueue ?? throw new ArgumentNullException(nameof(messageQueue));
            _topicArn = ConfigurationManager.AppSettings["AWS:TopicArn"];
        }

        public async Task Handle(PersonCreatedEvent notification, CancellationToken cancellationToken)
        {
            using var activity = TelemetryConfiguration.MessagingActivitySource.StartActivity("HandlePersonCreatedEvent");
            activity?.SetTag("event.type", "PersonCreated");
            activity?.SetTag("person.id", notification.PersonId);

            try
            {
                // Create the SNS message
                var messageBody = new
                {
                    EventType = "PersonCreated",
                    PersonId = notification.PersonId,
                    FirstName = notification.FirstName,
                    LastName = notification.LastName,
                    PhoneNumber = notification.PhoneNumber,
                    CreatedAt = notification.CreatedAt,
                    Timestamp = DateTime.UtcNow
                };

                // Get correlation ID from HTTP context if available
                string correlationId = null;
                if (HttpContext.Current != null && HttpContext.Current.Items.Contains(CorrelationIdActionFilter.CorrelationIdKey))
                {
                    correlationId = HttpContext.Current.Items[CorrelationIdActionFilter.CorrelationIdKey]?.ToString();
                }

                correlationId = correlationId ?? Guid.NewGuid().ToString();
                activity?.SetTag("correlation.id", correlationId);

                var snsMessage = new SnsMessage
                {
                    Subject = $"Person Created: {notification.FirstName} {notification.LastName}",
                    Body = JsonConvert.SerializeObject(messageBody),
                    EntityType = "Person",
                    EntityId = notification.PersonId.ToString(),
                    CorrelationId = correlationId,
                    MessageAttributes = JsonConvert.SerializeObject(new
                    {
                        eventType = new { DataType = "String", StringValue = "PersonCreated" },
                        personId = new { DataType = "Number", StringValue = notification.PersonId.ToString() }
                    })
                };

                // Queue the message for background processing
                await _messageQueue.EnqueueAsync(snsMessage, cancellationToken);
                
                // Record metrics
                TelemetryConfiguration.MessagesQueued.Add(1,
                    new System.Collections.Generic.KeyValuePair<string, object>("message.type", "PersonCreated"));
                
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.RecordException(ex);
                throw;
            }
        }
    }
}