using DotCraft.Configuration;
using DotCraft.Security;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed class FileSystemRequestHandler(IAppConfigMonitor? appConfigMonitor) : IAppServerDomainHandler
{
    private const long MaxReadBytes = 8L * 1024 * 1024;

    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Contract.AppServerRpc.FsReadFile, HandleReadFileAsync);
        table.Map(Contract.AppServerRpc.FsWriteFile, HandleWriteFileAsync);
        table.Map(Contract.AppServerRpc.FsCreateDirectory, HandleCreateDirectoryAsync);
    }

    private async Task<AppServerTypedResult<Contract.FsReadFileResult>> HandleReadFileAsync(
        AppServerTypedRequest<Contract.FsReadFileParams> request,
        CancellationToken ct)
    {
        var path = ResolvePath(request.Params.Path);
        if (Directory.Exists(path))
            throw AppServerErrors.FileSystem("NotAFile", "The path is a directory, not a file.", path);
        var file = new FileInfo(path);
        if (!file.Exists)
            throw AppServerErrors.FileSystem("FileNotFound", "The file does not exist.", path);
        if (file.Length > MaxReadBytes)
            throw AppServerErrors.FileSystem("FileTooLarge", "The file is larger than 8 MiB.", path);

        var bytes = await File.ReadAllBytesAsync(path, ct);
        return AppServerTypedResult<Contract.FsReadFileResult>.FromResult(
            new Contract.FsReadFileResult { DataBase64 = Convert.ToBase64String(bytes) });
    }

    private async Task<AppServerTypedResult<Protocol.RpcEmpty>> HandleWriteFileAsync(
        AppServerTypedRequest<Contract.FsWriteFileParams> request,
        CancellationToken ct)
    {
        var path = ResolvePath(request.Params.Path);
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(request.Params.DataBase64);
        }
        catch (FormatException)
        {
            throw AppServerErrors.InvalidParams("'dataBase64' is not valid base64.");
        }
        RequireParentDirectory(path);

        await File.WriteAllBytesAsync(path, bytes, ct);
        return AppServerTypedResult<Protocol.RpcEmpty>.FromResult(new());
    }

    private Task<AppServerTypedResult<Protocol.RpcEmpty>> HandleCreateDirectoryAsync(
        AppServerTypedRequest<Contract.FsCreateDirectoryParams> request,
        CancellationToken ct)
    {
        _ = ct;
        var path = ResolvePath(request.Params.Path);
        if (File.Exists(path))
            throw AppServerErrors.FileSystem("NotADirectory", "A file already exists at the path.", path);
        var recursive = !request.Params.Recursive.IsSet || request.Params.Recursive.Value;
        if (!recursive && !Directory.Exists(path))
            RequireParentDirectory(path);

        Directory.CreateDirectory(path);
        return Task.FromResult(AppServerTypedResult<Protocol.RpcEmpty>.FromResult(new()));
    }

    private string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw AppServerErrors.InvalidParams("'path' must be an absolute path.");

        var fullPath = Path.GetFullPath(path);
        var blacklist = new PathBlacklist(appConfigMonitor?.Current.Security.BlacklistedPaths ?? []);
        if (blacklist.IsBlacklisted(fullPath))
            throw AppServerErrors.FileSystem("PathBlocked", "Access to the path is blocked.", fullPath);
        return fullPath;
    }

    private static void RequireParentDirectory(string path)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
        if (parent is not null && !Directory.Exists(parent))
            throw AppServerErrors.FileSystem("DirectoryNotFound", "The parent directory does not exist.", path);
    }
}
