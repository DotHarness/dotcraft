using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace DotCraft.Tools;

public sealed class ThreadFileVersions
{
    private readonly ConcurrentDictionary<string, string> _fingerprints = new(StringComparer.Ordinal);

    public void Record(string fullPath)
    {
        if (Fingerprint(fullPath) is { } fingerprint)
            _fingerprints[PathAsyncMutex.Canonicalize(fullPath)] = fingerprint;
    }

    public void Forget(string fullPath) => _fingerprints.TryRemove(PathAsyncMutex.Canonicalize(fullPath), out _);

    public bool ChangedSinceRecorded(string fullPath) =>
        _fingerprints.TryGetValue(PathAsyncMutex.Canonicalize(fullPath), out var recorded)
        && Fingerprint(fullPath) != recorded;

    private static string? Fingerprint(string fullPath)
    {
        if (!File.Exists(fullPath))
            return null;
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
