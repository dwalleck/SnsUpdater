using System.Threading;
using System.Threading.Tasks;
using SnsUpdater.API.Infrastructure.Messaging;

namespace SnsUpdater.API.Infrastructure.Aws
{
    public interface ISnsClient
    {
        Task<string> PublishAsync(SnsMessage message, CancellationToken cancellationToken = default);
        bool IsCircuitOpen { get; }
        void ResetCircuit();
    }
}