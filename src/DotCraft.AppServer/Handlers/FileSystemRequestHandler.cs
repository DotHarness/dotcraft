using DotCraft.Configuration;
using DotCraft.Security;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed class FileSystemRequestHandler(
    IAppConfigMonitor? appConfigMonitor,
    AppServerConnection connection,
    IAppServerTransport transport) : IAppServerDomainHandler
{
    private const long MaxReadBytes = 8L * 1024 * 1024;

    private readonly FileSystemWatches _watches = new(connection, transport);

    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Contract.AppServerRpc.FsReadFile, HandleReadFileAsync);
        table.Map(Contract.AppServerRpc.FsWriteFile, HandleWriteFileAsync);
        table.Map(Contract.AppServerRpc.FsCreateDirectory, HandleCreateDirectoryAsync);
        table.Map(Contract.AppServerRpc.FsGetMetadata, HandleGetMetadataAsync);
        table.Map(Contract.AppServerRpc.FsReadDirectory, HandleReadDirectoryAsync);
        table.Map(Contract.AppServerRpc.FsRemove, HandleRemoveAsync);
        table.Map(Contract.AppServerRpc.FsCopy, HandleCopyAsync);
        table.Map(Contract.AppServerRpc.FsWatch, HandleWatchAsync);
        table.Map(Contract.AppServerRpc.FsUnwatch, HandleUnwatchAsync);
    }

    private async Task<AppServerTypedResult<Contract.FsReadFileResult>> HandleReadFileAsync(
        AppServerTypedRequest<Contract.FsReadFileParams> request,
        CancellationToken ct)
    {
        var path = ResolvePath(request.Params.Path).Resolved;
        if (Directory.Exists(path))
            throw AppServerErrors.FileSystem("NotAFile", "The path is a directory, not a file.", path);
        var file = new FileInfo(path);
        if (!file.Exists)
            throw FileNotFound(path);
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
        var path = ResolvePath(request.Params.Path).Resolved;
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
        var path = ResolvePath(request.Params.Path).Resolved;
        if (File.Exists(path))
            throw NotADirectory(path);
        var recursive = !request.Params.Recursive.IsSet || request.Params.Recursive.Value;
        if (!recursive && !Directory.Exists(path))
            RequireParentDirectory(path);

        Directory.CreateDirectory(path);
        return Empty();
    }

    private Task<AppServerTypedResult<Contract.FsGetMetadataResult>> HandleGetMetadataAsync(
        AppServerTypedRequest<Contract.FsGetMetadataParams> request,
        CancellationToken ct)
    {
        _ = ct;
        var (given, path) = ResolvePath(request.Params.Path);
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (!info.Exists)
            throw FileNotFound(path);

        return Task.FromResult(AppServerTypedResult<Contract.FsGetMetadataResult>.FromResult(new()
        {
            IsDirectory = info is DirectoryInfo,
            IsFile = info is FileInfo,
            IsSymlink = LinkAt(given) is not null,
            CreatedAtMs = UnixMilliseconds(info.CreationTimeUtc),
            ModifiedAtMs = UnixMilliseconds(info.LastWriteTimeUtc)
        }));
    }

    private Task<AppServerTypedResult<Contract.FsReadDirectoryResult>> HandleReadDirectoryAsync(
        AppServerTypedRequest<Contract.FsReadDirectoryParams> request,
        CancellationToken ct)
    {
        _ = ct;
        var path = ResolvePath(request.Params.Path).Resolved;
        if (File.Exists(path))
            throw NotADirectory(path);
        if (!Directory.Exists(path))
            throw FileNotFound(path);

        var entries = Directory.EnumerateFileSystemEntries(path)
            .Order(StringComparer.Ordinal)
            .Select(entry => new Contract.FsReadDirectoryEntry
            {
                FileName = Path.GetFileName(entry),
                IsDirectory = Directory.Exists(entry),
                IsFile = File.Exists(entry)
            })
            .ToArray();
        return Task.FromResult(AppServerTypedResult<Contract.FsReadDirectoryResult>.FromResult(new() { Entries = entries }));
    }

    private Task<AppServerTypedResult<Protocol.RpcEmpty>> HandleRemoveAsync(
        AppServerTypedRequest<Contract.FsRemoveParams> request,
        CancellationToken ct)
    {
        _ = ct;
        var (given, path) = ResolvePath(request.Params.Path);
        var recursive = !request.Params.Recursive.IsSet || request.Params.Recursive.Value;
        var force = !request.Params.Force.IsSet || request.Params.Force.Value;
        if (LinkAt(given) is { } link)
        {
            if (link.Attributes.HasFlag(FileAttributes.Directory))
                Directory.Delete(given);
            else
                File.Delete(given);
        }
        else if (Directory.Exists(path))
        {
            if (!recursive && Directory.EnumerateFileSystemEntries(path).Any())
                throw AppServerErrors.FileSystem("DirectoryNotEmpty", "The directory is not empty.", path);
            Directory.Delete(path, recursive);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
        else if (!force)
        {
            throw FileNotFound(path);
        }

        return Empty();
    }

    private Task<AppServerTypedResult<Protocol.RpcEmpty>> HandleCopyAsync(
        AppServerTypedRequest<Contract.FsCopyParams> request,
        CancellationToken ct)
    {
        var source = ResolvePath(request.Params.SourcePath, "sourcePath").Resolved;
        var destination = ResolvePath(request.Params.DestinationPath, "destinationPath").Resolved;
        if (Directory.Exists(source))
        {
            if (!request.Params.Recursive.IsSet || !request.Params.Recursive.Value)
                throw AppServerErrors.InvalidParams("'recursive' must be true to copy a directory.");
            if (IsSameOrDescendant(destination, source))
                throw AppServerErrors.InvalidParams("A directory cannot be copied into itself.");
            RequireParentDirectory(destination);
            CopyDirectory(source, destination, ct);
        }
        else if (File.Exists(source))
        {
            RequireParentDirectory(destination);
            File.Copy(source, destination, overwrite: true);
        }
        else
        {
            throw FileNotFound(source);
        }

        return Empty();
    }

    private Task<AppServerTypedResult<Contract.FsWatchResult>> HandleWatchAsync(
        AppServerTypedRequest<Contract.FsWatchParams> request,
        CancellationToken ct)
    {
        _ = ct;
        if (string.IsNullOrWhiteSpace(request.Params.WatchId))
            throw AppServerErrors.InvalidParams("'watchId' is required.");
        var path = ResolvePath(request.Params.Path).Resolved;
        if (!Directory.Exists(path) && !Directory.Exists(Path.GetDirectoryName(path)))
            throw FileNotFound(path);

        _watches.Add(request.Params.WatchId, path);
        return Task.FromResult(AppServerTypedResult<Contract.FsWatchResult>.FromResult(new() { Path = path }));
    }

    private async Task<AppServerTypedResult<Protocol.RpcEmpty>> HandleUnwatchAsync(
        AppServerTypedRequest<Contract.FsUnwatchParams> request,
        CancellationToken ct)
    {
        _ = ct;
        await _watches.RemoveAsync(request.Params.WatchId);
        return AppServerTypedResult<Protocol.RpcEmpty>.FromResult(new());
    }

    private (string Given, string Resolved) ResolvePath(string path, string name = "path")
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw AppServerErrors.InvalidParams($"'{name}' must be an absolute path.");

        var fullPath = Path.GetFullPath(path);
        var resolvedPath = WorkspaceBoundary.ResolveSymbolicLink(fullPath);
        var blacklist = new PathBlacklist(appConfigMonitor?.Current.Security.BlacklistedPaths ?? []);
        if (blacklist.IsBlacklisted(fullPath) || blacklist.IsBlacklisted(resolvedPath))
            throw AppServerErrors.FileSystem("PathBlocked", "Access to the path is blocked.", fullPath);
        return (fullPath, resolvedPath);
    }

    private static void RequireParentDirectory(string path)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
        if (parent is not null && !Directory.Exists(parent))
            throw AppServerErrors.FileSystem("DirectoryNotFound", "The parent directory does not exist.", path);
    }

    private static void CopyDirectory(string source, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if (!Directory.Exists(entry))
                File.Copy(entry, target, overwrite: true);
            else if (LinkAt(entry) is null)
                CopyDirectory(entry, target, ct);
        }
    }

    private static FileSystemInfo? LinkAt(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        return info.LinkTarget is null ? null : info;
    }

    private static bool IsSameOrDescendant(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return relative == "."
               || (relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative));
    }

    private static long UnixMilliseconds(DateTime utc) =>
        Math.Max(0, new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeMilliseconds());

    private static AppServerException FileNotFound(string path) =>
        AppServerErrors.FileSystem("FileNotFound", "Nothing exists at the path.", path);

    private static AppServerException NotADirectory(string path) =>
        AppServerErrors.FileSystem("NotADirectory", "The path is a file, not a directory.", path);

    private static Task<AppServerTypedResult<Protocol.RpcEmpty>> Empty() =>
        Task.FromResult(AppServerTypedResult<Protocol.RpcEmpty>.FromResult(new()));
}
