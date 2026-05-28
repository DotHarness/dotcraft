using System.Collections.Concurrent;
using DotCraft.Configuration;
using OpenSandbox;
using OpenSandbox.Config;
using OpenSandbox.Models;
using Spectre.Console;

namespace DotCraft.Tools.Sandbox;

/// <summary>
/// Manages OpenSandbox instances per agent session.
/// Each session gets its own isolated sandbox container.
/// Handles creation, reuse, idle cleanup, and workspace synchronization.
/// </summary>
public sealed class SandboxSessionManager : IAsyncDisposable
{
    private readonly AppConfig.SandboxConfig _config;
    private readonly string _workspacePath;
    private readonly ConcurrentDictionary<string, SandboxEntry> _sandboxes = new();
    private readonly SemaphoreSlim _createLock = new(1, 1);
    private readonly Timer? _cleanupTimer;

    /// <summary>
    /// Default session key used when no session context is available.
    /// </summary>
    public const string DefaultSessionKey = "__default__";

    public SandboxSessionManager(AppConfig.SandboxConfig config, string workspacePath)
    {
        _config = config;
        _workspacePath = Path.GetFullPath(workspacePath);

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
    public async Task<OpenSandbox.Sandbox> GetOrCreateAsync(string? sessionKey = null)
    {
        sessionKey ??= DefaultSessionKey;

        // Try to reuse existing sandbox
        if (_sandboxes.TryGetValue(sessionKey, out var entry) && !entry.IsDisposed)
        {
            entry.LastUsed = DateTime.UtcNow;
            return entry.Sandbox;
        }

        // Create new sandbox
        await _createLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_sandboxes.TryGetValue(sessionKey, out entry) && !entry.IsDisposed)
            {
                entry.LastUsed = DateTime.UtcNow;
                return entry.Sandbox;
            }

            var sandbox = await CreateSandboxAsync();
            
            // Sync workspace files to sandbox
            if (_config.SyncWorkspace)
            {
                await SyncWorkspaceToSandboxAsync(sandbox);
            }

            var newEntry = new SandboxEntry(sandbox);
            _sandboxes[sessionKey] = newEntry;

            AnsiConsole.MarkupLine($"[grey][[Sandbox]][/] Created sandbox [yellow]{sandbox.Id}[/] for session [dim]{Markup.Escape(sessionKey)}[/]");
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
            AnsiConsole.MarkupLine($"[grey][[Sandbox]][/] Released sandbox for session [dim]{Markup.Escape(sessionKey)}[/]");
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

    private async Task<OpenSandbox.Sandbox> CreateSandboxAsync()
    {
        var connectionConfig = new ConnectionConfig(new ConnectionConfigOptions
        {
            Domain = _config.Domain,
            ApiKey = string.IsNullOrWhiteSpace(_config.ApiKey) ? null : _config.ApiKey,
            Protocol = _config.UseHttps ? ConnectionProtocol.Https : ConnectionProtocol.Http,
            RequestTimeoutSeconds = 30,
        });

        var createOptions = new SandboxCreateOptions
        {
            ConnectionConfig = connectionConfig,
            Image = _config.Image,
            TimeoutSeconds = _config.TimeoutSeconds,
            Resource = new Dictionary<string, string>
            {
                ["cpu"] = _config.Cpu,
                ["memory"] = _config.Memory
            },
        };

        // Apply network policy
        var networkPolicy = BuildNetworkPolicy();
        if (networkPolicy != null)
        {
            createOptions.NetworkPolicy = networkPolicy;
        }

        var sandbox = await OpenSandbox.Sandbox.CreateAsync(createOptions);

        // Create a timestamp marker for tracking file modifications
        await sandbox.Commands.RunAsync("touch /tmp/.sandbox_created");

        return sandbox;
    }

    private NetworkPolicy? BuildNetworkPolicy()
    {
        return _config.NetworkPolicy.ToLowerInvariant() switch
        {
            "deny" => new NetworkPolicy
            {
                DefaultAction = NetworkRuleAction.Deny
            },
            "allow" => null, // No policy = allow all
            "custom" when _config.AllowedEgressDomains.Count > 0 => new NetworkPolicy
            {
                DefaultAction = NetworkRuleAction.Deny,
                Egress = _config.AllowedEgressDomains
                    .Select(domain => new NetworkRule
                    {
                        Action = NetworkRuleAction.Allow,
                        Target = domain
                    })
                    .ToList()
            },
            _ => null
        };
    }

    private async Task SyncWorkspaceToSandboxAsync(OpenSandbox.Sandbox sandbox)
    {
        try
        {
            // Create workspace directory in sandbox
            await sandbox.Files.CreateDirectoriesAsync([
                new CreateDirectoryEntry { Path = "/workspace", Mode = 755 }
            ]);

            // Use tar to efficiently transfer workspace contents
            // This is more efficient than transferring files one by one
            AnsiConsole.MarkupLine("[grey][[Sandbox]][/] Syncing workspace to sandbox...");

            // Get list of files (respect .gitignore-like patterns, skip large dirs)
            var files = EnumerateWorkspaceFiles(_workspacePath, _config.SyncExclude).Take(500).ToList();
            
            foreach (var batch in Chunk(files, 20))
            {
                var writeEntries = new List<WriteEntry>();
                foreach (var filePath in batch)
                {
                    try
                    {
                        var relativePath = Path.GetRelativePath(_workspacePath, filePath);
                        var sandboxPath = "/workspace/" + relativePath.Replace('\\', '/');
                        var content = await File.ReadAllTextAsync(filePath);
                        
                        writeEntries.Add(new WriteEntry
                        {
                            Path = sandboxPath,
                            Data = content,
                            Mode = 644
                        });
                    }
                    catch
                    {
                        // Skip files that can't be read (binary, permissions, etc.)
                    }
                }

                if (writeEntries.Count > 0)
                {
                    await sandbox.Files.WriteFilesAsync(writeEntries);
                }
            }

            AnsiConsole.MarkupLine($"[grey][[Sandbox]][/] Synced [yellow]{files.Count}[/] files to sandbox");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[grey][[Sandbox]][/] [red]Workspace sync failed: {Markup.Escape(ex.Message)}[/]");
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

    private async Task CleanupIdleSandboxesAsync()
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
                AnsiConsole.MarkupLine($"[grey][[Sandbox]][/] [yellow]Idle cleanup[/]: removed sandbox for [dim]{Markup.Escape(key)}[/]");
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

    private sealed class SandboxEntry(OpenSandbox.Sandbox sandbox)
    {
        public OpenSandbox.Sandbox Sandbox { get; } = sandbox;
        public DateTime LastUsed { get; set; } = DateTime.UtcNow;
        public bool IsDisposed { get; set; }
    }
}
