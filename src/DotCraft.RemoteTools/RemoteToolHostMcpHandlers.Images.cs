using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Security;
using DotCraft.Tools;
using ModelContextProtocol.Protocol;

namespace DotCraft.RemoteTools;

internal sealed partial class RemoteToolHostMcpHandlers
{
    private async ValueTask<JsonNode?> WriteImageAsync(JsonRpcRequest request, string peerId, CancellationToken ct)
    {
        var input = Deserialize<RemoteImageWriteRequest>(request);
        using var call = EnterCall(input.LeaseId, input.WorkspaceId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, call.Token);
        ct = linked.Token;
        var root = ValidateLease(input.LeaseId, input.WorkspaceId);
        var state = RequireState();
        var peer = RequirePeer(state, peerId, input.WorkspaceId);
        if (!state.Workspaces.TryGetValue(input.WorkspaceId, out var registered) || !PathsEqual(root, registered))
            throw new RemoteToolHostException(RemoteToolErrorCodes.WorkspaceNotFound, "The image workspace is unavailable.");
        var config = HostWorkspaceRuntime.LoadWorkspaceConfig(_storage.GlobalConfigPath, root);
        var limit = config.Tools.File.MaxFileSize;
        if (input.ImageBase64.Length > ((long)limit + 2) / 3 * 4)
            throw new RemoteToolHostException(RemoteToolErrorCodes.RemotePolicyDenied, "Generated image exceeds the file size limit.");
        var bytes = Convert.FromBase64String(input.ImageBase64);
        if (bytes.Length > limit || bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new RemoteToolHostException(RemoteToolErrorCodes.RemotePolicyDenied, "Generated image must be a PNG within the file size limit.");
        var path = Path.Combine(root, ".craft", "generated_images", ImageSegment(input.ThreadId), ImageSegment(input.CallId) + ".png");
        var arguments = new Dictionary<string, JsonElement> { ["path"] = JsonSerializer.SerializeToElement(path) };
        var approval = new HostInvocationApprovalService.Invocation(peer, input.CallId, root, _approvalPresenter, ct);
        using var scope = HostInvocationApprovalService.Begin(approval);
        await AuthorizeAsync("WriteFile", arguments, state, approval, root, ct).ConfigureAwait(false);
        var guard = new FileAccessGuard(root, approvalService: new HostInvocationApprovalService(),
            blacklist: new PathBlacklist(config.Security.BlacklistedPaths));
        if (guard.RequiresOutsideWorkspaceApproval(path, "write"))
            throw new RemoteToolHostException(RemoteToolErrorCodes.RemotePolicyDenied, "Image destination leaves the workspace.");
        var error = await guard.ValidatePathAsync(path, "write", path, ct).ConfigureAwait(false);
        if (error is not null)
            throw new RemoteToolHostException(RemoteToolErrorCodes.RemotePolicyDenied, error);
        if (RequirePeer(RequireState(), peerId, input.WorkspaceId).AuthorizationRevision != peer.AuthorizationRevision)
            throw new RemoteToolHostException(RemoteToolErrorCodes.RemotePolicyDenied, "Authorization changed.");
        ValidateLease(input.LeaseId, input.WorkspaceId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
                await file.WriteAsync(bytes, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            CommitArtifact(input.LeaseId, input.WorkspaceId, () => File.Move(temporary, path, overwrite: false));
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return JsonSerializer.SerializeToNode(new RemoteImageWriteResponse(path), RemoteToolHostProtocol.JsonOptions);
    }

    private static string ImageSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200
            || value.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '_' and not '-'))
            throw new RemoteToolHostException(RemoteToolErrorCodes.RemotePolicyDenied, "Invalid image artifact identifier.");
        return value;
    }
}
