using DotCraft.Oratorio.Domain;

namespace DotCraft.Oratorio.GitHub;

public enum GitHubCommentCommandParseStatus
{
    NotCommand,
    Invalid,
    Parsed
}

public sealed record GitHubCommentCommandParseResult(
    GitHubCommentCommandParseStatus Status,
    GitHubCommentCommandKind? CommandKind = null,
    string? Focus = null,
    string? Question = null,
    string? ErrorCode = null)
{
    public static GitHubCommentCommandParseResult NotCommand() =>
        new(GitHubCommentCommandParseStatus.NotCommand);

    public static GitHubCommentCommandParseResult Invalid(string errorCode) =>
        new(GitHubCommentCommandParseStatus.Invalid, ErrorCode: errorCode);

    public static GitHubCommentCommandParseResult Review(string? focus) =>
        new(GitHubCommentCommandParseStatus.Parsed, GitHubCommentCommandKind.Review, Focus: focus);

    public static GitHubCommentCommandParseResult Ask(string question) =>
        new(GitHubCommentCommandParseStatus.Parsed, GitHubCommentCommandKind.Question, Question: question);
}

public sealed class GitHubCommentCommandParser
{
    private const string Mention = "@dotcraft-ai";
    private const string ReviewCommand = "/review";
    private const string BareReviewCommand = "review";
    private const int MaxFocusCharacters = 500;
    private const int MaxQuestionCharacters = 8000;

    public GitHubCommentCommandParseResult Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return GitHubCommentCommandParseResult.NotCommand();
        }

        var command = body.Trim();
        if (!StartsWithMention(command))
        {
            return GitHubCommentCommandParseResult.NotCommand();
        }

        var remainder = command[Mention.Length..].TrimStart();
        if (remainder.Length == 0)
        {
            return GitHubCommentCommandParseResult.Invalid("githubCommentCommandTextRequired");
        }

        if (string.Equals(remainder, BareReviewCommand, StringComparison.OrdinalIgnoreCase))
        {
            return GitHubCommentCommandParseResult.Review(null);
        }

        var token = ReadToken(remainder);
        if (IsSlashCommand(token))
        {
            return ParseCommand(token, remainder[token.Length..]);
        }

        if (CountCharacters(remainder, MaxQuestionCharacters) > MaxQuestionCharacters)
        {
            return GitHubCommentCommandParseResult.Invalid("githubCommentCommandQuestionTooLong");
        }

        return GitHubCommentCommandParseResult.Ask(remainder);
    }

    private static GitHubCommentCommandParseResult ParseCommand(string token, string arguments)
    {
        if (!string.Equals(token, ReviewCommand, StringComparison.OrdinalIgnoreCase))
        {
            return GitHubCommentCommandParseResult.Invalid("githubCommentCommandUnknown");
        }

        if (arguments.IndexOfAny(['\r', '\n']) >= 0)
        {
            return GitHubCommentCommandParseResult.Invalid("githubCommentCommandMultiline");
        }

        var focus = arguments.Trim();
        if (CountCharacters(focus, MaxFocusCharacters) > MaxFocusCharacters)
        {
            return GitHubCommentCommandParseResult.Invalid("githubCommentCommandFocusTooLong");
        }

        return GitHubCommentCommandParseResult.Review(focus.Length == 0 ? null : focus);
    }

    private static bool IsSlashCommand(string token) =>
        token.Length > 1 &&
        token[0] == '/' &&
        token[1..].All(c => char.IsAsciiLetter(c) || c == '-');

    private static int CountCharacters(string value, int limit) =>
        value.EnumerateRunes().Take(limit + 1).Count();

    private static bool StartsWithMention(string value)
    {
        if (!value.StartsWith(Mention, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return value.Length == Mention.Length || char.IsWhiteSpace(value[Mention.Length]);
    }

    private static string ReadToken(string value)
    {
        var end = 0;
        while (end < value.Length && !char.IsWhiteSpace(value[end]))
        {
            end++;
        }

        return value[..end];
    }
}
