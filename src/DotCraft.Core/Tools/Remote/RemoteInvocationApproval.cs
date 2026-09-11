using DotCraft.Security;

namespace DotCraft.Tools;

internal static class RemoteInvocationApproval
{
    internal static async ValueTask<ToolDispatchDecision> RequestAsync(
        IApprovalService approval, ToolExecutionLocation location, string kind, string target, string operation)
    {
        if (kind == "file" && IsWorkspacePath(target, location.WorkspacePath))
            return ToolDispatchDecision.Allow;
        var destination = $"{location.Route?.HostId}/{location.Route?.WorkspaceId}: {target}";
        var accepted = await approval.RequestResourceApprovalAsync("remoteResource", operation, destination)
            .ConfigureAwait(false);
        return accepted ? ToolDispatchDecision.Allow
            : ToolDispatchDecision.Deny(ToolErrorCodes.ApprovalRejected, "Remote operation was rejected by user.");
    }

    private static bool IsWorkspacePath(string path, string? workspace)
    {
        if (workspace is null) return false;
        path = path.Replace('\\', '/');
        var root = workspace.Replace('\\', '/').TrimEnd('/');
        if (path.Split('/').Any(segment => segment == "..") || path.Contains('$') || path.Contains('%')
            || path.StartsWith('~')) return false;
        var windows = root.Length > 1 && root[1] == ':';
        var comparison = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (path.StartsWith('/') || path.Contains(':'))
            return path.Equals(root, comparison) || path.StartsWith(root + "/", comparison);
        return true;
    }
}
