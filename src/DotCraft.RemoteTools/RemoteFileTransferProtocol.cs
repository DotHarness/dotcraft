namespace DotCraft.RemoteTools;

internal static class RemoteFileTransferProtocol
{
    internal const int ChunkBytes = 1024 * 1024;
    internal const string Open = "dotcraft/remoteToolHost/files/open";
    internal const string Read = "dotcraft/remoteToolHost/files/read";
    internal const string Write = "dotcraft/remoteToolHost/files/write";
    internal const string Commit = "dotcraft/remoteToolHost/files/commit";
    internal const string Close = "dotcraft/remoteToolHost/files/close";
}

internal sealed record FileTransferOpen(string LeaseId, string WorkspaceId, string Path, bool Write,
    bool Overwrite = false, TransferFileManifest? Manifest = null);
internal sealed record FileTransferOpened(string TransferId, string Path, TransferFileManifest Manifest);
internal sealed record FileTransferPart(string TransferId, int Entry = 0, long Offset = 0, string? Base64 = null);
internal sealed record FileTransferChunk(string Base64);
