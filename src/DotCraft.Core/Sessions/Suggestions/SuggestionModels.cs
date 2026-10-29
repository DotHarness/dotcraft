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
