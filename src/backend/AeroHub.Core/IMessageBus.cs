using AeroHub.Contracts;

namespace AeroHub.Core;

public interface IMessageBus
{
    event EventHandler<NormalizedAviationMessage>? MessageReceived;

    ValueTask PublishAsync(NormalizedAviationMessage message, CancellationToken cancellationToken = default);

    IReadOnlyList<NormalizedAviationMessage> GetRecent(int limit);
}