using AeroHub.Contracts;
using AeroHub.Core;
using AeroHub.Decoders.Acars;

namespace AeroHub.Infrastructure;

public sealed class FixtureReplayService(IMessageBus messageBus, IClock clock, IAcarsMessageParser acarsMessageParser) : IFixtureReplayService
{
    private long _sequence;

    public async Task<FixtureReplayResult> ReplayAcarsAsync(CancellationToken cancellationToken = default)
    {
        var replayedAtUtc = clock.UtcNow;
        var messages = CreateAcarsFixture(replayedAtUtc);

        foreach (var message in messages)
        {
            await messageBus.PublishAsync(message, cancellationToken);
        }

        return new FixtureReplayResult("local-acars-vhf-sample", messages.Length, replayedAtUtc);
    }

    private NormalizedAviationMessage[] CreateAcarsFixture(DateTimeOffset replayedAtUtc)
    {
        return
        [
            CreateMessage(
                replayedAtUtc,
                "2L POS N123AB OUT 1412Z OFF 1427Z",
                136.800,
                1),
            CreateMessage(
                replayedAtUtc.AddMilliseconds(150),
                "H1 #DFB REQUEST WX N456CD",
                136.975,
                2),
            CreateMessage(
                replayedAtUtc.AddMilliseconds(300),
                "ZZ TEST N789EF UNKNOWN PAYLOAD",
                136.650,
                3)
        ];
    }

    private NormalizedAviationMessage CreateMessage(
        DateTimeOffset receivedAtUtc,
        string rawPayload,
        double frequencyMHz,
        int fixtureNumber)
    {
        var sequence = Interlocked.Increment(ref _sequence);

        return acarsMessageParser.Parse(new AcarsParserInput(
            rawPayload,
            sequence,
            receivedAtUtc,
            new IngestionProvenance(
                IngestionPath.FixtureReplay,
                "fixture-local-acars",
                "Local ACARS fixture replay",
                null,
                "fixture/acars-v1",
                "FixtureReplayService",
                "0.1.0",
                receivedAtUtc,
                receivedAtUtc,
                $"fixtures/acars/local-vhf/{fixtureNumber}.txt"),
            frequencyMHz,
            "VHF ACARS"));
    }
}