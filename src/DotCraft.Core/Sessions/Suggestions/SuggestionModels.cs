namespace DotCraft.Sessions;

/// <summary>Internal input for generating a source-control commit message.</summary>
public sealed class CommitMessageSuggestionRequest
{
    public string ThreadId { get; init; } = string.Empty;

    public string[] Paths { get; init; } = [];

    public string? Provider { get; init; }

    public int? MaxDiffChars { get; init; }
}

/// <summary>Internal commit-message suggestion result.</summary>
public sealed record CommitMessageSuggestionResult(string Message);

/// <summary>Internal input for personalized welcome suggestions.</summary>
public sealed class WelcomeSuggestionRequest
{
    public SessionIdentity Identity { get; init; } = new();

    public int? MaxItems { get; init; }
}

/// <summary>One internal welcome suggestion.</summary>
public sealed class WelcomeSuggestion
{
    public string Title { get; init; } = string.Empty;

    public string Prompt { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;
}

/// <summary>Internal personalized welcome-suggestion snapshot.</summary>
public sealed class WelcomeSuggestionSnapshot
{
    public List<WelcomeSuggestion> Items { get; init; } = [];

    public string Source { get; init; } = "none";

    public DateTimeOffset GeneratedAt { get; init; }

    public string Fingerprint { get; init; } = string.Empty;
}
