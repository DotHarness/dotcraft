using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;

namespace DotCraft.Oratorio.Tests;

public sealed class GitHubCommentCommandParserTests
{
    private readonly GitHubCommentCommandParser _parser = new();

    [Theory]
    [InlineData("@dotcraft-ai review")]
    [InlineData("  @DOTCRAFT-AI\tREVIEW  ")]
    public void Parse_RecognizesReviewCommand(string body)
    {
        var result = _parser.Parse(body);

        Assert.Equal(GitHubCommentCommandParseStatus.Parsed, result.Status);
        Assert.Equal(GitHubCommentCommandKind.Review, result.CommandKind);
        Assert.Null(result.Focus);
    }

    [Theory]
    [InlineData("@dotcraft-ai review for security regressions", "security regressions")]
    [InlineData("@DOTCRAFT-AI REVIEW FOR  Preserve This Case  ", "Preserve This Case")]
    public void Parse_RecognizesReviewFocus(string body, string expectedFocus)
    {
        var result = _parser.Parse(body);

        Assert.Equal(GitHubCommentCommandParseStatus.Parsed, result.Status);
        Assert.Equal(GitHubCommentCommandKind.Review, result.CommandKind);
        Assert.Equal(expectedFocus, result.Focus);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Please run @dotcraft-ai review")]
    [InlineData("@dotcraft-ai-helper review")]
    [InlineData("@oratorio review")]
    [InlineData("@oratorio-integration review")]
    [InlineData("@someone-else review")]
    public void Parse_ReturnsNotCommandForUnaddressedText(string? body)
    {
        var result = _parser.Parse(body);

        Assert.Equal(GitHubCommentCommandParseStatus.NotCommand, result.Status);
    }

    [Theory]
    [InlineData("@dotcraft-ai")]
    [InlineData("@dotcraft-ai review for")]
    [InlineData("@dotcraft-ai review security")]
    [InlineData("@dotcraft-ai review\nfor security")]
    public void Parse_RejectsInvalidReviewGrammar(string body)
    {
        var result = _parser.Parse(body);

        Assert.Equal(GitHubCommentCommandParseStatus.Invalid, result.Status);
        Assert.NotNull(result.ErrorCode);
    }

    [Fact]
    public void Parse_ReturnsUnsupportedForOtherVerb()
    {
        var result = _parser.Parse("@dotcraft-ai implement this");

        Assert.Equal(GitHubCommentCommandParseStatus.Unsupported, result.Status);
        Assert.Equal("implement", result.UnsupportedVerb);
    }

    [Fact]
    public void Parse_CountsFocusLimitByUnicodeScalar()
    {
        var accepted = _parser.Parse($"@dotcraft-ai review for {new string('a', 499)}😀");
        var rejected = _parser.Parse($"@dotcraft-ai review for {new string('a', 500)}😀");

        Assert.Equal(GitHubCommentCommandParseStatus.Parsed, accepted.Status);
        Assert.Equal(GitHubCommentCommandParseStatus.Invalid, rejected.Status);
        Assert.Equal("githubCommentCommandFocusTooLong", rejected.ErrorCode);
    }
}
