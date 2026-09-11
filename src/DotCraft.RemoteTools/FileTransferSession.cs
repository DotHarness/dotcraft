using System.Security.Cryptography;

namespace DotCraft.RemoteTools;

internal sealed class FileTransferSession(string root, TransferFileManifest manifest, bool write, bool overwrite)
    : IAsyncDisposable
{
    private PendingFile? _pending;
    private readonly HashSet<int> _completed = [];
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal CancellationTokenSource Stopping { get; } = new();
    internal string Root => root;
    internal bool Write => write;

    internal async Task PrepareAsync(long maxBytes, Func<string, string, CancellationToken, Task> authorize, CancellationToken ct)
    {
        if (manifest.Entries.Count is 0 or > TransferFileTree.MaxEntries)
            throw new IOException("Invalid transfer manifest size.");
        var names = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in manifest.Entries)
        {
            var path = TransferFileTree.ResolveEntry(root, entry.Path);
            if (!names.Add(path) || entry.Length < 0 || entry.Length > maxBytes - total
                || (entry.IsDirectory && (entry.Length != 0 || entry.Sha256 is not null))
                || (!entry.IsDirectory && (entry.Sha256 is not { Length: 64 } || !entry.Sha256.All(char.IsAsciiHexDigit))))
                throw new IOException("Invalid transfer manifest or byte limit exceeded.");
            total += entry.Length;
            TransferFileTree.RejectLinks(path);
            await authorize(path, write ? "write" : "read", ct).ConfigureAwait(false);
            if (write && ((entry.IsDirectory && File.Exists(path)) || (!entry.IsDirectory && Directory.Exists(path))
                || (!entry.IsDirectory && !overwrite && File.Exists(path))))
                throw new IOException($"Transfer destination already exists: {path}");
        }
        if (manifest.Entries[0].Path != "" || manifest.Entries[0].IsDirectory != manifest.IsDirectory
            || (!manifest.IsDirectory && manifest.Entries.Count != 1))
            throw new IOException("Transfer manifest must start with its root entry.");
    }

    internal async Task<byte[]> ReadAsync(int index, long offset, CancellationToken ct)
    {
        var entry = Entry(index, false);
        if (entry.IsDirectory || offset < 0 || offset > entry.Length) throw new IOException("Invalid file offset.");
        var path = TransferFileTree.ResolveEntry(root, entry.Path);
        TransferFileTree.RejectLinks(path);
        await using var file = TransferFileTree.OpenRead(path);
        if (file.Length != entry.Length) throw new IOException("Transfer source changed.");
        file.Position = offset;
        var bytes = new byte[(int)Math.Min(RemoteFileTransferProtocol.ChunkBytes, entry.Length - offset)];
        await file.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
        return bytes;
    }

    internal async Task WriteAsync(int index, long offset, byte[] bytes, CancellationToken ct)
    {
        var entry = Entry(index, true);
        if (entry.IsDirectory || _completed.Contains(index) || bytes.Length > RemoteFileTransferProtocol.ChunkBytes)
            throw new IOException("Invalid transfer write.");
        if (_pending is null)
        {
            if (offset != 0) throw new IOException("Transfer writes must be sequential.");
            var path = TransferFileTree.ResolveEntry(root, entry.Path);
            TransferFileTree.RejectLinks(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = Path.Combine(Path.GetDirectoryName(path)!, ".craft-transfer-" + Guid.NewGuid().ToString("N") + ".tmp");
            _pending = new PendingFile(index, temporary, new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous));
        }
        var pending = _pending;
        if (pending.Index != index || offset != pending.File.Position || bytes.LongLength > entry.Length - offset)
            throw new IOException("Invalid transfer write offset or length.");
        await pending.File.WriteAsync(bytes, ct).ConfigureAwait(false);
        pending.Hash.AppendData(bytes);
    }

    internal async Task CommitAsync(int index, Action<Action> commit, CancellationToken ct)
    {
        var entry = Entry(index, true);
        if (_completed.Contains(index)) throw new IOException("Transfer entry was already committed.");
        var destination = TransferFileTree.ResolveEntry(root, entry.Path);
        TransferFileTree.RejectLinks(destination);
        ct.ThrowIfCancellationRequested();
        if (entry.IsDirectory) commit(() => Directory.CreateDirectory(destination));
        else
        {
            if (_pending is not { } pending || pending.Index != index || pending.File.Position != entry.Length)
                throw new IOException("Transfer file is incomplete.");
            if (!Convert.ToHexString(pending.Hash.GetHashAndReset()).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Transfer checksum mismatch.");
            await pending.File.FlushAsync(ct).ConfigureAwait(false);
            await pending.File.DisposeAsync().ConfigureAwait(false);
            if (!OperatingSystem.IsWindows() && entry.UnixMode is { } mode)
                File.SetUnixFileMode(pending.Path, (UnixFileMode)(mode & 0x1FF));
            ct.ThrowIfCancellationRequested();
            TransferFileTree.RejectLinks(destination);
            commit(() => File.Move(pending.Path, destination, overwrite));
            pending.Hash.Dispose();
            _pending = null;
        }
        _completed.Add(index);
    }

    internal TransferFileEntry Entry(int index, bool expectedWrite)
    {
        if (expectedWrite != write || index < 0 || index >= manifest.Entries.Count)
            throw new IOException("Invalid transfer entry or direction.");
        Stopping.Token.ThrowIfCancellationRequested();
        return manifest.Entries[index];
    }

    public async ValueTask DisposeAsync()
    {
        await Stopping.CancelAsync().ConfigureAwait(false);
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_pending is { } file)
            {
                await file.File.DisposeAsync().ConfigureAwait(false);
                file.Hash.Dispose();
                try { File.Delete(file.Path); } catch (IOException) { }
            }
            _pending = null;
        }
        finally { Gate.Release(); }
    }

    private sealed class PendingFile(int index, string path, FileStream file)
    {
        internal int Index => index;
        internal string Path => path;
        internal FileStream File => file;
        internal IncrementalHash Hash { get; } = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    }
}
