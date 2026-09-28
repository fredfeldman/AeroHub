using AeroHub.Contracts;

namespace AeroHub.Core;

public interface INavaidService
{
    IReadOnlyList<NavaidSnapshot> GetNavaids(double? maxDistanceMiles = 200);
}
