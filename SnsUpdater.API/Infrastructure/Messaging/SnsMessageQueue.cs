using System;
using System.Configuration;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SnsUpdater.API.Infrastructure.Telemetry;

namespace SnsUpdater.API.Infrastructure.Messaging
{
    public class SnsMessageQueue : ISnsMessageQueue
    {
        private readonly Channel<SnsMessage> _channel;
        
        public SnsMessageQueue()
        {
            var capacity = int.Parse(ConfigurationManager.AppSettings["BackgroundService:ChannelCapacity"] ?? "1000");
            
            var options = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            };
            
            _channel = Channel.CreateBounded<SnsMessage>(options);
        }

        public async Task EnqueueAsync(SnsMessage message, CancellationToken cancellationToken = default)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            await _channel.Writer.WriteAsync(message, cancellationToken);
            
            // Update queue size metric
            TelemetryConfiguration.QueueSize.Add(1);
        }

        public async Task<SnsMessage> DequeueAsync(CancellationToken cancellationToken = default)
        {
            return await _channel.Reader.ReadAsync(cancellationToken);
        }

        public bool TryDequeue(out SnsMessage message)
        {
            return _channel.Reader.TryRead(out message);
        }

        public int Count => _channel.Reader.Count;
    }
}