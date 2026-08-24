using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    /// <summary>
    /// Owns the lifetime around an admitted Turn task. The task itself may run for a
    /// long time; only its final live-state transition returns to the Thread command
    /// owner.
    /// </summary>
    private static class TurnTaskCoordinator
    {
        public static void Start(
            SessionService owner,
            ThreadRuntime runtime,
            TurnKey turnKey,
            long runtimeGeneration,
            Func<Task> run,
            SessionEventChannel eventChannel)
        {
            _ = RunAsync();

            async Task RunAsync()
            {
                try
                {
                    await Task.Run(run, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Regular Turn execution owns expected failure projection. Reaching this
                    // boundary means the task failed outside that policy, so keep it observable
                    // without allowing an unobserved background exception.
                    owner.Logger?.LogError(
                        ex,
                        "Regular Turn task terminated unexpectedly for thread {ThreadId}, turn {TurnId}.",
                        turnKey.ThreadId,
                        turnKey.TurnId);
                }
                finally
                {
                    // Ahead of CompleteAsync, so contributors observe the Turn end before the client's event stream closes.
                    await NotifyTurnEndedAsync().ConfigureAwait(false);

                    try
                    {
                        await CompleteAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        owner.Logger?.LogError(
                            ex,
                            "Turn task cleanup failed for thread {ThreadId}, turn {TurnId}.",
                            turnKey.ThreadId,
                            turnKey.TurnId);
                    }
                }
            }

            // Runs for every admitted Turn on every outcome, which is what makes it the authoritative "Turn ended" boundary.
            async Task NotifyTurnEndedAsync()
            {
                try
                {
                    var turn = runtime.Thread.Turns
                        .LastOrDefault(candidate =>
                            string.Equals(candidate.Id, turnKey.TurnId, StringComparison.Ordinal));
                    await owner.ContributionLifecycle
                        .TurnEndedAsync(turnKey, turn?.Status, turn?.Error)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    owner.Logger?.LogWarning(
                        ex,
                        "Turn lifecycle dispatch failed for thread {ThreadId}, turn {TurnId}.",
                        turnKey.ThreadId,
                        turnKey.TurnId);
                }
            }

            async Task CompleteAsync()
            {
                if (!owner._runtimeRegistry.IsCurrent(turnKey.ThreadId, runtime)
                    || runtime.Generation != runtimeGeneration)
                {
                    eventChannel.Complete();
                    return;
                }

                try
                {
                    await runtime.Commands.InvokeAsync(
                        _ =>
                        {
                            if (owner._runtimeRegistry.IsCurrent(turnKey.ThreadId, runtime)
                                && runtime.Generation == runtimeGeneration
                                && runtime.TryRemoveTurn(turnKey.TurnId, out var removedState))
                            {
                                removedState.Dispose();
                            }

                            return Task.CompletedTask;
                        },
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException)
                {
                    // Runtime teardown already owns live-state cleanup.
                }
                finally
                {
                    eventChannel.Complete();
                }
            }
        }
    }
}
