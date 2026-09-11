using System.ComponentModel;
using System.Text.Json;
using DotCraft.Agents;

namespace DotCraft.Tools;

internal sealed class RemoteToolHostTransferTools(IRemoteToolHostClient client, RemoteLocalWorkspace? local)
{
    [GeneratedTool(Name = "Transfer")]
    [Description("Upload or download files and directories between local and connected remote workspaces; directories merge into the destination.")]
    public async Task<string> Transfer(
        [Description("Upload or download.")] string direction,
        [Description("Upload source or download destination, relative to the local workspace or absolute.")] string localPath,
        [Description("Upload destination or download source, relative to the remote workspace or absolute.")] string remotePath,
        [Description("Replace existing destination files.")] bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var scope = ToolHostExecutionScope.Current ?? throw new InvalidOperationException("Transfer requires an active Session turn.");
        if (client is not IRemoteFileTransferClient transfer)
            throw new RemoteToolHostException(RemoteToolErrorCodes.ProtocolMismatch, "File transfer is unavailable. Upgrade the Agent and Satellite.");
        var workspace = local is null ? new RemoteLocalWorkspace(scope.WorkspacePath, null, scope.ApprovalService)
            : local with { WorkspacePath = scope.WorkspacePath, ApprovalService = scope.ApprovalService };
        var result = await transfer.TransferAsync(scope.ThreadId, new(direction, localPath, remotePath, overwrite),
            workspace, cancellationToken, StreamingToolInvocationRuntimeScope.ReportProgress).ConfigureAwait(false);
        return JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
    }
}
