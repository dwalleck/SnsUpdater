using System;
using System.Configuration;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Newtonsoft.Json;
using SnsUpdater.API.Infrastructure.Messaging;
using SnsUpdater.API.Infrastructure.Telemetry;

namespace SnsUpdater.API.Infrastructure.Aws
{
    public class SnsClient : ISnsClient
    {
        private readonly string _region;
        private readonly string _roleArn;
        private readonly string _topicArn;
        private readonly string _roleSessionName;
        
        // Circuit breaker properties
        private readonly object _circuitLock = new object();
        private int _failureCount = 0;
        private DateTime _lastFailureTime = DateTime.MinValue;
        private readonly int _circuitBreakerThreshold = 5;
        private readonly TimeSpan _circuitBreakerTimeout = TimeSpan.FromMinutes(1);
        
        // Cached credentials
        private AssumeRoleResponse _cachedCredentials;
        private DateTime _credentialsExpiry = DateTime.MinValue;

        public bool IsCircuitOpen
        {
            get
            {
                lock (_circuitLock)
                {
                    // Circuit breaker pattern implementation:
                    // Opens (stops traffic) after threshold failures to prevent cascading failures
                    if (_failureCount >= _circuitBreakerThreshold)
                    {
                        var timeSinceLastFailure = DateTime.UtcNow - _lastFailureTime;
                        
                        // Circuit remains open for the timeout duration
                        if (timeSinceLastFailure < _circuitBreakerTimeout)
                        {
                            return true; // Circuit is OPEN - reject all requests
                        }
                        else
                        {
                            // Timeout has elapsed - attempt to close circuit
                            // This implements the "half-open" state implicitly
                            // Next request will test if the service has recovered
                            _failureCount = 0;
                            TelemetryConfiguration.CircuitBreakerStatus.Add(-1); // Set metric to 0 (closed)
                            return false; // Circuit is CLOSED - allow requests
                        }
                    }
                    return false; // Below threshold - circuit remains closed
                }
            }
        }

        public SnsClient()
        {
            _region = ConfigurationManager.AppSettings["AWS:Region"];
            _roleArn = ConfigurationManager.AppSettings["AWS:RoleArn"];
            _topicArn = ConfigurationManager.AppSettings["AWS:TopicArn"];
            _roleSessionName = ConfigurationManager.AppSettings["AWS:RoleSessionName"];
        }

        public async Task<string> PublishAsync(SnsMessage message, CancellationToken cancellationToken = default)
        {
            if (IsCircuitOpen)
            {
                throw new InvalidOperationException("Circuit breaker is open. SNS service is temporarily unavailable.");
            }

            try
            {
                var snsClient = await GetSnsClientAsync(cancellationToken);
                
                var publishRequest = new PublishRequest
                {
                    TopicArn = _topicArn,
                    Subject = message.Subject,
                    Message = message.Body
                };

                // Add message attributes if provided
                if (!string.IsNullOrEmpty(message.MessageAttributes))
                {
                    var attributes = JsonConvert.DeserializeObject<dynamic>(message.MessageAttributes);
                    foreach (var attr in attributes)
                    {
                        var attrValue = attr.Value;
                        publishRequest.MessageAttributes.Add(
                            attr.Name,
                            new MessageAttributeValue
                            {
                                DataType = (string)attrValue.DataType,
                                StringValue = (string)attrValue.StringValue
                            }
                        );
                    }
                }

                var response = await snsClient.PublishAsync(publishRequest, cancellationToken);
                
                // Reset failure count on success
                lock (_circuitLock)
                {
                    _failureCount = 0;
                }
                
                return response.MessageId;
            }
            catch (Exception ex)
            {
                RecordFailure();
                throw new InvalidOperationException($"Failed to publish message to SNS: {ex.Message}", ex);
            }
        }

        public void ResetCircuit()
        {
            lock (_circuitLock)
            {
                _failureCount = 0;
                _lastFailureTime = DateTime.MinValue;
            }
        }

        private async Task<IAmazonSimpleNotificationService> GetSnsClientAsync(CancellationToken cancellationToken)
        {
            var credentials = await GetAssumedRoleCredentialsAsync(cancellationToken);
            
            var sessionCredentials = new SessionAWSCredentials(
                credentials.Credentials.AccessKeyId,
                credentials.Credentials.SecretAccessKey,
                credentials.Credentials.SessionToken
            );

            var region = RegionEndpoint.GetBySystemName(_region);
            return new AmazonSimpleNotificationServiceClient(sessionCredentials, region);
        }

        private async Task<AssumeRoleResponse> GetAssumedRoleCredentialsAsync(CancellationToken cancellationToken)
        {
            // Credential caching strategy:
            // AWS STS calls are rate-limited and add latency
            // We cache credentials until 5 minutes before expiry to ensure they're always valid
            if (_cachedCredentials != null && DateTime.UtcNow < _credentialsExpiry)
            {
                return _cachedCredentials; // Use cached credentials - still valid
            }

            // Credentials expired or not yet obtained - assume role to get new ones
            using (var stsClient = new AmazonSecurityTokenServiceClient())
            {
                var assumeRoleRequest = new AssumeRoleRequest
                {
                    RoleArn = _roleArn,
                    RoleSessionName = _roleSessionName,
                    DurationSeconds = 3600 // 1 hour - maximum allowed for assumed roles
                };

                var response = await stsClient.AssumeRoleAsync(assumeRoleRequest, cancellationToken);
                
                // Cache the new credentials
                _cachedCredentials = response;
                
                // Set expiry 5 minutes before actual expiration
                // This ensures we never attempt to use expired credentials
                // which would result in authorization failures
                _credentialsExpiry = response.Credentials.Expiration.HasValue 
                    ? response.Credentials.Expiration.Value.AddMinutes(-5) 
                    : DateTime.UtcNow.AddHours(1);
                
                return response;
            }
        }

        private void RecordFailure()
        {
            lock (_circuitLock)
            {
                _failureCount++;
                _lastFailureTime = DateTime.UtcNow;
                
                if (_failureCount >= _circuitBreakerThreshold)
                {
                    TelemetryConfiguration.CircuitBreakerStatus.Add(1); // Set to 1 (open)
                }
            }
        }
    }
}