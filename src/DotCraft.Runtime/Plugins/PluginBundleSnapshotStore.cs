using DotCraft.Plugins;

namespace DotCraft.Runtime;

/// <summary>Turns installed plugin bytes into the immutable snapshots the runtime loads from.</summary>
internal sealed class PluginBundleSnapshotStore : IDisposable
{
    private const string OwnerFileName = ".owner";
    private readonly string _runtimeRoot;
    private readonly string _acceptedRoot;
    private readonly string _generationsRoot;
    private FileStream? _ownerLease;

    public PluginBundleSnapshotStore(string runtimeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRoot);
        _runtimeRoot = Path.GetFullPath(runtimeRoot);
        _acceptedRoot = Path.Combine(_runtimeRoot, "accepted");
        _generationsRoot = Path.Combine(_runtimeRoot, "generations");

        var parent = Path.GetDirectoryName(_runtimeRoot)
                     ?? throw new InvalidOperationException("The plugin runtime root needs a parent directory.");
        Directory.CreateDirectory(parent);
        using var cleanupLease = AcquireCleanupLease(Path.Combine(parent, ".cleanup.lock"));
        Directory.CreateDirectory(_runtimeRoot);
        if ((File.GetAttributes(_runtimeRoot) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The plugin runtime root cannot be a filesystem link.");
        _ownerLease = new FileStream(
            Path.Combine(_runtimeRoot, OwnerFileName),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.Read);
        DeleteStaleRoots(parent);
    }

    public PluginAcceptedSnapshot Accept(DiscoveredPlugin plugin)
    {
        var destination = Path.Combine(
            _acceptedRoot,
            Sanitize(plugin.Manifest.Id),
            Guid.NewGuid().ToString("N"));
        try
        {
            var sourceFingerprint = PluginBundleTree.CopyAndFingerprint(
                plugin.Manifest.RootPath,
                destination);
            var copiedFingerprint = PluginBundleFingerprint.Compute(destination);
            if (!string.Equals(sourceFingerprint, copiedFingerprint, StringComparison.Ordinal)
                || !string.Equals(
                    sourceFingerprint,
                    PluginBundleFingerprint.Compute(plugin.Manifest.RootPath),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Plugin '{plugin.Manifest.Id}' changed while its accepted snapshot was being prepared.");
            }

            var parsed = PluginManifestParser.Load(destination);
            if (parsed.Manifest?.Dotnet == null)
            {
                throw new InvalidOperationException(
                    $"Copied plugin '{plugin.Manifest.Id}' no longer has an admitted .NET plugin manifest.");
            }
            if (!PluginIds.EqualsCanonical(parsed.Manifest.Id, plugin.Manifest.Id))
            {
                throw new InvalidOperationException(
                    $"Copied plugin '{plugin.Manifest.Id}' no longer has the discovered plugin identity.");
            }

            return new PluginAcceptedSnapshot(
                parsed.Manifest,
                destination,
                copiedFingerprint,
                parsed.Diagnostics);
        }
        catch
        {
            TryDeleteDirectory(destination);
            throw;
        }
    }

    public string CreateGenerationCopy(PluginAcceptedSnapshot snapshot, string generationId)
    {
        var destination = Path.Combine(
            _generationsRoot,
            Sanitize(snapshot.Manifest.Id),
            generationId);
        try
        {
            PluginBundleTree.CopyAndFingerprint(snapshot.ContentRoot, destination);
            var copiedFingerprint = PluginBundleFingerprint.Compute(destination);
            if (!string.Equals(copiedFingerprint, snapshot.Fingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException("Generation shadow copy fingerprint mismatch.");

            return destination;
        }
        catch
        {
            TryDeleteDirectory(destination);
            throw;
        }
    }

    /// <summary>Removes one generation shadow copy.</summary>
    /// <returns><see langword="false"/> when the platform is still holding the copy.</returns>
    public bool DeleteGeneration(string path) =>
        IsDescendant(_generationsRoot, path) && TryDeleteDirectory(path);

    public void DeleteAccepted(string path)
    {
        if (IsDescendant(_acceptedRoot, path))
            TryDeleteDirectory(path);
    }

    public void DeleteAll()
    {
        ReleaseOwnerLease();
        TryDeleteDirectory(_runtimeRoot);
    }

    public void Dispose() => ReleaseOwnerLease();

    private static string Sanitize(string pluginId) =>
        string.Concat(pluginId.Select(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'
                ? character
                : '_'));

    /// <summary>Removes a copied bundle, retrying briefly while the platform releases the mapped assemblies.</summary>
    private static bool TryDeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                    return true;
                Directory.Delete(path, recursive: true);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(20);
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsDescendant(string root, string path)
    {
        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var candidate = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return candidate.StartsWith(rootPath + Path.DirectorySeparatorChar, comparison);
    }

    private void DeleteStaleRoots(string parent)
    {
        foreach (var candidate in Directory.EnumerateDirectories(parent))
        {
            try
            {
                if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
                    continue;
                if (string.Equals(
                        Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate)),
                        Path.TrimEndingDirectorySeparator(_runtimeRoot),
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    continue;
                }

                using (new FileStream(
                           Path.Combine(candidate, OwnerFileName),
                           FileMode.OpenOrCreate,
                           FileAccess.ReadWrite,
                           FileShare.None))
                {
                }
                TryDeleteDirectory(candidate);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Another process still owns the root, or the directory disappeared while being scanned.
            }
        }
    }

    private static FileStream AcquireCleanupLease(string path)
    {
        for (var attempt = 0; attempt < 250; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 249)
            {
                Thread.Sleep(20);
            }
        }

        throw new IOException("The plugin runtime cleanup lease is unavailable.");
    }

    private void ReleaseOwnerLease() => Interlocked.Exchange(ref _ownerLease, null)?.Dispose();
}
