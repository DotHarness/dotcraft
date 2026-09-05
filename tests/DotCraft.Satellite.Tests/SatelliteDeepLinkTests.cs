using DotCraft.Satellite.Services;
using Xunit;

namespace DotCraft.Satellite.Tests;

public sealed class SatelliteDeepLinkTests
{
    private const string Invitation = "http://ann-pc:47600/i/inv_abcdefgh";

    [Fact]
    public void TryParse_AcceptsJoinLinkAndBareInvitation()
    {
        Assert.True(SatelliteDeepLink.TryParse(
            "dotcraft://satellite/join?invite=" + Uri.EscapeDataString(Invitation),
            out var fromDeepLink));
        Assert.Equal(Invitation, fromDeepLink);

        Assert.True(SatelliteDeepLink.TryParse(Invitation, out var direct));
        Assert.Equal(Invitation, direct);

        Assert.True(SatelliteDeepLink.TryParse(
            "DOTCRAFT://Satellite/Join/?invite=" + Uri.EscapeDataString(Invitation),
            out var mixedCase));
        Assert.Equal(Invitation, mixedCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("dotcraft://workspace/open?path=C%3A%5Crepo&threadId=thread_1")]
    [InlineData("dotcraft://settings/computer-control/chrome")]
    [InlineData("dotcraft://satellite/join")]
    [InlineData("dotcraft://satellite/join?other=x")]
    [InlineData("dotcraft://satellite/leave?invite=http%3A%2F%2Fann-pc%3A47600%2Fi%2Finv_abcdefgh")]
    [InlineData("not a url")]
    [InlineData("file:///C:/windows/system32/cmd.exe")]
    public void TryParse_RefusesEverythingElse(string? link)
    {
        Assert.False(SatelliteDeepLink.TryParse(link, out var invite));
        Assert.Equal(string.Empty, invite);
    }

    [Fact]
    public void TryParse_RefusesControlCharactersAndOverlongLinks()
    {
        Assert.False(SatelliteDeepLink.TryParse(
            "dotcraft://satellite/join?invite=" + Uri.EscapeDataString("http://ann-pc:47600/i/inv_ab\u0007cdefgh"),
            out _));
        Assert.False(SatelliteDeepLink.TryParse("http://ann-pc:47600/i/" + new string('a', 4096), out _));
    }
}
