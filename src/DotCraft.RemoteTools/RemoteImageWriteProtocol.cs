namespace DotCraft.RemoteTools;

internal sealed record RemoteImageWriteRequest(string LeaseId, string WorkspaceId, string ThreadId, string CallId, string ImageBase64)
{
    public const string Method = "dotcraft/remoteToolHost/images/write";
}

internal sealed record RemoteImageWriteResponse(string SavedPath);
