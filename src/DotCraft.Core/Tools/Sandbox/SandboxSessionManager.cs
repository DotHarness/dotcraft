using System.Collections.Concurrent;
using DotCraft.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.Tools.Sandbox;

/// <summary>
/// Manages sandbox instances per agent session.
/// Each session gets its own isolated sandbox container.
/// Handles creation, reuse, idle cleanup, and workspace synchronization.
/// </summary>
public sealed class SandboxSessionManager : IAsyncDisposable
{
    private readonly AppConfig.SandboxConfig _config;
    private readonly ISandboxProvider _provider;
    private readonly string _workspacePath;
    private readonly IReadOnlyList<string> _workspaceRoots;
    private readonly ConcurrentDictionary<string, SandboxEntry> _sandboxes = new();
    private readonly SemaphoreSlim _createLock = new(1, 1);
    private readonly Timer? _cleanupTimer;
    private readonly ILogger<SandboxSessionManager> _logger;

    /// <summary>
    /// Default session key used when no session context is available.
    /// </summary>
    public const string DefaultSessionKey = "__default__";

    public SandboxSessionManager(
        AppConfig.SandboxConfig config,
        ISandboxProvider provider,
        string workspacePath,
        IReadOnlyList<string>? workspaceRoots = null,
        ILogger<SandboxSessionManager>? logger = null)
    {
        _config = config;
        _provider = provider;
        _workspacePath = Path.GetFullPath(workspacePath);
        _workspaceRoots = (workspaceRoots ?? [_workspacePath])
            .Select(Path.GetFullPath)
            .ToArray();
        _logger = logger ?? NullLogger<SandboxSessionManager>.Instance;

        // Start idle cleanup timer (check every 60 seconds)
        if (config.IdleTimeoutSeconds > 0)
        {
            _cleanupTimer = new Timer(
                _ => _ = CleanupIdleSandboxesAsync(),
                null,
                TimeSpan.FromSeconds(60),
                TimeSpan.FromSeconds(60));
        }
    }

    /// <summary>
    /// Gets or creates a sandbox for the given session key.
    /// If a sandbox already exists and is healthy, it is reused.
    /// </summary>
    public async Task<ISandboxInstance> GetOrCreateAsync(
        string? sessionKey = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sessionKey ??= DefaultSessionKey;

        // Try to reuse existing sandbox
        if (_sandboxes.TryGetValue(sessionKey, out var entry) && !entry.IsDisposed)
        {
            entry.LastUsed = DateTime.UtcNow;
            return entry.Sandbox;
        }

        // Create new sandbox
        await _createLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_sandboxes.TryGetValue(sessionKey, out entry) && !entry.IsDisposed)
            {
                entry.LastUsed = DateTime.UtcNow;
                return entry.Sandbox;
            }

            var sandbox = await CreateSandboxAsync(cancellationToken);

            // Sync workspace files to sandbox
            if (_config.SyncWorkspace)
            {
                await SyncWorkspaceToSandboxAsync(sandbox, cancellationToken);
            }

            var newEntry = new SandboxEntry(sandbox);
            _sandboxes[sessionKey] = newEntry;

            _logger.LogInformation(
                "Created sandbox {SandboxId} for session {SessionKey}",
                sandbox.Id,
                sessionKey);
            return sandbox;
        }
        finally
        {
            _createLock.Release();
        }
    }

    /// <summary>
    /// Releases the sandbox for a specific session.
    /// </summary>
    public async Task ReleaseAsync(string sessionKey)
    {
        if (_sandboxes.TryRemove(sessionKey, out var entry))
        {
            await SafeKillAsync(entry);
            _logger.LogInformation("Released sandbox for session {SessionKey}", sessionKey);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cleanupTimer != null)
            await _cleanupTimer.DisposeAsync();

        var tasks = _sandboxes.Values.Select(SafeKillAsync);
        await Task.WhenAll(tasks);
        _sandboxes.Clear();
        _createLock.Dispose();
    }

    private async Task<ISandboxInstance> CreateSandboxAsync(CancellationToken cancellationToken)
    {
        var sandbox = await _provider.CreateAsync(cancellationToken).ConfigureAwait(false);

        // Create a timestamp marker for tracking file modifications
        await sandbox.RunCommandAsync("touch /tmp/.sandbox_created", cancellationToken: cancellationToken);

        return sandbox;
    }

    private async Task SyncWorkspaceToSandboxAsync(
        ISandboxInstance sandbox,
        CancellationToken cancellationToken)
    {
        try
        {
            // Create workspace directory in sandbox
            await sandbox.CreateDirectoriesAsync([
                new SandboxDirectoryEntry("/workspace", 755),
                new SandboxDirectoryEntry("/workspace-roots", 755)
            ], cancellationToken);

            // Use tar to efficiently transfer workspace contents
            // This is more efficient than transferring files one by one
            _logger.LogDebug("Syncing workspace to sandbox");

            var syncedCount = 0;
            for (var rootIndex = 0; rootIndex < _workspaceRoots.Count; rootIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var root = _workspaceRoots[rootIndex];
                var sandboxRoot = string.Equals(root, _workspacePath, StringComparison.OrdinalIgnoreCase)
                    ? "/workspace"
                    : $"/workspace-roots/{rootIndex}";
                await sandbox.CreateDirectoriesAsync([
                    new SandboxDirectoryEntry(sandboxRoot, 755)
                ], cancellationToken);

                var files = EnumerateWorkspaceFiles(root, _config.SyncExclude)
                    .Take(500 - syncedCount)
                    .ToList();
                foreach (var batch in Chunk(files, 20))
                {
                    var writeEntries = new List<SandboxWriteEntry>();
                    foreach (var filePath in batch)
                    {
                        try
                        {
                            var relativePath = Path.GetRelativePath(root, filePath);
                            var sandboxPath = sandboxRoot + "/" + relativePath.Replace('\\', '/');
                            var content = await File.ReadAllTextAsync(filePath, cancellationToken);

                            writeEntries.Add(new SandboxWriteEntry(sandboxPath, content, 644));
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch
                        {
                            // Skip files that can't be read (binary, permissions, etc.)
                        }
                    }

                    if (writeEntries.Count > 0)
                    {
                        await sandbox.WriteFilesAsync(writeEntries, cancellationToken);
                    }
                }

                syncedCount += files.Count;
                if (syncedCount >= 500)
                    break;
            }

            _logger.LogInformation("Synced {FileCount} workspace files to sandbox", syncedCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workspace sync to sandbox failed");
        }
    }

    private static IEnumerable<string> EnumerateWorkspaceFiles(string root, IReadOnlyList<string> syncExclude)
    {
        var skipDirNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "node_modules", "bin", "obj", ".vs", ".idea",
            "packages", "TestResults", "__pycache__", ".venv"
        };

        var binaryExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".so", ".dylib", ".pdb", ".png", ".jpg", ".jpeg",
            ".gif", ".ico", ".zip", ".tar", ".gz", ".7z", ".bin", ".dat",
            ".pdf", ".mp3", ".mp4", ".wasm", ".pyc", ".db", ".sqlite"
        };

        // Normalize exclude patterns to forward-slash relative paths.
        var excludePatterns = syncExclude
            .Select(p => p.Replace('\\', '/').Trim('/'))
            .Where(p => p.Length > 0)
            .ToList();

        var dirs = new Stack<string>();
        dirs.Push(root);

        while (dirs.Count > 0)
        {
            var dir = dirs.Pop();
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir); }
            catch { continue; }

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                if (binaryExts.Contains(ext)) continue;

                var info = new FileInfo(file);
                if (info.Length > 512 * 1024) continue; // Skip files > 512KB

                var relFile = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (IsExcluded(relFile, excludePatterns)) continue;

                yield return file;
            }

            try
            {
                foreach (var subDir in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(subDir);
                    if (skipDirNames.Contains(name)) continue;

                    var relDir = Path.GetRelativePath(root, subDir).Replace('\\', '/');
                    if (IsExcluded(relDir, excludePatterns)) continue;

                    dirs.Push(subDir);
                }
            }
            catch { /* ignored */ }
        }
    }

    /// <summary>
    /// Returns true if <paramref name="relativePath"/> is covered by any exclude pattern.
    /// A pattern covers a path when the path equals the pattern or starts with the pattern followed by '/'.
    /// </summary>
    private static bool IsExcluded(string relativePath, IReadOnlyList<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (relativePath.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
            if (relativePath.StartsWith(pattern + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    internal async Task CleanupIdleSandboxesAsync()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-_config.IdleTimeoutSeconds);
        var toRemove = _sandboxes
            .Where(kv => kv.Value.LastUsed < cutoff)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            if (_sandboxes.TryRemove(key, out var entry))
            {
                await SafeKillAsync(entry);
                _logger.LogInformation("Removed idle sandbox for session {SessionKey}", key);
            }
        }
    }

    private static async Task SafeKillAsync(SandboxEntry entry)
    {
        if (entry.IsDisposed) return;
        entry.IsDisposed = true;
        try
        {
            await entry.Sandbox.KillAsync();
        }
        catch { /* ignored */ }
        try
        {
            await entry.Sandbox.DisposeAsync();
        }
        catch { /* ignored */ }
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> source, int chunkSize)
    {
        for (var i = 0; i < source.Count; i += chunkSize)
        {
            yield return source.GetRange(i, Math.Min(chunkSize, source.Count - i));
        }
    }

    private sealed class SandboxEntry(ISandboxInstance sandbox)
    {
        public ISandboxInstance Sandbox { get; } = sandbox;
        public DateTime LastUsed { get; set; } = DateTime.UtcNow;
        public bool IsDisposed { get; set; }
    }
}
