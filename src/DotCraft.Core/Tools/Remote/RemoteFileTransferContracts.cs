namespace DotCraft.Tools;

/// <summary>A binary file or recursive directory copy between the Agent and its captured remote route.</summary>
public sealed record RemoteFileTransferRequest(string Direction, string LocalPath, string RemotePath, bool Overwrite = false);

public sealed record RemoteFileTransferProgress(
    string Stage,
    string Direction,
    string LocalPath,
    string RemotePath,
    string HostId,
    string HostDisplayName,
    long TransferredBytes,
    int CompletedFiles,
    long CompletedBytes,
    long? TotalBytes = null,
    int? TotalFiles = null,
    string? CurrentFile = null)
{
    public string Kind { get; init; } = "remoteFileTransfer";
}

/// <summary>Model-safe copy outcome; completed files remain available after a partial failure.</summary>
public sealed record RemoteFileTransferResult(bool Success, string Direction, string LocalPath, string RemotePath,
    int CompletedFiles, long CompletedBytes, string? ErrorCode = null, string? Error = null,
    string? HostId = null, string? HostDisplayName = null, long? TotalBytes = null,
    long? TransferredBytes = null, int? TotalFiles = null, string? CurrentFile = null);

/// <summary>The Agent-local filesystem boundary used by file transfer.</summary>
public sealed record RemoteLocalWorkspace(string WorkspacePath, string? UserDataPath,
    DotCraft.Security.IApprovalService ApprovalService, DotCraft.Security.PathBlacklist? Blacklist = null,
    bool RequireApprovalOutsideWorkspace = true, IReadOnlyList<string>? WorkspaceRoots = null,
    long MaxTransferBytes = 10L * 1024 * 1024 * 1024);

/// <summary>Optional remote file capabilities implemented by the execution client.</summary>
public interface IRemoteFileTransferClient
{
    /// <summary>Copies bytes without exposing them to the model.</summary>
    ValueTask<RemoteFileTransferResult> TransferAsync(string threadId, RemoteFileTransferRequest request,
        RemoteLocalWorkspace local, CancellationToken cancellationToken = default,
        Action<RemoteFileTransferProgress>? reportProgress = null);
}
