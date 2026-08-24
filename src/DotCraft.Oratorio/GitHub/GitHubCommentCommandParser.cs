using DotCraft.Oratorio.Domain;

namespace DotCraft.Oratorio.GitHub;

public enum GitHubCommentCommandParseStatus
{
    NotCommand,
    Invalid,
    Unsupported,
    Parsed
}

public sealed record GitHubCommentCommandParseResult(
    GitHubCommentCommandParseStatus Status,
    GitHubCommentCommandKind? CommandKind = null,
    string? Focus = null,
    string? UnsupportedVerb = null,
    string? ErrorCode = null)
{
    public static GitHubCommentCommandParseResult NotCommand() =>
        new(GitHubCommentCommandParseStatus.NotCommand);

    public static GitHubCommentCommandParseResult Invalid(string errorCode) =>
        new(GitHubCommentCommandParseStatus.Invalid, ErrorCode: errorCode);

    public static GitHubCommentCommandParseResult Unsupported(string verb) =>
        new(GitHubCommentCommandParseStatus.Unsupported, UnsupportedVerb: verb);

    public static GitHubCommentCommandParseResult Review(string? focus) =>
        new(GitHubCommentCommandParseStatus.Parsed, GitHubCommentCommandKind.Review, focus);
}

public sealed class GitHubCommentCommandParser
{
    private const string Mention = "@dotcraft-ai";
    private const string ReviewVerb = "review";
    private const string FocusKeyword = "for";
    private const int MaxFocusCharacters = 500;

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

        if (command.Contains('\r') || command.Contains('\n'))
        {
            return GitHubCommentCommandParseResult.Invalid("githubCommentCommandMultiline");
        }

        var remainder = command[Mention.Length..].TrimStart();
        if (remainder.Length == 0)
        {
            return GitHubCommentCommandParseResult.Invalid("githubCommentCommandVerbRequired");
        }

        var (verb, arguments) = ReadToken(remainder);
        if (!string.Equals(verb, ReviewVerb, StringComparison.OrdinalIgnoreCase))
        {
            return GitHubCommentCommandParseResult.Unsupported(verb);
        }

        if (arguments.Length == 0)
        {
            return GitHubCommentCommandParseResult.Review(null);
        }

        var (keyword, focus) = ReadToken(arguments);
        if (!string.Equals(keyword, FocusKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return GitHubCommentCommandParseResult.Invalid("githubCommentCommandArgumentsInvalid");
        }

        if (focus.Length == 0)
        {
            return GitHubCommentCommandParseResult.Invalid("githubCommentCommandFocusRequired");
        }

        if (focus.EnumerateRunes().Take(MaxFocusCharacters + 1).Count() > MaxFocusCharacters)
        {
            return GitHubCommentCommandParseResult.Invalid("githubCommentCommandFocusTooLong");
        }

        return GitHubCommentCommandParseResult.Review(focus);
    }

    private static bool StartsWithMention(string value)
    {
        if (!value.StartsWith(Mention, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return value.Length == Mention.Length || char.IsWhiteSpace(value[Mention.Length]);
    }

    private static (string Token, string Remainder) ReadToken(string value)
    {
        var separator = 0;
        while (separator < value.Length && !char.IsWhiteSpace(value[separator]))
        {
            separator++;
        }

        return separator == value.Length
            ? (value, "")
            : (value[..separator], value[separator..].Trim());
    }
}
