namespace AeroHub.Contracts;

public sealed record ExternalDecoderSimulationResult(
    string ProcessId,
    string Scenario,
    SourceState State,
    int RestartCount,
    string? LastError);