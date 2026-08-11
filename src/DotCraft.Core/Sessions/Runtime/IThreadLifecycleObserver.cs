namespace DotCraft.Sessions;

/// <summary>Observes destructive thread lifecycle transitions before persisted state is removed.</summary>
public interface IThreadLifecycleObserver
{
    /// <summary>Runs before a thread and its associated persisted state are deleted.</summary>
    Task OnThreadDeletingAsync(SessionThread thread, CancellationToken cancellationToken = default);
}
