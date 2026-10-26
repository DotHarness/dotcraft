using DotCraft.Configuration;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private const string DefaultApprovalPolicyKeyPath = "Permissions.DefaultApprovalPolicy";

    private bool _approvalPolicyChangesAttached;

    private ApprovalPolicy ResolveLiveApprovalPolicy(SessionThread thread) =>
        ResolveApprovalPolicy(thread.Configuration?.ApprovalPolicy ?? ApprovalPolicy.Default);

    private void SettlePendingApprovals(SessionThread thread)
    {
        SessionApprovalDecision? decision = ResolveLiveApprovalPolicy(thread) switch
        {
            ApprovalPolicy.AutoApprove => SessionApprovalDecision.AcceptOnce,
            ApprovalPolicy.Deny => SessionApprovalDecision.Reject,
            _ => null
        };
        if (decision is not { } settled)
            return;

        foreach (var turn in thread.Turns.Where(turn => turn.Status == TurnStatus.WaitingApproval).ToArray())
            TryGetTurnRuntime(new TurnKey(thread.Id, turn.Id))?.PendingApproval?.ResolvePending(settled);
    }

    internal void AttachApprovalPolicyChanges()
    {
        if (_approvalPolicyChangesAttached || _appConfigMonitor is null)
            return;
        _approvalPolicyChangesAttached = true;
        _appConfigMonitor.Changed += OnAppConfigChanged;
    }

    private void OnAppConfigChanged(object? sender, AppConfigChangedEventArgs args)
    {
        if (!args.Regions.Contains(DefaultApprovalPolicyKeyPath, StringComparer.Ordinal))
            return;

        foreach (var runtime in _runtimeRegistry.Values)
        {
            if (runtime.Thread.Configuration?.ApprovalPolicy is null or ApprovalPolicy.Default)
                _ = SettlePendingApprovalsOnOwnerAsync(runtime);
        }
    }

    private async Task SettlePendingApprovalsOnOwnerAsync(ThreadRuntime runtime)
    {
        try
        {
            await runtime.Commands.InvokeAsync(_ =>
            {
                SettlePendingApprovals(runtime.Thread);
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The runtime was unloaded; its Turns and their pending approvals ended with it.
        }
    }
}
