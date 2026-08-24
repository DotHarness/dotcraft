using DotCraft.Sessions;

namespace DotCraft.Contributions;

/// <summary>Identifies one turn to a lifecycle contributor; the turn identifier is unique within its thread, not globally.</summary>
public sealed record TurnLifecycleContext(string ThreadId, string TurnId)
{
    /// <summary>Gets the terminal status of the turn, or <see langword="null"/> at turn start and when the turn record could no longer be located.</summary>
    public TurnStatus? Status { get; init; }

    /// <summary>Gets the turn's error code when it failed, otherwise <see langword="null"/>.</summary>
    public string? Error { get; init; }
}

/// <summary>Observes the start and end of a turn; observation only, and contributor exceptions never reach the turn.</summary>
public interface ITurnLifecycleContributor : IContributionContract
{
    /// <summary>Runs when a turn starts, after it becomes observable to clients.</summary>
    Task OnTurnStartedAsync(TurnLifecycleContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <summary>Runs exactly once for every turn that reported a start, whatever terminal state it reached.</summary>
    Task OnTurnEndedAsync(TurnLifecycleContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
