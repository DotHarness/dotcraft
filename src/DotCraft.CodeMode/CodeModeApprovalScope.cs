using DotCraft.Security;
using DotCraft.Security.ShellCommands;
using DotCraft.Sessions;
using DotCraft.Tools;
using DotCraft.Utilities;

namespace DotCraft.CodeMode;

internal static class CodeModeApprovalScope
{
    public static IDisposable? Enter(PausableDeadline deadline)
    {
        if (ToolHostExecutionScope.Current is not { } host)
            return null;
        var approval = new DeadlinePausingApprovalService(host.ApprovalService, deadline);
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

    private sealed class DeadlinePausingApprovalService(IApprovalService inner, PausableDeadline deadline)
        : IApprovalService, IApprovalServiceDecorator
    {
        public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null) =>
            PausedAsync(() => inner.RequestFileApprovalAsync(operation, path, context));

        public Task<bool> RequestShellApprovalAsync(ShellApprovalRequest request, ApprovalContext? context = null) =>
            PausedAsync(() => inner.RequestShellApprovalAsync(request, context));

        public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null) =>
            PausedAsync(() => inner.RequestResourceApprovalAsync(kind, operation, target, context));

        public Task<bool> RequestResourceApprovalAsync(ResourceApprovalRequest request, ApprovalContext? context = null) =>
            PausedAsync(() => inner.RequestResourceApprovalAsync(request, context));

        public IApprovalService? GetInnerApprovalService(ApprovalContext? context) => inner;

        private async Task<bool> PausedAsync(Func<Task<bool>> request)
        {
            deadline.Pause();
            try
            {
                return await request().ConfigureAwait(false);
            }
            finally
            {
                deadline.Resume();
            }
        }
    }
}
