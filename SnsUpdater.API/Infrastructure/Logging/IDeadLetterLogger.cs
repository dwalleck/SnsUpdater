using System;
using SnsUpdater.API.Infrastructure.Messaging;

namespace SnsUpdater.API.Infrastructure.Logging
{
    public interface IDeadLetterLogger
    {
        void LogFailedMessage(SnsMessage message, Exception exception);
    }
}