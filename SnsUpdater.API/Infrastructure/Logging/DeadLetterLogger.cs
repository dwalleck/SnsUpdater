using System;
using System.IO;
using System.Web.Hosting;
using Newtonsoft.Json;
using SnsUpdater.API.Infrastructure.Messaging;

namespace SnsUpdater.API.Infrastructure.Logging
{
    public class DeadLetterLogger : IDeadLetterLogger
    {
        private readonly string _logPath;
        private readonly object _lockObject = new object();

        public DeadLetterLogger()
        {
            _logPath = Path.Combine(HostingEnvironment.MapPath("~/App_Data"), "DeadLetters");
            Directory.CreateDirectory(_logPath);
        }

        public void LogFailedMessage(SnsMessage message, Exception exception)
        {
            try
            {
                var deadLetter = new
                {
                    MessageId = message.Id,
                    CorrelationId = message.CorrelationId,
                    EntityType = message.EntityType,
                    EntityId = message.EntityId,
                    Subject = message.Subject,
                    Body = message.Body,
                    CreatedAt = message.CreatedAt,
                    RetryCount = message.RetryCount,
                    LastRetryAt = message.LastRetryAt,
                    FailedAt = DateTime.UtcNow,
                    Error = new
                    {
                        Message = exception.Message,
                        StackTrace = exception.StackTrace,
                        Type = exception.GetType().FullName
                    }
                };

                var fileName = $"deadletter_{DateTime.UtcNow:yyyyMMdd}_{message.Id}.json";
                var filePath = Path.Combine(_logPath, fileName);

                lock (_lockObject)
                {
                    File.WriteAllText(filePath, JsonConvert.SerializeObject(deadLetter, Formatting.Indented));
                }

                // Also log to trace for immediate visibility
                System.Diagnostics.Trace.TraceError(
                    $"Dead Letter: MessageId={message.Id}, CorrelationId={message.CorrelationId}, " +
                    $"EntityType={message.EntityType}, EntityId={message.EntityId}, " +
                    $"RetryCount={message.RetryCount}, Error={exception.Message}");
            }
            catch (Exception ex)
            {
                // Last resort logging
                System.Diagnostics.Trace.TraceError($"Failed to log dead letter: {ex}");
            }
        }
    }
}