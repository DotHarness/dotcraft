using DotCraft.Contributions;
using DotCraft.Sessions;

namespace Acme.ReviewCore;

/// <summary>Observes thread starts, resumes, and deletions; observation is one-way and cannot veto a transition.</summary>
internal sealed class ReviewThreadLifecycle(ReviewJournal journal) : IThreadLifecycleContributor
{
    /// <inheritdoc />
    public Task OnThreadStartedAsync(SessionThread thread, CancellationToken cancellationToken = default)
    {
        journal.Write("thread started");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnThreadDeletingAsync(SessionThread thread, CancellationToken cancellationToken = default)
    {
        journal.Write("thread deleting");
        return Task.CompletedTask;
    }
}

/// <summary>Observes the start and end of every turn.</summary>
internal sealed class ReviewTurnLifecycle(ReviewJournal journal) : ITurnLifecycleContributor
{
    /// <inheritdoc />
    public Task OnTurnStartedAsync(
        TurnLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        journal.Write("turn started");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnTurnEndedAsync(
        TurnLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        journal.Write($"turn ended status={context.Status} failed={context.Error is not null}");
        return Task.CompletedTask;
    }
}

/// <summary>Observes the thread runtime signals no turn callback expresses: plan confirmation, compaction,
/// and the thread-maintenance transitions.</summary>
/// <remarks>Delivery is a bounded queue drained by one pump, so a slow contributor costs dropped signals
/// rather than a stalled turn.</remarks>
internal sealed class ReviewRuntimeSignals(ReviewJournal journal) : IThreadRuntimeSignalContributor
{
    /// <summary>The prefix every observed signal is journalled under.</summary>
    internal const string LinePrefix = "runtime signal";

    /// <inheritdoc />
    public Task OnThreadRuntimeSignalAsync(
        ThreadRuntimeSignalContext context,
        CancellationToken cancellationToken = default)
    {
        journal.Write($"{LinePrefix} {context.Signal}");
        return Task.CompletedTask;
    }
}
