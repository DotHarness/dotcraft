using DotCraft.Plugins.Marketplaces;
using Xunit;

namespace DotCraft.Core.Tests.Plugins;

public sealed class MarketplaceSourceParserTests
{
    [Theory]
    [InlineData("owner/repo@main", "release", "https://github.com/owner/repo.git", "release")]
    [InlineData("https://example.com/team/repo.git#v1", null, "https://example.com/team/repo.git", "v1")]
    [InlineData("git@example.com:team/repo.git", null, "git@example.com:team/repo.git", null)]
    public void Parse_NormalizesGitSourcesAndRefs(string input, string? explicitRef, string expectedValue, string? expectedRef)
    {
        var source = MarketplaceSourceParser.Parse(input, explicitRef);

        Assert.Equal(MarketplaceSourceKind.Git, source.Kind);
        Assert.Equal(expectedValue, source.Value);
        Assert.Equal(expectedRef, source.Ref);
    }

    [Fact]
    public void Parse_ShorthandAndRepositoryUrlNormalizeToSameSource()
    {
        var shorthand = MarketplaceSourceParser.Parse("owner/repo");
        var url = MarketplaceSourceParser.Parse("https://github.com/owner/repo/");

        Assert.True(shorthand.Matches(url));
    }

    [Theory]
    [InlineData("https://user:secret@example.com/team/repo.git")]
    [InlineData("file:///tmp/marketplace.git")]
    [InlineData("ext::sh -c echo")]
    public void Parse_RejectsCredentialsAndUnsupportedSources(string source)
    {
        var error = Assert.Throws<MarketplaceException>(() => MarketplaceSourceParser.Parse(source));

        Assert.Equal(MarketplaceErrorCodes.SourceInvalid, error.Code);
    }

    [Fact]
    public void Parse_ResolvesLocalDirectoriesButRejectsRefsSparsePathsAndFiles()
    {
        var directory = NewTempDir();
        var file = Path.Combine(directory, "marketplace.json");
        File.WriteAllText(file, "{}");

        var source = MarketplaceSourceParser.Parse(directory);

        Assert.Equal(MarketplaceSourceKind.Local, source.Kind);
        Assert.Equal(Path.GetFullPath(directory), source.Value);
        Assert.Equal(
            MarketplaceErrorCodes.SourceInvalid,
            Assert.Throws<MarketplaceException>(() => MarketplaceSourceParser.Parse(directory, "main")).Code);
        Assert.Equal(
            MarketplaceErrorCodes.SourceInvalid,
            Assert.Throws<MarketplaceException>(() => MarketplaceSourceParser.Parse(directory, explicitRef: null, ["plugins/example"])).Code);
        Assert.Equal(
            MarketplaceErrorCodes.SourceInvalid,
            Assert.Throws<MarketplaceException>(() => MarketplaceSourceParser.Parse(file)).Code);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    public void Parse_RejectsTraversingSparsePaths(string sparsePath)
    {
        var error = Assert.Throws<MarketplaceException>(
            () => MarketplaceSourceParser.Parse("owner/repo", explicitRef: null, [sparsePath]));

        Assert.Equal(MarketplaceErrorCodes.SourceInvalid, error.Code);
    }

    [Fact]
    public void Parse_NormalizesSparsePathSeparatorsAndDropsDuplicates()
    {
        var source = MarketplaceSourceParser.Parse(
            "owner/repo",
            explicitRef: null,
            [@"plugins\example", "plugins/example", "  ", "plugins/other"]);

        Assert.Equal(["plugins/example", "plugins/other"], source.SparsePathList);
    }

    [Fact]
    public void FromConfigured_InfersKindFromTheValueWhenMissing()
    {
        var directory = NewTempDir();

        var archive = MarketplaceSourceParser.FromConfigured(sourceType: null, "https://example.com/plugins.zip", refName: null, sparsePaths: null);
        var local = MarketplaceSourceParser.FromConfigured(sourceType: null, directory, refName: null, sparsePaths: null);
        var git = MarketplaceSourceParser.FromConfigured("git", "https://example.com/team/repo.git", "main", ["plugins/example"]);

        Assert.Equal(MarketplaceSourceKind.Archive, archive.Kind);
        Assert.Equal(MarketplaceSourceKind.Local, local.Kind);
        Assert.Equal(MarketplaceSourceKind.Git, git.Kind);
        Assert.Equal("main", git.Ref);
        Assert.Equal(["plugins/example"], git.SparsePathList);
    }

    private static string NewTempDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-marketplace-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
