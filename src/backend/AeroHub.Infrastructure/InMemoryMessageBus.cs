using AeroHub.Contracts;
using AeroHub.Core;

namespace AeroHub.Infrastructure;

public sealed class InMemoryMessageBus(IRecordStore? recordStore = null) : IMessageBus
{
    private const int MaximumStoredMessages = 500;
    private readonly object _gate = new();
    private readonly List<NormalizedAviationMessage> _messages = [];

    public event EventHandler<NormalizedAviationMessage>? MessageReceived;

    public ValueTask PublishAsync(NormalizedAviationMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _messages.Add(message);
            recordStore?.SaveMessage(message);

            if (_messages.Count > MaximumStoredMessages)
            {
                _messages.RemoveRange(0, _messages.Count - MaximumStoredMessages);
            }
        }

        MessageReceived?.Invoke(this, message);
        return ValueTask.CompletedTask;
    }

    public IReadOnlyList<NormalizedAviationMessage> GetRecent(int limit)
    {
        var boundedLimit = Math.Clamp(limit, 1, MaximumStoredMessages);

        lock (_gate)
        {
            var sourceMessages = _messages.Count == 0 && recordStore is not null
                ? recordStore.GetRecentMessages(boundedLimit)
                : _messages;

            return sourceMessages
                .OrderByDescending(message => message.Sequence)
                .Take(boundedLimit)
                .ToArray();
        }
    }
}