using DotCraft.Plugins;
using Xunit;

namespace DotCraft.Tests.Plugins;

public sealed class PluginHostVersionTests
{
    [Theory]
    [InlineData("0.3.0", true)]
    [InlineData("0.5.8", true)]
    [InlineData("0.5.9", false)]
    [InlineData("1.0.0", false)]
    [InlineData("0.6.0", false)]
    [InlineData("0.4.99", true)]
    public void Satisfies_ComparesComponentwiseAgainstTheProductVersion(string minHostVersion, bool expected)
    {
        var host = new PluginHostVersion(new Version(0, 5, 8));

        Assert.Equal(expected, host.Satisfies(minHostVersion));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.5")]
    [InlineData("0.5.8-preview.1")]
    [InlineData("v0.5.8")]
    public void Satisfies_TreatsAnUnparsableMinimumAsNotGating(string? minHostVersion)
    {
        var host = new PluginHostVersion(new Version(0, 0, 1));

        Assert.True(host.Satisfies(minHostVersion));
    }

    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("0.0.0", 0, 0, 0)]
    [InlineData("10.20.30", 10, 20, 30)]
    public void TryParseCanonical_AcceptsThreePartVersions(string value, int major, int minor, int patch)
    {
        Assert.True(PluginHostVersion.TryParseCanonical(value, out var version));
        Assert.Equal(new Version(major, minor, patch), version);
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("01.2.3")]
    [InlineData("[1.0.0,2.0.0)")]
    public void TryParseCanonical_RejectsNonCanonicalVersions(string value)
    {
        Assert.False(PluginHostVersion.TryParseCanonical(value, out _));
    }

    [Fact]
    public void ProductText_RendersTheCanonicalThreePartForm()
    {
        Assert.Equal("2.7.13", new PluginHostVersion(new Version(2, 7, 13)).ProductText);
    }

    [Fact]
    public void Current_ResolvesAConcreteHostIdentity()
    {
        var current = PluginHostVersion.Current;

        Assert.Same(current, PluginHostVersion.Current);
        Assert.NotNull(current.Product);
    }
}
