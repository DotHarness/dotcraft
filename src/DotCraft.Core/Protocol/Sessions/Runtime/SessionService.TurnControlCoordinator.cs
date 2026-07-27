namespace DotCraft.Protocol;

public sealed partial class SessionService
{
    private sealed class TurnControlCoordinator(SessionService owner)
    {
        public Task ResolveApproval(
            string threadId,
            string turnId,
            string requestId,
            SessionApprovalDecision decision)
        {
            if (owner.TryGetTurnRuntime(new TurnKey(threadId, turnId))?.PendingApproval is { } svc)
                svc.TryResolve(requestId, decision);
            return Task.CompletedTask;
        }

        public Task ResolveUserInputRequest(
            string threadId,
            string turnId,
            string requestId,
            RequestUserInputResponse response)
        {
            if (owner.TryGetTurnRuntime(new TurnKey(threadId, turnId))?.PendingUserInput is { } svc)
                svc.TryResolve(requestId, response);
            return Task.CompletedTask;
        }

        public Task CancelTurn(string threadId, string turnId)
        {
            if (owner.TryGetTurnRuntime(new TurnKey(threadId, turnId))?.Cancellation is { } cts)
                cts.Cancel();
            return Task.CompletedTask;
        }

        public Task CancelThreadMaintenance(string threadId)
        {
            if (owner._runtimeRegistry.TryGetRuntime(threadId, out var runtime)
                && runtime.Maintenance is { } maintenance)
            {
                try
                {
                    maintenance.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Maintenance reached its terminal state while the interrupt request was in flight.
                }
            }

            return Task.CompletedTask;
        }

        public async Task CleanBackgroundTerminalsAsync(string threadId, CancellationToken ct)
        {
            if (owner.BackgroundTerminalService == null)
                return;

            await owner.BackgroundTerminalService.CleanThreadAsync(threadId, ct);
        }
    }
}
