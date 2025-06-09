using System.Threading;
using System.Threading.Tasks;

namespace SnsUpdater.API.Infrastructure.Messaging
{
    public interface ISnsMessageQueue
    {
        Task EnqueueAsync(SnsMessage message, CancellationToken cancellationToken = default);
        Task<SnsMessage> DequeueAsync(CancellationToken cancellationToken = default);
        bool TryDequeue(out SnsMessage message);
        int Count { get; }
    }
}