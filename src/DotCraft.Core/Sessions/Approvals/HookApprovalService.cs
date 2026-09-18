using DotCraft.Hooks;
using DotCraft.Security;
using DotCraft.Security.ShellCommands;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

internal sealed class HookApprovalService(
    IApprovalService inner,
    HookRunner hookRunner,
    string threadId,
    string turnId,
    string? workspacePath,
    bool stopHookActive,
    ILogger? logger = null) : IApprovalService, IApprovalServiceDecorator
{
    IApprovalService IApprovalServiceDecorator.GetInnerApprovalService(ApprovalContext? context) => inner;

    public async Task<bool> RequestFileApprovalAsync(
        string operation,
        string path,
        ApprovalContext? context = null)
    {
        var hookContext = BuildContext("file", operation, path, context);
        if (!await RunPermissionHookAsync(hookContext).ConfigureAwait(false))
            return false;

        return await inner.RequestFileApprovalAsync(operation, path, context).ConfigureAwait(false);
    }

    public async Task<bool> RequestShellApprovalAsync(
        ShellApprovalRequest request,
        ApprovalContext? context = null)
    {
        var hookContext = BuildContext("shell", request.Command, request.WorkingDirectory, context);
        hookContext["command"] = request.Command;
        hookContext["workingDir"] = request.WorkingDirectory;
        hookContext["working_dir"] = request.WorkingDirectory;
        hookContext["shell"] = request.Shell.Kind.ToString();
        hookContext["shellExecutable"] = request.Shell.ExecutablePath;
        hookContext["commands"] = request.Commands;
        hookContext["risk"] = request.Risk.ToString();
        hookContext["reasons"] = request.Reasons;
        if (!await RunPermissionHookAsync(hookContext).ConfigureAwait(false))
            return false;

        return await inner.RequestShellApprovalAsync(request, context).ConfigureAwait(false);
    }

    public async Task<bool> RequestResourceApprovalAsync(
        string kind,
        string operation,
        string target,
        ApprovalContext? context = null)
    {
        var hookContext = BuildContext(kind, operation, target, context);
        if (!await RunPermissionHookAsync(hookContext).ConfigureAwait(false))
            return false;

        return await inner.RequestResourceApprovalAsync(kind, operation, target, context).ConfigureAwait(false);
    }

    private async Task<bool> RunPermissionHookAsync(Dictionary<string, object?> hookContext)
    {
        try
        {
            var result = await hookRunner.RunAsync(
                HookEvent.PermissionRequest,
                new HookInput
                {
                    SessionId = threadId,
                    TurnId = turnId,
                    Cwd = workspacePath,
                    ToolName = nameof(HookEvent.PermissionRequest),
                    ToolArgs = hookContext,
                    StopHookActive = stopHookActive
                },
                CancellationToken.None).ConfigureAwait(false);
            return !result.Blocked;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "PermissionRequest hook failed for thread {ThreadId}", threadId);
            return true;
        }
    }

    private static Dictionary<string, object?> BuildContext(
        string approvalType,
        string operation,
        string target,
        ApprovalContext? context) =>
        new(StringComparer.Ordinal)
        {
            ["approvalType"] = approvalType,
            ["approval_type"] = approvalType,
            ["kind"] = approvalType,
            ["operation"] = operation,
            ["target"] = target,
            ["contextSource"] = context?.Source,
            ["context_source"] = context?.Source,
            ["userId"] = context?.UserId,
            ["user_id"] = context?.UserId,
            ["userRole"] = context?.UserRole,
            ["user_role"] = context?.UserRole,
            ["groupId"] = context?.GroupId,
            ["group_id"] = context?.GroupId
        };
}
