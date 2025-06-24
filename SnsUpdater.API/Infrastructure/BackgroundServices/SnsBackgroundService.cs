using System;
using System.Configuration;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Hosting;
using SnsUpdater.API.Infrastructure.Aws;
using SnsUpdater.API.Infrastructure.Logging;
using SnsUpdater.API.Infrastructure.Messaging;
using SnsUpdater.API.Infrastructure.Telemetry;

namespace SnsUpdater.API.Infrastructure.BackgroundServices
{
    public class SnsBackgroundService : IRegisteredObject
    {
        private readonly ISnsMessageQueue _messageQueue;
        private readonly ISnsClient _snsClient;
        private readonly IDeadLetterLogger _deadLetterLogger;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly int _maxRetryAttempts;
        private readonly int _initialRetryDelayMs;
        private Task _backgroundTask;
        private readonly object _lock = new object();
        private bool _shuttingDown;

        public SnsBackgroundService(ISnsMessageQueue messageQueue, ISnsClient snsClient, IDeadLetterLogger deadLetterLogger)
        {
            _messageQueue = messageQueue ?? throw new ArgumentNullException(nameof(messageQueue));
            _snsClient = snsClient ?? throw new ArgumentNullException(nameof(snsClient));
            _deadLetterLogger = deadLetterLogger ?? throw new ArgumentNullException(nameof(deadLetterLogger));
            _cancellationTokenSource = new CancellationTokenSource();
            
            _maxRetryAttempts = int.Parse(ConfigurationManager.AppSettings["BackgroundService:MaxRetryAttempts"] ?? "3");
            _initialRetryDelayMs = int.Parse(ConfigurationManager.AppSettings["BackgroundService:InitialRetryDelayMs"] ?? "1000");

            HostingEnvironment.RegisterObject(this);
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_backgroundTask != null)
                    return;

                _backgroundTask = Task.Run(() => ProcessMessagesAsync(_cancellationTokenSource.Token));
            }
        }

        public void Stop(bool immediate)
        {
            lock (_lock)
            {
                _shuttingDown = true;
            }

            _cancellationTokenSource.Cancel();

            try
            {
                _backgroundTask?.Wait(TimeSpan.FromSeconds(10));
            }
            catch (Exception)
            {
                // Log error
            }

            HostingEnvironment.UnregisterObject(this);
        }

        private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var message = await _messageQueue.DequeueAsync(cancellationToken);
                    
                    // Update queue size metric
                    TelemetryConfiguration.QueueSize.Add(-1);
                    
                    await ProcessMessageWithRetryAsync(message, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Log error and continue processing
                    using (var activity = TelemetryConfiguration.BackgroundServiceActivitySource.StartActivity("ProcessMessageError"))
                    {
                        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                        activity?.SetTag("exception.type", ex.GetType().FullName);
                        activity?.SetTag("exception.message", ex.Message);
                    }
                    
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }

        private async Task ProcessMessageWithRetryAsync(SnsMessage message, CancellationToken cancellationToken)
        {
            // Create OpenTelemetry activity span for distributed tracing
            using (var activity = TelemetryConfiguration.BackgroundServiceActivitySource.StartActivity("ProcessSnsMessage"))
            {
                activity?.SetTag("message.id", message.Id);
                activity?.SetTag("message.correlationId", message.CorrelationId);
                activity?.SetTag("message.entityType", message.EntityType);
                activity?.SetTag("message.entityId", message.EntityId);

                var stopwatch = Stopwatch.StartNew();
                var retryDelay = _initialRetryDelayMs;

            // Retry loop - continues until success or max attempts reached
            while (message.RetryCount < _maxRetryAttempts)
            {
                try
                {
                    // Attempt to publish to SNS with distributed tracing
                    using (var publishActivity = TelemetryConfiguration.BackgroundServiceActivitySource.StartActivity("PublishToSns"))
                    {
                        publishActivity?.SetTag("sns.retryCount", message.RetryCount);
                        await _snsClient.PublishAsync(message, cancellationToken);
                    }
                    
                    // Success path - message published successfully
                    stopwatch.Stop();
                    
                    // Record success metrics for monitoring dashboards
                    TelemetryConfiguration.MessageProcessingDuration.Record(stopwatch.ElapsedMilliseconds,
                        new System.Collections.Generic.KeyValuePair<string, object>("status", "success"),
                        new System.Collections.Generic.KeyValuePair<string, object>("retries", message.RetryCount));
                    
                    TelemetryConfiguration.MessagesProcessed.Add(1,
                        new System.Collections.Generic.KeyValuePair<string, object>("status", "success"));
                    
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return; // Exit on success
                }
                catch (Exception ex)
                {
                    // Failure path - increment retry count and track timing
                    message.RetryCount++;
                    message.LastRetryAt = DateTime.UtcNow;
                    
                    // Record retry metric for monitoring retry rates
                    TelemetryConfiguration.MessagesRetried.Add(1,
                        new System.Collections.Generic.KeyValuePair<string, object>("attempt", message.RetryCount));

                    if (message.RetryCount >= _maxRetryAttempts)
                    {
                        // Final failure - all retries exhausted
                        // Message will be sent to dead letter queue for manual investigation
                        LogDeadLetter(message, ex);
                        
                        stopwatch.Stop();
                        
                        // Record failure metrics
                        TelemetryConfiguration.MessageProcessingDuration.Record(stopwatch.ElapsedMilliseconds,
                            new System.Collections.Generic.KeyValuePair<string, object>("status", "failed"),
                            new System.Collections.Generic.KeyValuePair<string, object>("retries", message.RetryCount));
                        
                        TelemetryConfiguration.MessagesDeadLettered.Add(1,
                            new System.Collections.Generic.KeyValuePair<string, object>("reason", ex.GetType().Name));
                        
                        // Mark span as error for tracing
                        activity?.SetStatus(ActivityStatusCode.Error, "Max retries exceeded");
                        activity?.SetTag("exception.type", ex.GetType().FullName);
                        activity?.SetTag("exception.message", ex.Message);
                        return; // Exit after dead lettering
                    }

                    // Exponential backoff implementation
                    // Delays: 1s, 2s, 4s, 8s, etc. to avoid overwhelming failing service
                    await Task.Delay(retryDelay, cancellationToken);
                    retryDelay *= 2; // Double delay for next retry
                }
            }
            }
        }

        private void LogDeadLetter(SnsMessage message, Exception exception)
        {
            _deadLetterLogger.LogFailedMessage(message, exception);
        }
    }
}