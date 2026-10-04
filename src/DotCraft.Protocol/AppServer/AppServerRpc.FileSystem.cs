namespace DotCraft.Protocol.AppServer;

public static partial class AppServerRpc
{
    private static readonly string[] FileSystemErrors =
    [
        .. CommonErrors,
        "PathBlocked",
        "FileNotFound",
        "NotAFile",
        "FileTooLarge",
        "DirectoryNotFound",
        "NotADirectory"
    ];

    public static readonly RpcRequest<FsReadFileParams, FsReadFileResult> FsReadFile =
        new("fs/readFile", RpcDirection.ClientToServer, "1", Spec, scope: "connection", capability: "fileSystem", errors: FileSystemErrors);

    public static readonly RpcRequest<FsWriteFileParams, RpcEmpty> FsWriteFile =
        new("fs/writeFile", RpcDirection.ClientToServer, "1", Spec, scope: "connection", capability: "fileSystem", errors: FileSystemErrors);

    public static readonly RpcRequest<FsCreateDirectoryParams, RpcEmpty> FsCreateDirectory =
        new("fs/createDirectory", RpcDirection.ClientToServer, "1", Spec, scope: "connection", capability: "fileSystem", errors: FileSystemErrors);
}
