using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace SnsUpdater.API.Infrastructure.Telemetry
{
    public static class TelemetryConfiguration
    {
        public const string ServiceName = "SnsUpdater.API";
        public const string ServiceVersion = "1.0.0";
        
        // Activity sources for tracing
        public static readonly ActivitySource ApiActivitySource = new ActivitySource($"{ServiceName}.Api", ServiceVersion);
        public static readonly ActivitySource MessagingActivitySource = new ActivitySource($"{ServiceName}.Messaging", ServiceVersion);
        public static readonly ActivitySource BackgroundServiceActivitySource = new ActivitySource($"{ServiceName}.BackgroundService", ServiceVersion);
        
        // Meters for metrics
        public static readonly Meter ApiMeter = new Meter($"{ServiceName}.Api", ServiceVersion);
        public static readonly Meter MessagingMeter = new Meter($"{ServiceName}.Messaging", ServiceVersion);
        public static readonly Meter BackgroundServiceMeter = new Meter($"{ServiceName}.BackgroundService", ServiceVersion);

        // Metric instruments
        public static readonly Counter<long> PersonsCreated = ApiMeter.CreateCounter<long>("persons_created", "Count", "Number of persons created");
        public static readonly Counter<long> MessagesQueued = MessagingMeter.CreateCounter<long>("messages_queued", "Count", "Number of messages queued");
        public static readonly Counter<long> MessagesProcessed = BackgroundServiceMeter.CreateCounter<long>("messages_processed", "Count", "Number of messages processed");
        public static readonly Counter<long> MessagesRetried = BackgroundServiceMeter.CreateCounter<long>("messages_retried", "Count", "Number of message retries");
        public static readonly Counter<long> MessagesDeadLettered = BackgroundServiceMeter.CreateCounter<long>("messages_deadlettered", "Count", "Number of messages sent to dead letter");
        public static readonly Histogram<double> MessageProcessingDuration = BackgroundServiceMeter.CreateHistogram<double>("message_processing_duration", "ms", "Message processing duration in milliseconds");
        public static readonly UpDownCounter<int> QueueSize = MessagingMeter.CreateUpDownCounter<int>("queue_size", "Count", "Current queue size");
        public static readonly UpDownCounter<int> CircuitBreakerStatus = BackgroundServiceMeter.CreateUpDownCounter<int>("circuit_breaker_status", "Status", "Circuit breaker status (0=closed, 1=open)");

        private static TracerProvider _tracerProvider;
        private static MeterProvider _meterProvider;

        public static void Initialize()
        {
            var resourceBuilder = ResourceBuilder.CreateDefault()
                .AddService(ServiceName, serviceVersion: ServiceVersion)
                .AddAttributes(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, object>("environment", "production"),
                    new System.Collections.Generic.KeyValuePair<string, object>("deployment.region", "us-east-1")
                });

            // Configure tracing
            _tracerProvider = Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(resourceBuilder)
                .AddSource(ApiActivitySource.Name)
                .AddSource(MessagingActivitySource.Name)
                .AddSource(BackgroundServiceActivitySource.Name)
                .AddAspNetInstrumentation()
                .AddHttpClientInstrumentation()
                .AddConsoleExporter() // For development
                // .AddOtlpExporter() // For production - configure endpoint in config
                .Build();

            // Configure metrics
            _meterProvider = Sdk.CreateMeterProviderBuilder()
                .SetResourceBuilder(resourceBuilder)
                .AddMeter(ApiMeter.Name)
                .AddMeter(MessagingMeter.Name)
                .AddMeter(BackgroundServiceMeter.Name)
                .AddAspNetInstrumentation()
                .AddHttpClientInstrumentation()
                .AddConsoleExporter() // For development
                // .AddOtlpExporter() // For production - configure endpoint in config
                .Build();
        }

        public static void Shutdown()
        {
            _tracerProvider?.Dispose();
            _meterProvider?.Dispose();
        }
    }
}