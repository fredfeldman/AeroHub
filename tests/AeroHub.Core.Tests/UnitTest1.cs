namespace AeroHub.Core.Tests;

public class UnitTest1
{
    [Fact]
    public void CoreAssemblyMarker_can_be_created()
    {
        var marker = new CoreAssemblyMarker();

        Assert.NotNull(marker);
    }
}