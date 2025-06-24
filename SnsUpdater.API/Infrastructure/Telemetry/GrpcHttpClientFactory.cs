using System;
using System.Net.Http;

namespace SnsUpdater.API.Infrastructure.Telemetry
{
    /// <summary>
    /// Custom HttpClientFactory for OpenTelemetry OTLP gRPC exporter on .NET Framework.
    /// This is required because the default HTTP client factory is not supported on .NET Framework
    /// when using OtlpExportProtocol.Grpc.
    /// </summary>
    public class GrpcHttpClientFactory : IHttpClientFactory
    {
        private static readonly HttpClient _httpClient;

        static GrpcHttpClientFactory()
        {
            // Create a single HttpClient instance for gRPC communication
            var handler = new HttpClientHandler
            {
                // Allow self-signed certificates in development
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };

            _httpClient = new HttpClient(handler)
            {
                // Set a reasonable timeout for OTLP exports
                Timeout = TimeSpan.FromSeconds(30)
            };
            
            // Note: .NET Framework doesn't support HTTP/2 natively
            // The OTLP exporter will handle the protocol negotiation
        }

        public HttpClient CreateClient(string name)
        {
            // Return the shared HttpClient instance
            // Note: In production, you might want to implement proper HttpClient management
            // with different clients for different purposes
            return _httpClient;
        }
    }
}