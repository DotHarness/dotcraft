namespace DotCraft.Sessions;

public sealed record ImportedTurnInput
{
    public required string UserText { get; init; }

    public IReadOnlyList<string> AgentTexts { get; init; } = [];

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }
}

public sealed record ThreadImportRequest
{
    public required SessionIdentity Identity { get; init; }

    public required string ThreadId { get; init; }

    public required string DisplayName { get; init; }

    public string? Cwd { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    public required IReadOnlyList<ImportedTurnInput> Turns { get; init; }

    /// <summary>Approximate token count of the imported history, recorded as the initial context usage.</summary>
    public long EstimatedTokens { get; init; }
}

public sealed record ThreadImportAppendRequest
{
    public required string ThreadId { get; init; }

    /// <summary>Turns already imported, in order, as the source has them now; only the last may have gained agent texts, which are appended to it.</summary>
    public required IReadOnlyList<ImportedTurnInput> ExistingTurns { get; init; }

    public required IReadOnlyList<ImportedTurnInput> NewTurns { get; init; }

    /// <summary>Approximate token count of the whole history after the append; zero keeps the recorded usage.</summary>
    public long EstimatedTokens { get; init; }
}

public sealed record ThreadImportResult
{
    public required SessionThread Thread { get; init; }

    public bool AlreadyExisted { get; init; }
}

public static class ThreadImportConstants
{
    public const string ChannelName = "session-import";

    /// <summary>Text of the agent message that ends the last turn of the first import.</summary>
    public const string Marker = "<EXTERNAL SESSION IMPORTED>";
}

/// <summary>Refusal to extend an imported Thread that is not active, is busy, or no longer matches its source; nothing is written.</summary>
public sealed class ThreadImportRefusedException(string message)
    : InvalidOperationException($"{ErrorCode}: {message}")
{
    public const string ErrorCode = "import_append_refused";
}
