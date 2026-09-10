using DotCraft.Satellite.Web;
using Xunit;

namespace DotCraft.Satellite.Tests;

public sealed class SatellitePagesTests
{
    [Theory]
    [InlineData("DotCraft.Satellite.island.html")]
    [InlineData("DotCraft.Satellite.consent.html")]
    public void EveryPage_ShipsWholeInTheAssemblyAndReachesNothingOutside(string logicalName)
    {
        var page = SatellitePageHost.Page(logicalName);

        string[] outside = ["<a ", "href=", "src=\"http", "<link", "<iframe", "<script src", "fetch("];
        foreach (var value in outside)
            Assert.DoesNotContain(value, page, StringComparison.OrdinalIgnoreCase);
    }
}
