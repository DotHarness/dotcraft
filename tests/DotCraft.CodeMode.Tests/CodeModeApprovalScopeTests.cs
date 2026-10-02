using DotCraft.Security;
using DotCraft.Security.ShellCommands;
using DotCraft.Tools;
using DotCraft.Utilities;

namespace DotCraft.CodeMode.Tests;

public sealed class CodeModeApprovalScopeTests
{
    [Fact]
    public async Task PendingApproval_PausesTheScriptDeadlineUntilItReturns()
    {
        using var deadline = new PausableDeadline(TimeSpan.FromSeconds(1), CancellationToken.None);
        var approvals = new DelayedApprovalService(TimeSpan.FromMilliseconds(2500));
        using var hostScope = ToolHostExecutionScope.Set(
            new ToolHostExecutionContext("thread_1", "turn_1", Path.GetTempPath(), approvals, null!));

        bool approved;
        using (CodeModeApprovalScope.Enter(deadline))
            approved = await ToolHostExecutionScope.Current!.ApprovalService.RequestFileApprovalAsync("write", "a.txt");

        Assert.True(approved);
        Assert.False(deadline.Token.IsCancellationRequested);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Task.Delay(Timeout.Infinite, deadline.Token).WaitAsync(TimeSpan.FromSeconds(30)));
    }

    private sealed class DelayedApprovalService(TimeSpan delay) : IApprovalService
    {
        public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null) => ApproveAsync();

        public Task<bool> RequestShellApprovalAsync(ShellApprovalRequest request, ApprovalContext? context = null) => ApproveAsync();

        public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null) => ApproveAsync();

        private async Task<bool> ApproveAsync()
        {
            await Task.Delay(delay);
            return true;
        }
    }
}
