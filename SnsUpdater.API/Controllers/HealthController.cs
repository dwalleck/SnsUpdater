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
            using var activity = TelemetryConfiguration.ApiActivitySource.StartActivity("HealthCheck");
            
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
    }
}