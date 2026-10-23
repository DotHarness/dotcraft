using DotCraft.Plugins;
using Xunit;

namespace DotCraft.Tests.Plugins;

public sealed class PluginHostVersionTests
{
    [Theory]
    [InlineData("0.4.99", true)]
    [InlineData("0.5.9", false)]
    [InlineData(null, true)]
    [InlineData("0.5.8-preview.1", true)]
    public void Satisfies_ComparesComponentwiseAndTreatsUnparsableMinimumsAsNotGating(string? minHostVersion, bool expected)
    {
        var host = new PluginHostVersion(new Version(0, 5, 8));

        Assert.Equal(expected, host.Satisfies(minHostVersion));
    }

    [Theory]
    [InlineData("10.20.30", true)]
    [InlineData("01.2.3", false)]
    [InlineData("[1.0.0,2.0.0)", false)]
    public void TryParseCanonical_AcceptsOnlyThreePartVersions(string value, bool expected)
    {
        Assert.Equal(expected, PluginHostVersion.TryParseCanonical(value, out var version));
        if (expected)
            Assert.Equal(new Version(10, 20, 30), version);
    }

    [Fact]
    public void Current_IsTheVersionOfThePluginApiAssemblyRatherThanTheEntryAssembly()
    {
        var api = typeof(PluginHostVersion).Assembly;
        var entry = System.Reflection.Assembly.GetEntryAssembly()!;

        Assert.Equal(PluginHostVersion.PluginApiAssemblyName, api.GetName().Name);
        Assert.Equal(PluginHostVersion.For(api).Product, PluginHostVersion.Current.Product);
        Assert.NotEqual(PluginHostVersion.For(entry).Product, PluginHostVersion.Current.Product);
        Assert.NotEqual(new Version(0, 0, 0), PluginHostVersion.Current.Product);
    }
}
