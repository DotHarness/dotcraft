using System.Security.Cryptography;
using DotCraft.Security;
using DotCraft.Tools;

namespace DotCraft.RemoteTools;

/// <summary>Portable, bounded filesystem manifests for file transfer.</summary>
internal static class TransferFileTree
{
    internal const int MaxEntries = 10_000;

    internal static async Task<TransferFileManifest> DescribeAsync(string root, FileAccessGuard guard,
        long maxBytes, CancellationToken ct = default)
    {
        root = Path.GetFullPath(root);
        var entries = new List<TransferFileEntry>();
        var directory = Directory.Exists(root);
        var pending = new Stack<(string Path, string Relative)>();
        pending.Push((root, ""));
        long total = 0;
        while (pending.TryPop(out var item))
        {
            ct.ThrowIfCancellationRequested();
            await ValidateAsync(guard, item.Path, "read", ct).ConfigureAwait(false);
            if (entries.Count >= MaxEntries || item.Relative.Split('/').Length > 64)
                throw new IOException("Transfer contains too many entries or exceeds the directory depth limit.");
            if (Directory.Exists(item.Path))
            {
                entries.Add(new(item.Relative, true, 0, null));
                foreach (var child in Directory.EnumerateFileSystemEntries(item.Path).Order(StringComparer.Ordinal))
                    pending.Push((child, string.IsNullOrEmpty(item.Relative) ? Path.GetFileName(child)
                        : item.Relative + "/" + Path.GetFileName(child)));
            }
            else
            {
                await using var stream = OpenRead(item.Path);
                var size = stream.Length;
                if (size > maxBytes - total) throw new IOException("Transfer exceeds the configured byte limit.");
                total += size;
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false)).ToLowerInvariant();
                if (stream.Length != size) throw new IOException("Source changed while computing its manifest.");
                entries.Add(new(item.Relative, false, size, hash,
                    OperatingSystem.IsWindows() ? null : (int)File.GetUnixFileMode(item.Path) & 0x1FF));
            }
        }
        return new(directory, entries.OrderBy(e => e.Path, StringComparer.Ordinal).ToArray());
    }

    internal static string ResolveEntry(string root, string relative)
    {
        if (relative.Length > 2048) throw new IOException("Transfer path is too long.");
        if (relative.Length == 0) return Path.GetFullPath(root);
        var parts = relative.Split('/');
        if (parts.Length > 64 || parts.Any(p => string.IsNullOrEmpty(p) || p is "." or ".."
            || p.IndexOfAny(['\\', ':', '\0']) >= 0 || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new IOException("Invalid transfer path.");
        if (OperatingSystem.IsWindows() && parts.Any(p => p.EndsWith(' ') || p.EndsWith('.')
            || IsWindowsDevice(p.Split('.')[0]))) throw new IOException("Unsupported Windows transfer name.");
        return Path.Combine([Path.GetFullPath(root), .. parts]);
    }

    private static bool IsWindowsDevice(string name) => name.ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL"
        || (name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && name[3] is >= '0' and <= '9');

    internal static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            var info = Directory.Exists(current) ? (FileSystemInfo)new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null || (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0))
                throw new IOException("File transfer does not follow filesystem links.");
        }
    }

    internal static async Task ValidateAsync(FileAccessGuard guard, string path, string operation, CancellationToken ct)
    {
        RejectLinks(path);
        var error = await guard.ValidatePathAsync(path, operation, path, ct).ConfigureAwait(false);
        if (error is not null) throw new RemoteToolHostException(RemoteToolErrorCodes.RemotePolicyDenied, error);
    }

    internal static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
        81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
}

internal sealed record TransferFileManifest(bool IsDirectory, IReadOnlyList<TransferFileEntry> Entries);

internal sealed record TransferFileEntry(string Path, bool IsDirectory, long Length, string? Sha256, int? UnixMode = null);
