namespace DotCraft.Protocol.AppServer;

public static partial class AppServerRpc
{
    private static string[] FileSystemErrors =>
    [
        .. CommonErrors,
        "PathBlocked",
        "FileNotFound",
        "NotAFile",
        "FileTooLarge",
        "DirectoryNotFound",
        "NotADirectory",
        "DirectoryNotEmpty"
    ];

    public static readonly RpcRequest<FsReadFileParams, FsReadFileResult> FsReadFile =
        new("fs/readFile", RpcDirection.ClientToServer, "1", Spec, scope: "connection", capability: "fileSystem", errors: FileSystemErrors);

    public static readonly RpcRequest<FsWriteFileParams, RpcEmpty> FsWriteFile =
        new("fs/writeFile", RpcDirection.ClientToServer, "1", Spec, scope: "connection", capability: "fileSystem", errors: FileSystemErrors);

    public static readonly RpcRequest<FsCreateDirectoryParams, RpcEmpty> FsCreateDirectory =
        new("fs/createDirectory", RpcDirection.ClientToServer, "1", Spec, scope: "connection", capability: "fileSystem", errors: FileSystemErrors);

    public static readonly RpcRequest<FsGetMetadataParams, FsGetMetadataResult> FsGetMetadata =
        new("fs/getMetadata", RpcDirection.ClientToServer, "1", Spec, scope: "connection", capability: "fileSystem", errors: FileSystemErrors);

    public static readonly RpcRequest<FsReadDirectoryParams, FsReadDirectoryResult> FsReadDirectory =
        new("fs/readDirectory", RpcDirection.ClientToServer, "1", Spec, scope: "connection", capability: "fileSystem", errors: FileSystemErrors);

    public static readonly RpcRequest<FsRemoveParams, RpcEmpty> FsRemove =
        new("fs/remove", RpcDirection.ClientToServer, "1", Spec, scope: "connection", capability: "fileSystem", errors: FileSystemErrors);

    public static readonly RpcRequest<FsCopyParams, RpcEmpty> FsCopy =
        new("fs/copy", RpcDirection.ClientToServer, "1", Spec, scope: "connection", capability: "fileSystem", errors: FileSystemErrors);

    public static readonly RpcRequest<FsWatchParams, FsWatchResult> FsWatch =
        new("fs/watch", RpcDirection.ClientToServer, "1", Spec, scope: "connection", capability: "fileSystem", errors: FileSystemErrors);

    public static readonly RpcRequest<FsUnwatchParams, RpcEmpty> FsUnwatch =
        new("fs/unwatch", RpcDirection.ClientToServer, "1", Spec, scope: "connection", capability: "fileSystem", errors: CommonErrors);

    public static readonly RpcNotification<FsChangedNotification> FsChanged =
        new("fs/changed", RpcDirection.ServerToClient, "1", Spec, scope: "connection", capability: "fileSystem", notificationOptOut: true);
}
