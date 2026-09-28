using AeroHub.Contracts;

namespace AeroHub.Core;

public interface IFixtureReplayService
{
    Task<FixtureReplayResult> ReplayAcarsAsync(CancellationToken cancellationToken = default);
}