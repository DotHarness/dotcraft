using DotCraft.Hooks;
using DotCraft.Security;
using Microsoft.Extensions.Logging;

namespace DotCraft.Protocol;

internal sealed class HookApprovalService(
    IApprovalService inner,
    HookRunner hookRunner,
    string threadId,
    string turnId,
    string? workspacePath,
    bool stopHookActive,
    ILogger? logger = null) : IApprovalService
{
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
        string command,
        string? workingDir,
        ApprovalContext? context = null)
    {
        var hookContext = BuildContext("shell", command, workingDir ?? string.Empty, context);
        hookContext["command"] = command;
        hookContext["workingDir"] = workingDir;
        hookContext["working_dir"] = workingDir;
        if (!await RunPermissionHookAsync(hookContext).ConfigureAwait(false))
            return false;

        return await inner.RequestShellApprovalAsync(command, workingDir, context).ConfigureAwait(false);
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
