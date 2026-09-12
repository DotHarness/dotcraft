using System.Text.Json.Nodes;
using DotCraft.Security;
using DotCraft.Tools;

namespace DotCraft.RemoteTools;

public sealed partial class RemoteExecutionSession
{
    public async ValueTask<RemoteFileTransferResult> TransferAsync(string threadId, RemoteFileTransferRequest request,
        RemoteLocalWorkspace local, CancellationToken cancellationToken = default,
        Action<RemoteFileTransferProgress>? reportProgress = null)
    {
        if (request.Direction is not ("upload" or "download"))
            return new(false, request.Direction, request.LocalPath, request.RemotePath, 0, 0,
                ToolErrorCodes.InputInvalid, "Direction must be upload or download.");
        using var operation = _operations.Enter(cancellationToken);
        return await TransferOnLeaseAsync(RequireLease(Route), request, local, threadId, reportProgress, operation.Token).ConfigureAwait(false);
    }

    private static IApprovalService ResolveEffectiveApprovalService(IApprovalService service)
    {
        var current = service;
        for (var depth = 0; depth < 16 && current is IApprovalServiceDecorator decorator; depth++)
        {
            var inner = decorator.GetInnerApprovalService(context: null);
            if (inner is null || ReferenceEquals(inner, current))
                break;
            current = inner;
        }
        return current;
    }

    private SessionLease RequireLease(RemoteToolRoute route)
    {
        if (!IsAvailable || route != Route)
            throw new RemoteToolHostException(RemoteToolErrorCodes.LeaseLost, "The remote execution session was lost.");
        return _lease;
    }

    private async Task<RemoteFileTransferResult> TransferOnLeaseAsync(SessionLease lease, RemoteFileTransferRequest request,
        RemoteLocalWorkspace local, string threadId, Action<RemoteFileTransferProgress>? reportProgress,
        CancellationToken ct)
    {
        var upload = request.Direction == "upload";
        var guard = new FileAccessGuard(local.WorkspacePath, local.RequireApprovalOutsideWorkspace,
            ResolveEffectiveApprovalService(local.ApprovalService), local.Blacklist,
            local.UserDataPath is null ? [] : [local.UserDataPath], local.WorkspaceRoots);
        string localPath = request.LocalPath, remotePath = request.RemotePath;
        var completed = 0;
        long bytes = 0;
        long transferredBytes = 0;
        long? totalBytes = null;
        int? totalFiles = null;
        string? currentFile = null;
        var hostDisplayName = HostDisplayName;
        var lastProgressAt = 0L;
        FileTransferOpened? opened = null;
        FileTransferSession? receiver = null;
        var committingRemote = false;
        void ValidateRoute()
        {
            RequireLease(lease.Route);
            ct.ThrowIfCancellationRequested();
        }
        async Task SendPart(string method, FileTransferPart part)
        {
            ValidateRoute();
            _ = await SendAsync<FileTransferPart, JsonObject>(lease.Session.Client, method, part, ct).ConfigureAwait(false);
        }
        void Report(string stage, bool force = false)
        {
            if (reportProgress is null)
                return;
            var now = Environment.TickCount64;
            if (!force && now - lastProgressAt < 100)
                return;
            lastProgressAt = now;
            reportProgress(new(stage, request.Direction, localPath, remotePath,
                lease.Route.HostId, hostDisplayName, transferredBytes, completed, bytes,
                totalBytes, totalFiles, currentFile));
        }
        try
        {
            Report("preparing", force: true);
            localPath = guard.ResolvePath(localPath);
            await TransferFileTree.ValidateAsync(guard, localPath, upload ? "read" : "write", ct).ConfigureAwait(false);
            var manifest = upload ? await TransferFileTree.DescribeAsync(localPath, guard, local.MaxTransferBytes, ct).ConfigureAwait(false) : null;
            ValidateRoute();
            opened = await SendAsync<FileTransferOpen, FileTransferOpened>(lease.Session.Client, RemoteFileTransferProtocol.Open,
                new(lease.Route.LeaseId, lease.Route.WorkspaceId, remotePath, upload, request.Overwrite, manifest), ct).ConfigureAwait(false);
            remotePath = opened.Path;
            manifest ??= opened.Manifest;
            totalBytes = manifest.Entries.Where(static entry => !entry.IsDirectory).Sum(static entry => entry.Length);
            totalFiles = manifest.Entries.Count(static entry => !entry.IsDirectory);
            currentFile = manifest.Entries.FirstOrDefault(static entry => !entry.IsDirectory)?.Path;
            if (string.IsNullOrEmpty(currentFile) && totalFiles > 0)
                currentFile = Path.GetFileName(upload ? localPath : remotePath);
            Report("transferring", force: true);
            if (!upload)
            {
                receiver = new(localPath, manifest, true, request.Overwrite);
                await receiver.PrepareAsync(local.MaxTransferBytes,
                    (path, op, token) => TransferFileTree.ValidateAsync(guard, path, op, token), ct).ConfigureAwait(false);
            }
            for (var index = 0; index < manifest.Entries.Count; index++)
            {
                var entry = manifest.Entries[index];
                var path = TransferFileTree.ResolveEntry(localPath, entry.Path);
                await TransferFileTree.ValidateAsync(guard, path, upload ? "read" : "write", ct).ConfigureAwait(false);
                if (!entry.IsDirectory)
                {
                    currentFile = string.IsNullOrEmpty(entry.Path)
                        ? Path.GetFileName(upload ? localPath : remotePath)
                        : entry.Path;
                    await using var source = upload ? TransferFileTree.OpenRead(path) : null;
                    if (source is not null && source.Length != entry.Length) throw new IOException("Transfer source changed.");
                    long offset = 0;
                    do
                    {
                        ValidateRoute();
                        if (upload)
                        {
                            var block = new byte[(int)Math.Min(RemoteFileTransferProtocol.ChunkBytes, entry.Length - offset)];
                            await source!.ReadExactlyAsync(block, ct).ConfigureAwait(false);
                            await SendPart(RemoteFileTransferProtocol.Write,
                                new(opened.TransferId, index, offset, Convert.ToBase64String(block))).ConfigureAwait(false);
                            offset += block.Length;
                            transferredBytes += block.Length;
                        }
                        else
                        {
                            var chunk = await SendAsync<FileTransferPart, FileTransferChunk>(lease.Session.Client,
                                RemoteFileTransferProtocol.Read, new(opened.TransferId, index, offset), ct).ConfigureAwait(false);
                            if (chunk.Base64.Length > (RemoteFileTransferProtocol.ChunkBytes + 2) / 3 * 4)
                                throw new IOException("Remote transfer chunk exceeds the transport limit.");
                            var block = Convert.FromBase64String(chunk.Base64);
                            if (block.Length == 0 && offset < entry.Length) throw new IOException("Unexpected end of remote file.");
                            await receiver!.WriteAsync(index, offset, block, ct).ConfigureAwait(false);
                            offset += block.Length;
                            transferredBytes += block.Length;
                        }
                        Report("transferring");
                    } while (offset < entry.Length);
                }
                ValidateRoute();
                if (upload)
                {
                    committingRemote = true;
                    await SendPart(RemoteFileTransferProtocol.Commit, new(opened.TransferId, index)).ConfigureAwait(false);
                    committingRemote = false;
                }
                else await receiver!.CommitAsync(index, action => { ValidateRoute(); action(); }, ct).ConfigureAwait(false);
                if (!entry.IsDirectory) { completed++; bytes += entry.Length; }
                Report("transferring", force: true);
            }
            currentFile = null;
            return new(true, request.Direction, localPath, remotePath, completed, bytes,
                HostId: lease.Route.HostId, HostDisplayName: hostDisplayName, TotalBytes: totalBytes,
                TransferredBytes: transferredBytes, TotalFiles: totalFiles);
        }
        catch (Exception ex)
        {
            var code = ex is RemoteToolHostException remote ? remote.Code
                : committingRemote ? RemoteToolErrorCodes.RemoteOutcomeUnknown
                : ex is OperationCanceledException ? ToolErrorCodes.Cancelled : ToolErrorCodes.ExecutionFailed;
            return new(false, request.Direction, localPath, remotePath, completed, bytes, code,
                committingRemote && ex is not RemoteToolHostException ? "Remote commit outcome is unknown; it was not retried." : ex.Message,
                lease.Route.HostId, hostDisplayName, totalBytes, transferredBytes, totalFiles, currentFile);
        }
        finally
        {
            if (receiver is not null) await receiver.DisposeAsync().ConfigureAwait(false);
            if (opened is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await SendAsync<FileTransferPart, JsonObject>(lease.Session.Client, RemoteFileTransferProtocol.Close,
                        new(opened.TransferId), timeout.Token).ConfigureAwait(false);
                }
                catch { }
            }
        }
    }
}
