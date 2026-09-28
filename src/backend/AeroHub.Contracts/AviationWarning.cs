namespace AeroHub.Contracts;

public sealed record AviationWarning(
    string Code,
    string Message,
    string Severity);