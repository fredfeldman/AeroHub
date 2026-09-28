namespace AeroHub.Core;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}