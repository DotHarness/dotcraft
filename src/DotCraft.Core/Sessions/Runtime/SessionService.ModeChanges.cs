using DotCraft.Agents;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private async Task CommitThreadModeAsync(SessionThread thread, string mode, CancellationToken ct)
    {
        if (mode is not ("agent" or "plan"))
            throw new ArgumentException("Mode must be 'agent' or 'plan'.", nameof(mode));
        ct.ThrowIfCancellationRequested();
        if (thread.Configuration?.Mode == mode)
            return;

        var candidate = thread.Configuration is { } configuration
            ? ThreadConfigurationCloner.Clone(configuration)
            : new ThreadConfiguration();
        candidate.Mode = mode;
        if (thread.Ephemeral)
            thread.Configuration = candidate;
        else
        {
            var captured = CaptureRunningTurnItems(thread);
            await persistence.CommitThreadConfigurationAsync(thread, candidate, ct);
            SetMaterializedIfCurrent(thread, materialized: true);
            MarkTurnItemsPersisted(captured);
            ThreadWorkingSet.Trim(thread);
        }

        if (!thread.Turns.Any(turn => turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput)
            && _runtimeRegistry.TryGetRuntime(thread.Id, out var runtime)
            && runtime.ModeManager is { } modeManager)
        {
            modeManager.SwitchMode(mode == "plan" ? AgentMode.Plan : AgentMode.Agent);
        }
        ThreadUpdatedForBroadcast?.Invoke(thread);
    }
}
