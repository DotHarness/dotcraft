using DotCraft.Satellite.Web;
using Xunit;

namespace DotCraft.Satellite.Tests;

public sealed class SatellitePagesTests
{
    [Theory]
    [InlineData("DotCraft.Satellite.island.html")]
    [InlineData("DotCraft.Satellite.glow.html")]
    public void BuiltPage_ShipsWholeInTheAssemblyAndForbidsEveryRequest(string logicalName)
    {
        var page = SatellitePageHost.Page(logicalName);

        Assert.Contains("default-src 'none'", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<script src", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link ", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConsentPage_ShipsWholeInTheAssemblyAndReachesNothingOutside()
    {
        var page = SatellitePageHost.Page("DotCraft.Satellite.consent.html");

        string[] outside = ["<a ", "href=", "src=\"http", "<link", "<iframe", "<script src", "fetch("];
        foreach (var value in outside)
            Assert.DoesNotContain(value, page, StringComparison.OrdinalIgnoreCase);
    }
}
