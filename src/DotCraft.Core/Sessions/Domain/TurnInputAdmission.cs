using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

/// <summary>The accepted Turn and the execution stream owned only by a new admission.</summary>
public sealed record TurnInputAdmission(
    SessionTurn Turn,
    bool Started,
    IAsyncEnumerable<SessionEvent>? Events = null);

public sealed record TurnSettingsOverrides
{
    public string? ProviderId { get; init; }
    public string? Model { get; init; }
    public TurnReasoningOverrides? Reasoning { get; init; }
    public InferenceSpeed? Speed { get; init; }
    public string? Mode { get; init; }
    public string? Cwd { get; init; }
    public IReadOnlyList<string>? RuntimeWorkspaceRoots { get; init; }
}

public sealed record TurnReasoningOverrides
{
    public bool? Enabled { get; init; }
    public ModelReasoningEffort? Effort { get; init; }
    public ReasoningOutput? Output { get; init; }
}
