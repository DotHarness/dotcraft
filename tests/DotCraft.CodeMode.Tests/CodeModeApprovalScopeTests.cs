using DotCraft.Security;
using DotCraft.Security.ShellCommands;
using DotCraft.Tools;

namespace DotCraft.CodeMode.Tests;

public sealed class CodeModeApprovalScopeTests
{
    [Fact]
    public async Task PendingApproval_HoldsTheYieldUntilItReturns()
    {
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var approvals = new CodeModeApprovals();
        using var hostScope = ToolHostExecutionScope.Set(
            new ToolHostExecutionContext("thread_1", "turn_1", Path.GetTempPath(), new PendingApprovalService(decision.Task), null!));

        Task<bool> approval;
        using (CodeModeApprovalScope.Enter(approvals, CancellationToken.None))
            approval = ToolHostExecutionScope.Current!.ApprovalService.RequestFileApprovalAsync("write", "a.txt");
        var cleared = approvals.WhenClearAsync();
        Assert.False(cleared.IsCompleted);

        decision.SetResult(true);

        Assert.True(await approval.WaitAsync(TimeSpan.FromSeconds(30)));
        await cleared.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task PendingApproval_IsDeniedWhenTheCellEnds_EvenIfApprovedLater()
    {
        using var cellEnded = new CancellationTokenSource();
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var hostScope = ToolHostExecutionScope.Set(
            new ToolHostExecutionContext("thread_1", "turn_1", Path.GetTempPath(), new PendingApprovalService(decision.Task), null!));

        Task<bool> approval;
        using (CodeModeApprovalScope.Enter(new CodeModeApprovals(), cellEnded.Token))
            approval = ToolHostExecutionScope.Current!.ApprovalService.RequestFileApprovalAsync("write", "a.txt");
        await cellEnded.CancelAsync();
        decision.SetResult(true);

        Assert.False(await approval.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    private sealed class PendingApprovalService(Task<bool> decision) : IApprovalService
    {
        public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null) => decision;

        public Task<bool> RequestShellApprovalAsync(ShellApprovalRequest request, ApprovalContext? context = null) => decision;

        public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null) => decision;
    }
}
