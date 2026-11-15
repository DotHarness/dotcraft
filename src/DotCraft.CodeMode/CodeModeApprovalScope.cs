using DotCraft.Security;
using DotCraft.Security.ShellCommands;
using DotCraft.Sessions;
using DotCraft.Tools;

namespace DotCraft.CodeMode;

internal static class CodeModeApprovalScope
{
    public static IDisposable? Enter(CodeModeApprovals approvals, CancellationToken cancellation)
    {
        if (ToolHostExecutionScope.Current is not { } host)
            return null;
        var approval = new TrackedApprovalService(host.ApprovalService, approvals, cancellation);
        var sessionScope = SessionScopedApprovalService.SetOverride(approval);
        var hostScope = ToolHostExecutionScope.Set(host with { ApprovalService = approval });
        return new Scope(hostScope, sessionScope);
    }

    private sealed class Scope(IDisposable hostScope, IDisposable sessionScope) : IDisposable
    {
        public void Dispose()
        {
            hostScope.Dispose();
            sessionScope.Dispose();
        }
    }

    private sealed class TrackedApprovalService(IApprovalService inner, CodeModeApprovals approvals, CancellationToken cancellation)
        : IApprovalService, IApprovalServiceDecorator
    {
        public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null) =>
            TrackAsync(() => inner.RequestFileApprovalAsync(operation, path, context));

        public Task<bool> RequestShellApprovalAsync(ShellApprovalRequest request, ApprovalContext? context = null) =>
            TrackAsync(() => inner.RequestShellApprovalAsync(request, context));

        public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null) =>
            TrackAsync(() => inner.RequestResourceApprovalAsync(kind, operation, target, context));

        public Task<bool> RequestResourceApprovalAsync(ResourceApprovalRequest request, ApprovalContext? context = null) =>
            TrackAsync(() => inner.RequestResourceApprovalAsync(request, context));

        public IApprovalService? GetInnerApprovalService(ApprovalContext? context) => inner;

        private async Task<bool> TrackAsync(Func<Task<bool>> request)
        {
            approvals.Begin();
            try
            {
                return await request().WaitAsync(cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return false;
            }
            finally
            {
                approvals.End();
            }
        }
    }
}

/// <summary>Tracks a cell's pending approvals: the cell does not yield while any is pending.</summary>
internal sealed class CodeModeApprovals
{
    private readonly Lock _gate = new();
    private int _pending;
    private TaskCompletionSource? _cleared;

    public void Begin()
    {
        lock (_gate)
        {
            if (_pending++ == 0)
                _cleared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void End()
    {
        lock (_gate)
        {
            if (--_pending == 0)
                _cleared!.TrySetResult();
        }
    }

    public Task WhenClearAsync()
    {
        lock (_gate)
            return _pending == 0 ? Task.CompletedTask : _cleared!.Task;
    }
}
