namespace DotCraft.RemoteTools;

internal sealed record RemoteImageWriteRequest(string LeaseId, string WorkspaceId, string ThreadId, string CallId, string ImageBase64)
{
    public const string Method = "dotcraft/remoteToolHost/images/write";
}

internal sealed record RemoteImageWriteResponse(string SavedPath);

internal sealed record RemoteImageReadRequest(string LeaseId, string WorkspaceId, string CallId, string Path)
{
    public const string Method = "dotcraft/remoteToolHost/images/read";
}

internal sealed record RemoteImageReadResponse(string ImageBase64);
