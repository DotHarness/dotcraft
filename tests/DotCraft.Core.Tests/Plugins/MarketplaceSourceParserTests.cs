using DotCraft.Plugins.Marketplaces;

namespace DotCraft.Core.Tests.Plugins;

public sealed class MarketplaceSourceParserTests
{
    [Fact]
    public void Parse_ExpandsShorthandToRepositoryUrl()
    {
        var source = MarketplaceSourceParser.Parse("owner/repo");

        Assert.Equal(MarketplaceSourceKind.Git, source.Kind);
        Assert.Equal("https://github.com/owner/repo.git", source.Value);
        Assert.Null(source.Ref);
    }

    [Fact]
    public void Parse_ReadsRefSuffixFromShorthand()
    {
        var source = MarketplaceSourceParser.Parse("owner/repo@release");

        Assert.Equal("https://github.com/owner/repo.git", source.Value);
        Assert.Equal("release", source.Ref);
    }

    [Fact]
    public void Parse_ReadsFragmentRefFromRepositoryUrl()
    {
        var source = MarketplaceSourceParser.Parse("https://example.com/team/repo.git#v1");

        Assert.Equal("https://example.com/team/repo.git", source.Value);
        Assert.Equal("v1", source.Ref);
    }

    [Fact]
    public void Parse_ExplicitRefOverridesEmbeddedRef()
    {
        var source = MarketplaceSourceParser.Parse("owner/repo@main", "release");

        Assert.Equal("release", source.Ref);
    }

    [Fact]
    public void Parse_ShorthandAndRepositoryUrlNormalizeToSameSource()
    {
        var shorthand = MarketplaceSourceParser.Parse("owner/repo");
        var url = MarketplaceSourceParser.Parse("https://github.com/owner/repo/");

        Assert.True(shorthand.Matches(url));
    }

    [Fact]
    public void Parse_KeepsScpStyleAddressIntact()
    {
        var source = MarketplaceSourceParser.Parse("git@example.com:team/repo.git");

        Assert.Equal(MarketplaceSourceKind.Git, source.Kind);
        Assert.Equal("git@example.com:team/repo.git", source.Value);
        Assert.Null(source.Ref);
    }

    [Fact]
    public void Parse_AllowsAccountNameOnSshUrl()
    {
        var source = MarketplaceSourceParser.Parse("ssh://git@example.com/team/repo.git#main");

        Assert.Equal("ssh://git@example.com/team/repo.git", source.Value);
        Assert.Equal("main", source.Ref);
    }

    [Theory]
    [InlineData("https://user:secret@example.com/team/repo.git")]
    [InlineData("https://token@example.com/team/repo.git")]
    public void Parse_RejectsEmbeddedCredentials(string source)
    {
        var error = Assert.Throws<MarketplaceException>(() => MarketplaceSourceParser.Parse(source));

        Assert.Equal(MarketplaceErrorCodes.SourceInvalid, error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("file:///tmp/marketplace.git")]
    [InlineData("ext::sh -c echo")]
    [InlineData("owner/repo/extra")]
    public void Parse_RejectsUnsupportedSources(string source)
    {
        var error = Assert.Throws<MarketplaceException>(() => MarketplaceSourceParser.Parse(source));

        Assert.Equal(MarketplaceErrorCodes.SourceInvalid, error.Code);
    }

    [Fact]
    public void Parse_RejectsRefOnLocalSource()
    {
        var directory = NewTempDir();

        var error = Assert.Throws<MarketplaceException>(() => MarketplaceSourceParser.Parse(directory, "main"));

        Assert.Equal(MarketplaceErrorCodes.SourceInvalid, error.Code);
    }

    [Fact]
    public void Parse_RejectsSparsePathsOnLocalSource()
    {
        var directory = NewTempDir();

        var error = Assert.Throws<MarketplaceException>(
            () => MarketplaceSourceParser.Parse(directory, explicitRef: null, ["plugins/example"]));

        Assert.Equal(MarketplaceErrorCodes.SourceInvalid, error.Code);
    }

    [Fact]
    public void Parse_RejectsLocalFileSource()
    {
        var directory = NewTempDir();
        var file = Path.Combine(directory, "marketplace.json");
        File.WriteAllText(file, "{}");

        var error = Assert.Throws<MarketplaceException>(() => MarketplaceSourceParser.Parse(file));

        Assert.Equal(MarketplaceErrorCodes.SourceInvalid, error.Code);
    }

    [Fact]
    public void Parse_ResolvesExistingLocalDirectory()
    {
        var directory = NewTempDir();

        var source = MarketplaceSourceParser.Parse(directory);

        Assert.Equal(MarketplaceSourceKind.Local, source.Kind);
        Assert.Equal(Path.GetFullPath(directory), source.Value);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("plugins/../../escape")]
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
    public void FromConfigured_TreatsMissingKindAsArchiveUrl()
    {
        var source = MarketplaceSourceParser.FromConfigured(
            sourceType: null,
            "https://example.com/plugins.zip",
            refName: null,
            sparsePaths: null);

        Assert.Equal(MarketplaceSourceKind.Archive, source.Kind);
    }

    [Fact]
    public void FromConfigured_TreatsMissingKindWithExistingDirectoryAsLocal()
    {
        var directory = NewTempDir();

        var source = MarketplaceSourceParser.FromConfigured(
            sourceType: null,
            directory,
            refName: null,
            sparsePaths: null);

        Assert.Equal(MarketplaceSourceKind.Local, source.Kind);
    }

    [Fact]
    public void FromConfigured_KeepsRepositorySourceWithoutRequiringLocalDirectory()
    {
        var source = MarketplaceSourceParser.FromConfigured(
            "git",
            "https://example.com/team/repo.git",
            "main",
            ["plugins/example"]);

        Assert.Equal(MarketplaceSourceKind.Git, source.Kind);
        Assert.Equal("main", source.Ref);
        Assert.Equal(["plugins/example"], source.SparsePathList);
    }

    private static string NewTempDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-marketplace-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
