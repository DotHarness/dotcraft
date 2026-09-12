using System.Text.Json;
using DotCraft.Security;
using DotCraft.Tools;

namespace DotCraft.RemoteTools;

internal sealed partial class RemoteToolHostMcpHandlers
{
    private RemoteToolHubPeer RequirePeer(RemoteToolHostState state, string peerId, string? workspaceId = null)
    {
        if (_isPaused?.Invoke() == true)
            throw new RemoteToolHostException(RemoteToolErrorCodes.RemotePolicyDenied, "Sharing is paused.");
        var peer = state.Peers.FirstOrDefault(item => item.PeerId == peerId);
        if (peer is null || !RemoteToolAuthorization.IsValid(peer.AuthorizationMode))
            throw new RemoteToolHostException(RemoteToolErrorCodes.AuthorizationRequired,
                "The machine owner must confirm this pairing's authorization in Satellite.");
        if (workspaceId is not null && peer.WorkspaceId != workspaceId)
            throw new RemoteToolHostException(RemoteToolErrorCodes.RemotePolicyDenied,
                "This workspace is not authorized for the authenticated pairing.");
        return peer;
    }

    private static string Argument(IDictionary<string, JsonElement>? args, string name) =>
        args is not null && args.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;

    private async Task AuthorizeAsync(
        string toolName, IDictionary<string, JsonElement>? args, RemoteToolHostState state,
        HostInvocationApprovalService.Invocation approval, string workspacePath, CancellationToken ct)
    {
        var policy = state.ToolPolicies.GetValueOrDefault(toolName);
        if (string.Equals(policy, "deny", StringComparison.OrdinalIgnoreCase))
            throw new RemoteToolHostException(RemoteToolErrorCodes.RemotePolicyDenied, "Host policy denied this tool.");
        if (string.Equals(policy, "needsApproval", StringComparison.OrdinalIgnoreCase))
            await approval.RequestAsync("tool", toolName, JsonSerializer.Serialize(args), force: true).ConfigureAwait(false);

        if (toolName == "Exec")
            await approval.RequestAsync("shell", "execute", Argument(args, "command")).ConfigureAwait(false);
        else if (toolName == "WriteStdin" && Argument(args, "input").Length > 0)
            await approval.RequestAsync("terminal", "input",
                Argument(args, "sessionId") + "\n" + Argument(args, "input")).ConfigureAwait(false);
        else if (toolName == "LSP")
            await approval.RequestAsync("process", "languageServer", JsonSerializer.Serialize(args)).ConfigureAwait(false);

        if (toolName is "ReadFile" or "WriteFile" or "EditFile" or "GrepFiles" or "FindFiles" or "LSP")
        {
            var path = Argument(args, toolName == "LSP" ? "filePath" : "path");
            var operation = toolName == "EditFile" ? "edit" : toolName == "WriteFile" ? "write" : "read";
            var config = HostWorkspaceRuntime.LoadWorkspaceConfig(_storage.GlobalConfigPath, workspacePath);
            var guard = new FileAccessGuard(workspacePath, approvalService: new HostInvocationApprovalService(),
                blacklist: new PathBlacklist(config.Security.BlacklistedPaths), trustedReadPaths: [ArtifactRoot]);
            var resolved = guard.ResolvePath(path);
            ValidatePrivatePath(resolved, operation);
            var error = await guard.ValidatePathAsync(resolved, operation, path, ct).ConfigureAwait(false);
            if (error is not null)
                throw new RemoteToolHostException(RemoteToolErrorCodes.RemotePolicyDenied, error);
        }
    }

    private void ValidatePrivatePath(string path, string operation)
    {
        var hostState = new FileAccessGuard(_storage.RootPath, requireApprovalOutsideWorkspace: false);
        if (hostState.RequiresOutsideWorkspaceApproval(path, "read")) return;
        var owned = new FileAccessGuard(ArtifactRoot, requireApprovalOutsideWorkspace: false);
        if (operation is "read" or "list" && !owned.RequiresOutsideWorkspaceApproval(path, "read")) return;
        throw new RemoteToolHostException(RemoteToolErrorCodes.RemotePolicyDenied,
            "Host-private execution resources are available only to their owning session.");
    }
}
