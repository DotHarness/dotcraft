using DotCraft.Sessions;

namespace DotCraft.Contributions;

/// <summary>Observes thread lifecycle transitions; observation is one-way and contributor exceptions never block the transition.</summary>
public interface IThreadLifecycleContributor : IContributionContract
{
    /// <summary>Runs after a new thread has been created and published, forks included.</summary>
    Task OnThreadStartedAsync(SessionThread thread, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <summary>Runs after the explicit resume transition only, not when a persisted thread is loaded on demand.</summary>
    Task OnThreadResumedAsync(SessionThread thread, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <summary>Runs before a thread and its associated persisted state are deleted, and before its thread-scoped contributions are released.</summary>
    Task OnThreadDeletingAsync(SessionThread thread, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
