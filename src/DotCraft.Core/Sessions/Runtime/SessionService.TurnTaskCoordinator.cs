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
