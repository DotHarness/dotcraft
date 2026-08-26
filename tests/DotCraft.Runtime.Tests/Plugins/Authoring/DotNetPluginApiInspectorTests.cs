using DotCraft.Runtime;
using Xunit;

namespace DotCraft.Tests.Runtime.Plugins.Authoring;

public sealed class DotNetPluginApiInspectorTests(
    AuthoringReferencePackFixture fixture) : IClassFixture<AuthoringReferencePackFixture>
{
    private readonly DotNetPluginApiInspector _inspector = new(fixture.Load());

    [Fact]
    public void Inspect_ReturnsExactTypeWithAssemblySignatureAndDocumentation()
    {
        var results = _inspector.Inspect("dotcraft.plugins.idotcraftplugin");

        var result = Assert.IsType<DotNetPluginApiSymbol>(results[0]);
        Assert.Equal("DotCraft.Core", result.AssemblyName);
        Assert.Equal("public interface DotCraft.Plugins.IDotCraftPlugin", result.Signature);
        Assert.Contains("Entry point implemented by every in-process DotCraft plugin", result.Summary);
    }

    [Fact]
    public void Inspect_ReturnsPublicMemberSignatureAndDocumentation()
    {
        var results = _inspector.Inspect("ActivateAsync");

        var result = Assert.Single(results, static result =>
            result.Signature.Contains("IDotCraftPlugin.ActivateAsync", StringComparison.Ordinal));
        Assert.Equal("DotCraft.Core", result.AssemblyName);
        Assert.Contains("ValueTask", result.Signature, StringComparison.Ordinal);
        Assert.Contains("IPluginActivationContext", result.Signature, StringComparison.Ordinal);
        Assert.Contains("Activates the plugin", result.Summary);
    }

    [Fact]
    public void Inspect_PreservesGenericMemberConstraints()
    {
        var result = Assert.Single(
            _inspector.Inspect("DotCraft.Contributions.IContributionRegistrar.Add"));

        Assert.Contains(
            "where TContract : class, DotCraft.Contributions.IContributionContract",
            result.Signature,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_KeepsGenericConstraintsOutOfTheQualifiedLookupIdentity()
    {
        var results = _inspector.Inspect(
            "DotCraft.Contributions.ContributionEntry<TContract>");

        Assert.StartsWith(
            "public struct DotCraft.Contributions.ContributionEntry<TContract>",
            results[0].Signature,
            StringComparison.Ordinal);
        Assert.Contains(
            "where TContract : class, DotCraft.Contributions.IContributionContract",
            results[0].Signature,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_MatchesSimpleNamesCaseInsensitivelyAndSortsSignatures()
    {
        var results = _inspector.Inspect("pluginidentity");
        var signatures = results.Select(static result => result.Signature).ToArray();

        Assert.Contains(
            signatures,
            static signature => signature.Contains("DotCraft.Plugins.PluginIdentity", StringComparison.Ordinal));
        Assert.Equal(signatures.OrderBy(static signature => signature, StringComparer.Ordinal), signatures);
    }

    [Fact]
    public void Inspect_LimitsBroadQueriesAndExcludesBclSymbols()
    {
        Assert.Equal(25, _inspector.Inspect("DotCraft").Count);
        Assert.Empty(_inspector.Inspect("System.String"));
    }

    [Fact]
    public void Inspect_DoesNotExposeInternalSymbols()
    {
        Assert.Empty(_inspector.Inspect("PluginDotnetMetadataInspector"));
    }

    [Fact]
    public void Inspect_ReturnsEmptyForUnknownSymbol()
    {
        Assert.Empty(_inspector.Inspect("ContosoDefinitelyMissingApi"));
    }

    [Fact]
    public void Inspect_RejectsBlankQuery()
    {
        Assert.Throws<ArgumentException>(() => _inspector.Inspect(" \t"));
    }
}
