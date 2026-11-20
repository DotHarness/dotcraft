using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace DotCraft.Tools;

public sealed class ThreadFileVersions
{
    private readonly ConcurrentDictionary<string, string> _fingerprints = new(StringComparer.Ordinal);

    public void Record(string fullPath, ReadOnlySpan<byte> bytes) =>
        _fingerprints[PathAsyncMutex.Canonicalize(fullPath)] = Hash(bytes);

    public async Task RecordAsync(string fullPath, Stream stream, CancellationToken cancellationToken = default)
    {
        stream.Position = 0;
        _fingerprints[PathAsyncMutex.Canonicalize(fullPath)] = Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken));
    }

    public void Forget(string fullPath) => _fingerprints.TryRemove(PathAsyncMutex.Canonicalize(fullPath), out _);

    public bool ChangedSinceRecorded(string fullPath, ReadOnlySpan<byte> currentBytes, bool exists) =>
        _fingerprints.TryGetValue(PathAsyncMutex.Canonicalize(fullPath), out var recorded)
        && (!exists || Hash(currentBytes) != recorded);

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
