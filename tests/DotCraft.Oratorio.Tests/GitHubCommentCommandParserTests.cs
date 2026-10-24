using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;

namespace DotCraft.Oratorio.Tests;

public sealed class GitHubCommentCommandParserTests
{
    private readonly GitHubCommentCommandParser _parser = new();

    [Theory]
    [InlineData("@dotcraft-ai review")]
    [InlineData("  @DOTCRAFT-AI\tREVIEW  ")]
    [InlineData("@dotcraft-ai review\n\n")]
    [InlineData("@dotcraft-ai /review")]
    [InlineData("@dotcraft-ai /REVIEW  ")]
    public void Parse_RecognizesReviewCommand(string body)
    {
        var result = _parser.Parse(body);

        Assert.Equal(GitHubCommentCommandParseStatus.Parsed, result.Status);
        Assert.Equal(GitHubCommentCommandKind.Review, result.CommandKind);
        Assert.Null(result.Focus);
    }

    [Theory]
    [InlineData("@dotcraft-ai /review security regressions", "security regressions")]
    [InlineData("@DOTCRAFT-AI /Review  Preserve This Case  ", "Preserve This Case")]
    public void Parse_RecognizesReviewFocus(string body, string expectedFocus)
    {
        var result = _parser.Parse(body);

        Assert.Equal(GitHubCommentCommandParseStatus.Parsed, result.Status);
        Assert.Equal(GitHubCommentCommandKind.Review, result.CommandKind);
        Assert.Equal(expectedFocus, result.Focus);
    }

    [Theory]
    [InlineData("@dotcraft-ai implement this", "implement this")]
    [InlineData("@dotcraft-ai review security", "review security")]
    [InlineData("@dotcraft-ai review for me why this fails", "review for me why this fails")]
    [InlineData("@dotcraft-ai review\nthanks", "review\nthanks")]
    [InlineData("@dotcraft-ai /etc/hosts looks wrong here", "/etc/hosts looks wrong here")]
    [InlineData("@dotcraft-ai  Why is this\n  safe? ", "Why is this\n  safe?")]
    public void Parse_TreatsOtherTextAsQuestion(string body, string expectedQuestion)
    {
        var result = _parser.Parse(body);

        Assert.Equal(GitHubCommentCommandParseStatus.Parsed, result.Status);
        Assert.Equal(GitHubCommentCommandKind.Question, result.CommandKind);
        Assert.Equal(expectedQuestion, result.Question);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Please run @dotcraft-ai /review")]
    [InlineData("@dotcraft-ai-helper /review")]
    [InlineData("@oratorio /review")]
    public void Parse_ReturnsNotCommandForUnaddressedText(string? body)
    {
        var result = _parser.Parse(body);

        Assert.Equal(GitHubCommentCommandParseStatus.NotCommand, result.Status);
    }

    [Theory]
    [InlineData("@dotcraft-ai", "githubCommentCommandTextRequired")]
    [InlineData("  @dotcraft-ai \n ", "githubCommentCommandTextRequired")]
    [InlineData("@dotcraft-ai /fix the build", "githubCommentCommandUnknown")]
    [InlineData("@dotcraft-ai /review auth\nand also tests", "githubCommentCommandMultiline")]
    public void Parse_RejectsInvalidCommand(string body, string expectedError)
    {
        var result = _parser.Parse(body);

        Assert.Equal(GitHubCommentCommandParseStatus.Invalid, result.Status);
        Assert.Equal(expectedError, result.ErrorCode);
    }

    [Fact]
    public void Parse_CountsFocusLimitByUnicodeScalar()
    {
        var accepted = _parser.Parse($"@dotcraft-ai /review {new string('a', 499)}😀");
        var rejected = _parser.Parse($"@dotcraft-ai /review {new string('a', 500)}😀");

        Assert.Equal(GitHubCommentCommandParseStatus.Parsed, accepted.Status);
        Assert.Equal(GitHubCommentCommandParseStatus.Invalid, rejected.Status);
        Assert.Equal("githubCommentCommandFocusTooLong", rejected.ErrorCode);
    }

    [Fact]
    public void Parse_LimitsQuestionLength()
    {
        var accepted = _parser.Parse($"@dotcraft-ai {new string('q', 8000)}");
        var rejected = _parser.Parse($"@dotcraft-ai {new string('q', 8001)}");

        Assert.Equal(GitHubCommentCommandKind.Question, accepted.CommandKind);
        Assert.Equal(GitHubCommentCommandParseStatus.Invalid, rejected.Status);
        Assert.Equal("githubCommentCommandQuestionTooLong", rejected.ErrorCode);
    }
}
