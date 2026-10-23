using DotCraft.Runtime;
using Xunit;

namespace DotCraft.Tests.Runtime.Plugins.Authoring;

public sealed class DotNetPluginApiInspectorTests(
    AuthoringReferencePackFixture fixture) : IClassFixture<AuthoringReferencePackFixture>
{
    private readonly DotNetPluginApiInspector _inspector = fixture.Inspector;

    [Fact]
    public void Inspect_ReturnsPublicTypesAndMembersWithSignaturesAndDocumentation()
    {
        var type = Assert.IsType<DotNetPluginApiSymbol>(_inspector.Inspect("dotcraft.plugins.idotcraftplugin")[0]);
        Assert.Equal("DotCraft.Core", type.AssemblyName);
        Assert.Equal("public interface DotCraft.Plugins.IDotCraftPlugin", type.Signature);
        Assert.Contains("Entry point implemented by every in-process DotCraft plugin", type.Summary);

        var member = Assert.Single(_inspector.Inspect("ActivateAsync"), static result =>
            result.Signature.Contains("IDotCraftPlugin.ActivateAsync", StringComparison.Ordinal));
        Assert.Equal("DotCraft.Core", member.AssemblyName);
        Assert.Contains("ValueTask", member.Signature, StringComparison.Ordinal);
        Assert.Contains("IPluginActivationContext", member.Signature, StringComparison.Ordinal);
        Assert.Contains("Activates the plugin", member.Summary);

        var signatures = _inspector.Inspect("pluginidentity").Select(static result => result.Signature).ToArray();
        Assert.Contains(
            signatures,
            static signature => signature.Contains("DotCraft.Plugins.PluginIdentity", StringComparison.Ordinal));
        Assert.Equal(signatures.OrderBy(static signature => signature, StringComparer.Ordinal), signatures);
    }

    [Fact]
    public void Inspect_PreservesGenericConstraintsOutsideTheLookupIdentity()
    {
        const string constraint = "where TContract : class, DotCraft.Contributions.IContributionContract";

        var member = Assert.Single(_inspector.Inspect("DotCraft.Contributions.IContributionRegistrar.Add"));
        var type = _inspector.Inspect("DotCraft.Contributions.ContributionEntry<TContract>")[0];

        Assert.Contains(constraint, member.Signature, StringComparison.Ordinal);
        Assert.StartsWith(
            "public struct DotCraft.Contributions.ContributionEntry<TContract>",
            type.Signature,
            StringComparison.Ordinal);
        Assert.Contains(constraint, type.Signature, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_LimitsBroadQueries_AndHidesBclInternalAndUnknownSymbols()
    {
        Assert.Equal(25, _inspector.Inspect("DotCraft").Count);
        Assert.Empty(_inspector.Inspect("System.String"));
        Assert.Empty(_inspector.Inspect("PluginDotnetMetadataInspector"));
        Assert.Empty(_inspector.Inspect("ContosoDefinitelyMissingApi"));
        Assert.Throws<ArgumentException>(() => _inspector.Inspect(" \t"));
    }
}
