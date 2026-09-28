using AeroHub.Contracts;
using AeroHub.Infrastructure;

namespace AeroHub.Api.Tests;

public class NavaidServiceTests
{
    [Fact]
    public void GetNavaids_returns_navaids_within_distance_limit_sorted_by_distance()
    {
        var service = new StaticNavaidService();

        var navaids = service.GetNavaids(200);

        Assert.NotEmpty(navaids);
        Assert.All(navaids, n => Assert.True(n.DistanceMilesFromCenter <= 200.0));

        // Check distance sorting
        for (var i = 0; i < navaids.Count - 1; i++)
        {
            Assert.True(navaids[i].DistanceMilesFromCenter <= navaids[i + 1].DistanceMilesFromCenter);
        }

        // Verify key navaids exist
        Assert.Contains(navaids, n => n.Identifier == "CVE" && n.Type == NavaidType.VorDme);
        Assert.Contains(navaids, n => n.Identifier == "DFW" && n.Type == NavaidType.VorDme);
        Assert.Contains(navaids, n => n.Identifier == "BYP" && n.Type == NavaidType.Vortac);
        Assert.Contains(navaids, n => n.Identifier == "I-TKI" && n.Type == NavaidType.IlsLocalizer);
    }

    [Fact]
    public void GetNavaids_filters_by_smaller_radius()
    {
        var service = new StaticNavaidService();

        var localNavaids = service.GetNavaids(30);

        Assert.NotEmpty(localNavaids);
        Assert.All(localNavaids, n => Assert.True(n.DistanceMilesFromCenter <= 30.0));
        Assert.Contains(localNavaids, n => n.Identifier == "CVE");
        Assert.DoesNotContain(localNavaids, n => n.Identifier == "SPS"); // Wichita Falls is >100 mi
    }
}
