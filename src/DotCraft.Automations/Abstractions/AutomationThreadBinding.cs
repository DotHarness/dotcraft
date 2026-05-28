namespace DotCraft.Automations.Abstractions;

/// <summary>
/// Binding from an automation task to a pre-existing thread.
/// When set, the orchestrator submits workflow turns into that thread instead of
/// creating/resuming the synthesized <c>task-{source}-{id}</c> thread.
/// </summary>
public sealed class AutomationThreadBinding
{
    /// <summary>Identifier of the user-selected thread to submit turns into.</summary>
    public required string ThreadId { get; init; }

    /// <summary>
    /// Binding mode.
    /// <list type="bullet">
    /// <item><c>run-in-thread</c> (default): submit turns directly into <see cref="ThreadId"/>,
    /// reusing its existing <see cref="DotCraft.Protocol.ThreadConfiguration"/>.</item>
    /// <item>Reserved for future: <c>poll-separate</c> — run in an isolated automation thread but read
    /// the target thread's recent turns as context.</item>
    /// </list>
    /// </summary>
    public string Mode { get; init; } = "run-in-thread";
}
