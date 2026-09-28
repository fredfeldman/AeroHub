namespace AeroHub.Contracts;

public sealed record ExternalDecoderDiagnostic(
    string Id,
    DateTimeOffset OccurredAtUtc,
    string ProcessId,
    string Severity,
    string Code,
    string Message);