using System;
using System.Configuration;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Exporter;
using System.Net.Http;
using OpenTelemetry.Logs;

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
        private static readonly GrpcHttpClientFactory _httpClientFactory = new GrpcHttpClientFactory();

        public static void Initialize()
        {
            var environment = ConfigurationManager.AppSettings["Environment"] ?? "development";
            var useOtlp = bool.Parse(ConfigurationManager.AppSettings["UseOtlpExporter"] ?? "false");
            var otlpEndpoint = ConfigurationManager.AppSettings["OtlpEndpoint"] ?? "http://localhost:4317";
            
            System.Diagnostics.Trace.WriteLine($"[OTEL] Initializing OpenTelemetry - UseOtlp: {useOtlp}, Endpoint: {otlpEndpoint}");
            
            var resourceBuilder = ResourceBuilder.CreateDefault()
                .AddService(ServiceName, serviceVersion: ServiceVersion)
                .AddAttributes(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, object>("environment", environment),
                    new System.Collections.Generic.KeyValuePair<string, object>("deployment.region", "us-east-1")
                });

            // Configure tracing
            var tracerProviderBuilder = Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(resourceBuilder)
                .AddSource(ApiActivitySource.Name)
                .AddSource(MessagingActivitySource.Name)
                .AddSource(BackgroundServiceActivitySource.Name)
                .AddAspNetInstrumentation()
                .AddHttpClientInstrumentation();

            if (useOtlp)
            {
                tracerProviderBuilder.AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otlpEndpoint);
                    options.Protocol = OtlpExportProtocol.HttpProtobuf;
                    // HttpClientFactory not needed for HTTP protocol
                });
            }
            else
            {
                tracerProviderBuilder.AddConsoleExporter();
            }

            _tracerProvider = tracerProviderBuilder.Build();
            System.Diagnostics.Trace.WriteLine($"[OTEL] TracerProvider built successfully");

            // Configure metrics
            var meterProviderBuilder = Sdk.CreateMeterProviderBuilder()
                .SetResourceBuilder(resourceBuilder)
                .AddMeter(ApiMeter.Name)
                .AddMeter(MessagingMeter.Name)
                .AddMeter(BackgroundServiceMeter.Name)
                .AddAspNetInstrumentation()
                .AddHttpClientInstrumentation();

            if (useOtlp)
            {
                meterProviderBuilder.AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otlpEndpoint.Replace("/v1/traces", "/v1/metrics"));
                    options.Protocol = OtlpExportProtocol.HttpProtobuf;
                    // HttpClientFactory not needed for HTTP protocol
                });
            }
            else
            {
                meterProviderBuilder.AddConsoleExporter();
            }

            _meterProvider = meterProviderBuilder.Build();
            System.Diagnostics.Trace.WriteLine($"[OTEL] MeterProvider built successfully");
            System.Diagnostics.Trace.WriteLine($"[OTEL] OpenTelemetry initialization complete");
        }

        public static void Shutdown()
        {
            _tracerProvider?.Dispose();
            _meterProvider?.Dispose();
        }
    }
}