using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;

namespace DotCraft.Runtime;

/// <summary>Computes the content identity of a plugin bundle without following filesystem links.</summary>
internal static class PluginBundleFingerprint
{
    public static string Compute(string root) => PluginBundleTree.Fingerprint(root);
}

/// <summary>The single bounded tree walker used by both bundle hashing and copying.</summary>
internal static class PluginBundleTree
{
    private const string MarkerFileName = ".builtin";
    private const int MaxEntries = 20_000;
    private const int MaxFiles = 10_000;
    private const int MaxDepth = 64;
    private const long MaxBytes = 512L * 1024 * 1024;

    public static string Fingerprint(string sourceRoot) =>
        Process(sourceRoot, destinationRoot: null);

    public static string CopyAndFingerprint(string sourceRoot, string destinationRoot) =>
        Process(sourceRoot, destinationRoot);

    private static string Process(string sourceRoot, string? destinationRoot)
    {
        var entries = Enumerate(sourceRoot);
        if (destinationRoot != null)
            Directory.CreateDirectory(destinationRoot);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("DotCraft.PluginBundleFingerprint\0v1\0"u8);
        var buffer = new byte[81920];
        long bytesRead = 0;
        foreach (var entry in entries)
        {
            var normalizedPath = entry.RelativePath.Replace('\\', '/');
            var pathBytes = Encoding.UTF8.GetBytes(normalizedPath);
            if (entry.IsDirectory)
            {
                AppendEntryHeader(hash, kind: 0, pathBytes.Length, contentLength: 0);
                hash.AppendData(pathBytes);
                if (destinationRoot != null)
                    Directory.CreateDirectory(Path.Combine(destinationRoot, entry.RelativePath));
                continue;
            }

            RejectLink(entry.SourcePath);
            var includeInHash = !string.Equals(normalizedPath, MarkerFileName, StringComparison.Ordinal);

            using var source = new FileStream(
                entry.SourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                buffer.Length,
                FileOptions.SequentialScan);
            var contentLength = source.Length;
            if (includeInHash)
            {
                AppendEntryHeader(hash, kind: 1, pathBytes.Length, contentLength);
                hash.AppendData(pathBytes);
            }
            using var destination = destinationRoot == null
                                    || !includeInHash
                ? null
                : CreateDestination(destinationRoot, entry.RelativePath, buffer.Length);
            long fileBytesRead = 0;
            int count;
            while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                bytesRead += count;
                fileBytesRead += count;
                if (bytesRead > MaxBytes)
                    throw new InvalidOperationException("Plugin bundle exceeds the 512 MiB content limit.");
                if (includeInHash)
                    hash.AppendData(buffer.AsSpan(0, count));
                destination?.Write(buffer, 0, count);
            }
            if (fileBytesRead != contentLength || source.Length != contentLength)
                throw new IOException("Plugin bundle changed while its content identity was being computed.");
            RejectLink(entry.SourcePath);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendEntryHeader(
        IncrementalHash hash,
        byte kind,
        int pathLength,
        long contentLength)
    {
        Span<byte> header = stackalloc byte[13];
        header[0] = kind;
        BinaryPrimitives.WriteInt32LittleEndian(header[1..5], pathLength);
        BinaryPrimitives.WriteInt64LittleEndian(header[5..13], contentLength);
        hash.AppendData(header);
    }

    private static IReadOnlyList<TreeEntry> Enumerate(string root)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("Plugin bundle root does not exist.");
        RejectLink(root);

        var rootPath = Path.GetFullPath(root);
        var entries = new List<TreeEntry>();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((rootPath, 0));
        var entryCount = 0;
        var fileCount = 0;
        while (pending.TryPop(out var current))
        {
            if (current.Depth >= MaxDepth)
                throw new InvalidOperationException("Plugin bundle exceeds the directory depth limit.");

            foreach (var path in Directory.EnumerateFileSystemEntries(current.Path))
            {
                RejectLink(path);
                if (++entryCount > MaxEntries)
                    throw new InvalidOperationException("Plugin bundle contains too many filesystem entries.");

                var attributes = File.GetAttributes(path);
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                var relative = Path.GetRelativePath(rootPath, path);
                entries.Add(new TreeEntry(path, relative, isDirectory));
                if (isDirectory)
                {
                    pending.Push((path, current.Depth + 1));
                }
                else if (++fileCount > MaxFiles)
                {
                    throw new InvalidOperationException("Plugin bundle contains too many files.");
                }
            }
        }

        return entries
            .OrderBy(static entry => entry.RelativePath.Replace('\\', '/'), StringComparer.Ordinal)
            .ToArray();
    }

    private static FileStream CreateDestination(string root, string relativePath, int bufferSize)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize,
            FileOptions.SequentialScan);
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Plugin bundles cannot contain filesystem links.");
    }

    private sealed record TreeEntry(string SourcePath, string RelativePath, bool IsDirectory);
}
