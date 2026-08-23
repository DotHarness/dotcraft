using DotCraft.Contributions;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    /// <summary>Dispatches thread and turn lifecycle contributions, and releases a thread's scoped contributions on teardown. Dispatch is observation only: a contributor that throws is logged and skipped.</summary>
    private sealed class ContributionLifecycleCoordinator(SessionService owner)
    {
        private IContributionView? Contributions => owner.AgentFactory.RuntimeContext.Contributions;

        /// <summary>Reports a newly created thread, forks included.</summary>
        public Task ThreadStartedAsync(SessionThread thread, CancellationToken cancellationToken) =>
            DispatchThreadAsync(
                thread,
                static (contributor, target, ct) => contributor.OnThreadStartedAsync(target, ct),
                "started",
                cancellationToken);

        /// <summary>Reports an explicitly resumed thread.</summary>
        public Task ThreadResumedAsync(SessionThread thread, CancellationToken cancellationToken) =>
            DispatchThreadAsync(
                thread,
                static (contributor, target, ct) => contributor.OnThreadResumedAsync(target, ct),
                "resumed",
                cancellationToken);

        /// <summary>Reports a thread about to be deleted, before any of its state is removed. Must run before <see cref="ReleaseThreadContributions"/>.</summary>
        public Task ThreadDeletingAsync(SessionThread thread, CancellationToken cancellationToken) =>
            DispatchThreadAsync(
                thread,
                static (contributor, target, ct) => contributor.OnThreadDeletingAsync(target, ct),
                "deleting",
                cancellationToken);

        /// <summary>Disposes every contribution scoped to a thread being torn down. Called on permanent deletion only: an archived thread stays restorable and could not re-acquire released contributions.</summary>
        public void ReleaseThreadContributions(string threadId)
        {
            if (Contributions is not IContributionRegistry registry)
                return;

            try
            {
                var released = registry.ReleaseThread(threadId);
                if (released > 0)
                {
                    owner.Logger?.LogDebug(
                        "Released {Count} thread-scoped contributions for thread {ThreadId}.",
                        released,
                        threadId);
                }
            }
            catch (Exception ex)
            {
                owner.Logger?.LogWarning(
                    ex,
                    "Failed to release thread-scoped contributions for thread {ThreadId}.",
                    threadId);
            }
        }

        /// <summary>Reports a turn that has just become observable.</summary>
        public Task TurnStartedAsync(TurnKey turnKey, CancellationToken cancellationToken)
        {
            owner._turnLifecycleStarted[turnKey] = 0;
            return DispatchTurnAsync(
                new TurnLifecycleContext(turnKey.ThreadId, turnKey.TurnId),
                static (contributor, context, ct) => contributor.OnTurnStartedAsync(context, ct),
                "started",
                cancellationToken);
        }

        /// <summary>Reports a turn that reached a terminal state. Does nothing for a turn that never reported a start, keeping the two callbacks paired.</summary>
        public Task TurnEndedAsync(TurnKey turnKey, TurnStatus? status, string? error)
        {
            if (!owner._turnLifecycleStarted.TryRemove(turnKey, out _))
                return Task.CompletedTask;

            var context = new TurnLifecycleContext(turnKey.ThreadId, turnKey.TurnId)
            {
                Status = status,
                Error = error
            };
            return DispatchTurnAsync(
                context,
                static (contributor, ctx, ct) => contributor.OnTurnEndedAsync(ctx, ct),
                "ended",
                CancellationToken.None);
        }

        /// <summary>Collects container-registered observers then the registry contributions, de-duplicated by instance so an object registered both ways is called once.</summary>
        private IReadOnlyList<IThreadLifecycleContributor> ResolveThreadContributors(string threadId)
        {
            var observers = owner._threadLifecycleObservers;
            var contributed = Contributions?.Resolve<IThreadLifecycleContributor>(threadId) ?? [];
            if (contributed.Count == 0)
                return observers;
            if (observers.Count == 0)
                return contributed;

            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var combined = new List<IThreadLifecycleContributor>(observers.Count + contributed.Count);
            foreach (var contributor in observers.Concat(contributed))
            {
                if (seen.Add(contributor))
                    combined.Add(contributor);
            }

            return combined;
        }

        private Task DispatchThreadAsync(
            SessionThread thread,
            Func<IThreadLifecycleContributor, SessionThread, CancellationToken, Task> callback,
            string transition,
            CancellationToken cancellationToken) =>
            ContributionRead.FanoutAsync(
                ResolveThreadContributors(thread.Id),
                (contributor, token) => new ValueTask(callback(contributor, thread, token)),
                (contributor, ex) => owner.Logger?.LogWarning(
                    ex,
                    "Thread lifecycle contributor {ContributorType} failed on {Transition} for thread {ThreadId}.",
                    contributor.GetType().FullName,
                    transition,
                    thread.Id),
                cancellationToken).AsTask();

        private Task DispatchTurnAsync(
            TurnLifecycleContext context,
            Func<ITurnLifecycleContributor, TurnLifecycleContext, CancellationToken, Task> callback,
            string transition,
            CancellationToken cancellationToken) =>
            ContributionRead.FanoutAsync(
                Contributions?.Resolve<ITurnLifecycleContributor>(context.ThreadId),
                (contributor, token) => new ValueTask(callback(contributor, context, token)),
                (contributor, ex) => owner.Logger?.LogWarning(
                    ex,
                    "Turn lifecycle contributor {ContributorType} failed on {Transition} for thread {ThreadId}, turn {TurnId}.",
                    contributor.GetType().FullName,
                    transition,
                    context.ThreadId,
                    context.TurnId),
                cancellationToken).AsTask();
    }
}
