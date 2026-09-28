using AeroHub.Contracts;
using AeroHub.Infrastructure;

namespace AeroHub.Api.Tests;

public class InMemoryMessageBusTests
{
    [Fact]
    public async Task PublishAsync_bounds_recent_message_history_during_sustained_replay()
    {
        var bus = new InMemoryMessageBus();

        for (var index = 1; index <= 600; index++)
        {
            await bus.PublishAsync(CreateMessage($"message-{index}", index));
        }

        var recentMessages = bus.GetRecent(500);

        Assert.Equal(500, recentMessages.Count);
        Assert.Equal("message-600", recentMessages[0].Id);
        Assert.Equal("message-101", recentMessages[^1].Id);
    }

    private static NormalizedAviationMessage CreateMessage(string id, long sequence)
    {
        var receivedAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(sequence);

        return new NormalizedAviationMessage(
            id,
            sequence,
            receivedAtUtc,
            AviationMessageKind.Acars,
            "ACARS replay sample",
            new AcarsMessageDetails("2L", "POS", null, "Replay sample", "POS N123AB", "POS N123AB"),
            "N123AB",
            "Synthetic replay",
            136.8,
            ParserConfidence.Candidate,
            new IngestionProvenance(
                IngestionPath.FixtureReplay,
                "synthetic-replay",
                "Synthetic reliability replay",
                null,
                "fixture/acars-v1",
                "InMemoryMessageBusTests",
                "0.1.0",
                receivedAtUtc,
                receivedAtUtc,
                $"fixtures/replay/{sequence}.txt"),
            "POS N123AB",
            []);
    }
}