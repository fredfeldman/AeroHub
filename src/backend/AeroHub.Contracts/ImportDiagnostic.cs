namespace AeroHub.Contracts;

public sealed record ImportDiagnostic(
    string Id,
    DateTimeOffset OccurredAtUtc,
    string ImportId,
    string Severity,
    string Code,
    string Message,
    string? RawReference);