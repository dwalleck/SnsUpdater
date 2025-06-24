using System;
using System.Diagnostics;
using System.Linq;
using System.Web.Http;
using SnsUpdater.API.Infrastructure.Aws;
using SnsUpdater.API.Infrastructure.Messaging;
using SnsUpdater.API.Infrastructure.Telemetry;

namespace SnsUpdater.API.Controllers
{
    [RoutePrefix("api/health")]
    public class HealthController : ApiController
    {
        private readonly ISnsMessageQueue _messageQueue;
        private readonly ISnsClient _snsClient;

        public HealthController(ISnsMessageQueue messageQueue, ISnsClient snsClient)
        {
            _messageQueue = messageQueue ?? throw new ArgumentNullException(nameof(messageQueue));
            _snsClient = snsClient ?? throw new ArgumentNullException(nameof(snsClient));
        }

        [HttpGet]
        [Route("")]
        public IHttpActionResult GetHealth()
        {
            System.Diagnostics.Trace.WriteLine($"[OTEL] Health endpoint called");
            using (var activity = TelemetryConfiguration.ApiActivitySource.StartActivity("HealthCheck"))
            {
                System.Diagnostics.Trace.WriteLine($"[OTEL] Activity created: {activity?.Id}, IsRecording: {activity?.IsAllDataRequested}");
                var health = new
                {
                    Status = "Healthy",
                    Timestamp = DateTime.UtcNow,
                    Services = new
                    {
                        MessageQueue = new
                        {
                            Status = "Healthy",
                            QueuedMessages = _messageQueue.Count
                        },
                        SnsClient = new
                        {
                            Status = _snsClient.IsCircuitOpen ? "Unhealthy - Circuit Open" : "Healthy",
                            CircuitBreakerOpen = _snsClient.IsCircuitOpen
                        }
                    },
                    Telemetry = new
                    {
                        ActiveTraces = Activity.Current != null,
                        ActivitySources = new[]
                        {
                            TelemetryConfiguration.ApiActivitySource.Name,
                            TelemetryConfiguration.MessagingActivitySource.Name,
                            TelemetryConfiguration.BackgroundServiceActivitySource.Name
                        }
                    }
                };

                return Ok(health);
            }
        }

        [HttpGet]
        [Route("queue")]
        public IHttpActionResult GetQueueStatus()
        {
            var queueStatus = new
            {
                QueuedMessages = _messageQueue.Count,
                Timestamp = DateTime.UtcNow
            };

            return Ok(queueStatus);
        }

        [HttpPost]
        [Route("circuit-breaker/reset")]
        public IHttpActionResult ResetCircuitBreaker()
        {
            _snsClient.ResetCircuit();
            return Ok(new { Message = "Circuit breaker reset successfully", Timestamp = DateTime.UtcNow });
        }

        [HttpGet]
        [Route("test")]
        public IHttpActionResult TestTrace()
        {
            System.Diagnostics.Trace.WriteLine("[OTEL] TestTrace endpoint called");
            
            // Force a trace with all three activity sources
            using (var apiActivity = TelemetryConfiguration.ApiActivitySource.StartActivity("TestTrace.API", ActivityKind.Server))
            {
                apiActivity?.SetTag("test.type", "manual");
                apiActivity?.SetTag("test.timestamp", DateTime.UtcNow.ToString("o"));
                System.Diagnostics.Trace.WriteLine($"[OTEL] API Activity: {apiActivity?.Id}, Recording: {apiActivity?.IsAllDataRequested}");
                
                using (var msgActivity = TelemetryConfiguration.MessagingActivitySource.StartActivity("TestTrace.Messaging", ActivityKind.Producer))
                {
                    msgActivity?.SetTag("test.queue.size", _messageQueue.Count);
                    System.Diagnostics.Trace.WriteLine($"[OTEL] Messaging Activity: {msgActivity?.Id}, Recording: {msgActivity?.IsAllDataRequested}");
                    
                    using (var bgActivity = TelemetryConfiguration.BackgroundServiceActivitySource.StartActivity("TestTrace.Background", ActivityKind.Internal))
                    {
                        bgActivity?.SetTag("test.circuit.open", _snsClient.IsCircuitOpen);
                        System.Diagnostics.Trace.WriteLine($"[OTEL] Background Activity: {bgActivity?.Id}, Recording: {bgActivity?.IsAllDataRequested}");
                        
                        // Also increment a metric
                        TelemetryConfiguration.PersonsCreated.Add(1, 
                            new System.Collections.Generic.KeyValuePair<string, object>("test", true));
                        
                        return Ok(new 
                        { 
                            Message = "Test trace created",
                            Activities = new
                            {
                                Api = apiActivity?.Id,
                                ApiRecording = apiActivity?.IsAllDataRequested,
                                Messaging = msgActivity?.Id,
                                MessagingRecording = msgActivity?.IsAllDataRequested,
                                Background = bgActivity?.Id,
                                BackgroundRecording = bgActivity?.IsAllDataRequested
                            },
                            Timestamp = DateTime.UtcNow 
                        });
                    }
                }
            }
        }
    }
}