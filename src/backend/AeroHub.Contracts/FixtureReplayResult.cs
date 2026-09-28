namespace AeroHub.Contracts;

public sealed record FixtureReplayResult(
    string FixtureName,
    int PublishedMessages,
    DateTimeOffset ReplayedAtUtc);