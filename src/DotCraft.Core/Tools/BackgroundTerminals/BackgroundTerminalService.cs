using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Security.ShellCommands;
using Microsoft.Extensions.Logging;

namespace DotCraft.Tools.BackgroundTerminals;

/// <summary>
/// Pipe-based process manager for host shell commands that may outlive a single tool call.
/// </summary>
public sealed partial class BackgroundTerminalService : IBackgroundTerminalService, IAsyncDisposable
{
    private const string MetadataExtension = ".json";
    private const string OutputExtension = ".log";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _terminalRoot;
    private readonly AppConfig.ShellBackgroundConfig _config;
    private readonly ILogger<BackgroundTerminalService>? _logger;
    private readonly Timer _retentionTimer;
    private readonly ConcurrentDictionary<string, ActiveTerminal> _active = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, BackgroundTerminalMetadata> _metadata = new(StringComparer.Ordinal);
    private int _cleanupRunning;

    private enum ProcessWaitOutcome
    {
        Exited,
        TimedOut,
        CallerCancelled
    }

    public BackgroundTerminalService(
        string craftPath,
        AppConfig.ShellBackgroundConfig config,
        ILogger<BackgroundTerminalService>? logger = null)
    {
        _terminalRoot = Path.Combine(craftPath, "terminals");
        _config = config;
        _logger = logger;
        Directory.CreateDirectory(_terminalRoot);
        LoadMetadataAndMarkLost();
        CleanupExpiredArtifactsOnStartup();
        _retentionTimer = new Timer(
            static state => ((BackgroundTerminalService)state!).RunRetentionCleanup(),
            this,
            TimeSpan.FromHours(24),
            TimeSpan.FromHours(24));
    }

    public event Action<BackgroundTerminalEvent>? TerminalEvent;

    public async Task<BackgroundTerminalSnapshot> StartAsync(
        BackgroundTerminalStartRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (request.RunInBackground && !_config.Enabled)
            throw new InvalidOperationException("Background terminals are disabled by Tools.Shell.Background.Enabled.");
        if (string.IsNullOrWhiteSpace(request.Command))
            throw new ArgumentException("Command is required.", nameof(request));

        EnforceSessionLimits(request.ThreadId);

        var sessionId = "term_" + Guid.NewGuid().ToString("N")[..12];
        var sessionDir = GetThreadDirectory(request.ThreadId);
        Directory.CreateDirectory(sessionDir);
        var outputPath = Path.Combine(sessionDir, sessionId + OutputExtension);
        var metadataPath = Path.Combine(sessionDir, sessionId + MetadataExtension);

        var shell = request.Shell ?? ResolveHostDefaultShell();
        // Materialize the output artifact even when the command produces no bytes. This keeps
        // terminal ownership and archive/delete lifecycle observable for empty-output commands.
        Directory.CreateDirectory(sessionDir);
        using (File.Create(outputPath))
        {
        }

        var process = Process.Start(CreateStartInfo(request, shell))
            ?? throw new InvalidOperationException("Failed to start process.");

        var terminal = new ActiveTerminal(
            sessionId,
            metadataPath,
            outputPath,
            request,
            process,
            DateTimeOffset.UtcNow,
            request.StdinSession ?? new ShellStdinSession(shell, request.WorkingDirectory),
            this);

        _active[sessionId] = terminal;
        _metadata[sessionId] = terminal.ToMetadata(BackgroundTerminalStatus.Running);
        try
        {
            await PersistMetadataAsync(_metadata[sessionId], CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            process.Dispose();
            _active.TryRemove(sessionId, out _);
            _metadata.TryRemove(sessionId, out _);
            throw;
        }
        Raise("started", terminal.CreateSnapshot(maxOutputChars: request.MaxOutputChars), null);

        terminal.BeginReading();

        if (!OperatingSystem.IsWindows())
        {
            await process.StandardInput.WriteLineAsync(request.Command).ConfigureAwait(false);
            if (!request.Interactive)
                process.StandardInput.Close();
        }
        else if (!request.Interactive)
        {
            process.StandardInput.Close();
        }

        _ = WatchProcessAsync(terminal);

        var yieldMs = NormalizeYield(request.YieldTimeMs);
        if (request.RunInBackground)
        {
            var outcome = await WaitForExitOrDelayAsync(process, TimeSpan.FromMilliseconds(yieldMs), ct).ConfigureAwait(false);
            if (outcome != ProcessWaitOutcome.Exited)
                return terminal.CreateSnapshot(BackgroundTerminalStatus.Running, request.MaxOutputChars, "runInBackground");
        }
        else
        {
            var timeoutSeconds = Math.Max(1, request.TimeoutSeconds);
            var outcome = await WaitForExitOrDelayAsync(process, TimeSpan.FromSeconds(timeoutSeconds), ct).ConfigureAwait(false);
            if (outcome == ProcessWaitOutcome.TimedOut)
            {
                await KillAsync(terminal, BackgroundTerminalStatus.TimedOut, CancellationToken.None).ConfigureAwait(false);
                return terminal.CreateSnapshot(BackgroundTerminalStatus.TimedOut, request.MaxOutputChars);
            }
            if (outcome == ProcessWaitOutcome.CallerCancelled)
            {
                await KillAsync(terminal, BackgroundTerminalStatus.Killed, CancellationToken.None).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
            }
        }

        await terminal.WaitForCompletionMetadataAsync(request.RunInBackground ? CancellationToken.None : ct).ConfigureAwait(false);
        return terminal.CreateSnapshot(maxOutputChars: request.MaxOutputChars);
    }

    public async Task<BackgroundTerminalSnapshot> ReadAsync(
        string sessionId,
        int waitMs = 0,
        int? maxOutputChars = null,
        CancellationToken ct = default)
    {
        if (_active.TryGetValue(sessionId, out var active))
        {
            if (waitMs > 0 && !active.Process.HasExited)
            {
                var waitFor = TimeSpan.FromMilliseconds(Math.Min(waitMs, _config.MaxYieldTimeMs));
                var outcome = await WaitForExitOrDelayAsync(active.Process, waitFor, ct).ConfigureAwait(false);
                if (outcome == ProcessWaitOutcome.CallerCancelled)
                    ct.ThrowIfCancellationRequested();
            }
            if (active.Process.HasExited)
                await active.WaitForCompletionMetadataAsync(ct).ConfigureAwait(false);
            return active.CreateSnapshot(maxOutputChars: maxOutputChars ?? _config.DefaultReadMaxOutputChars);
        }

        var metadata = GetMetadata(sessionId);
        return await SnapshotFromMetadataAsync(metadata, maxOutputChars ?? _config.DefaultReadMaxOutputChars, ct)
            .ConfigureAwait(false);
    }

    public async Task<BackgroundTerminalSnapshot> WriteStdinAsync(
        string sessionId,
        string input,
        int yieldTimeMs = 1000,
        int? maxOutputChars = null,
        CancellationToken ct = default)
    {
        if (!_active.TryGetValue(sessionId, out var active))
            throw new KeyNotFoundException($"Background terminal '{sessionId}' is not running.");

        if (!string.IsNullOrEmpty(input))
        {
            await active.Process.StandardInput.WriteAsync(input).ConfigureAwait(false);
            await active.Process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
        }

        await Task.Delay(NormalizeYield(yieldTimeMs), ct).ConfigureAwait(false);
        return active.CreateSnapshot(maxOutputChars: maxOutputChars ?? _config.DefaultReadMaxOutputChars);
    }

    public ShellStdinSession? GetStdinSession(string sessionId) =>
        _active.TryGetValue(sessionId, out var active) ? active.StdinSession : null;

    public async Task<IReadOnlyList<BackgroundTerminalSnapshot>> ListAsync(
        string? threadId = null,
        CancellationToken ct = default)
    {
        var snapshots = new List<BackgroundTerminalSnapshot>();
        foreach (var metadata in _metadata.Values.OrderByDescending(m => m.StartedAt))
        {
            if (!string.IsNullOrWhiteSpace(threadId)
                && !string.Equals(metadata.ThreadId, threadId, StringComparison.Ordinal))
            {
                continue;
            }

            if (_active.TryGetValue(metadata.SessionId, out var active))
                snapshots.Add(active.CreateSnapshot(maxOutputChars: _config.DefaultReadMaxOutputChars));
            else
                snapshots.Add(await SnapshotFromMetadataAsync(metadata, _config.DefaultReadMaxOutputChars, ct).ConfigureAwait(false));
        }

        return snapshots;
    }

    public async Task<BackgroundTerminalSnapshot> StopAsync(string sessionId, CancellationToken ct = default)
    {
        if (!_active.TryGetValue(sessionId, out var active))
        {
            var metadata = GetMetadata(sessionId);
            return await SnapshotFromMetadataAsync(metadata, _config.DefaultReadMaxOutputChars, ct).ConfigureAwait(false);
        }

        await KillAsync(active, BackgroundTerminalStatus.Killed, ct).ConfigureAwait(false);
        return active.CreateSnapshot(maxOutputChars: _config.DefaultReadMaxOutputChars);
    }

    public async Task<IReadOnlyList<BackgroundTerminalSnapshot>> CleanThreadAsync(string threadId, CancellationToken ct = default)
    {
        var targets = _active.Values
            .Where(t => string.Equals(t.ThreadId, threadId, StringComparison.Ordinal))
            .ToArray();
        var snapshots = new List<BackgroundTerminalSnapshot>();
        foreach (var target in targets)
            snapshots.Add(await StopAsync(target.SessionId, ct).ConfigureAwait(false));
        return snapshots;
    }

    public async Task<IReadOnlyList<string>> DeleteThreadArtifactsAsync(
        string threadId,
        CancellationToken ct = default)
    {
        await CleanThreadAsync(threadId, ct).ConfigureAwait(false);

        var failures = new List<string>();
        var threadDirectory = GetThreadDirectory(threadId);
        try
        {
            if (Directory.Exists(threadDirectory))
                Directory.Delete(threadDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add(threadDirectory);
            _logger?.LogWarning(ex, "Failed to delete background terminal artifacts for thread {ThreadId}.", threadId);
        }

        foreach (var metadata in _metadata.Values.Where(m => string.Equals(m.ThreadId, threadId, StringComparison.Ordinal)).ToArray())
            _metadata.TryRemove(metadata.SessionId, out _);

        return failures;
    }

    public Task<int> CleanupExpiredArtifactsAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _cleanupRunning, 1) != 0)
            return Task.FromResult(0);

        try
        {
            return Task.FromResult(CleanupExpiredArtifactsCore(ct));
        }
        finally
        {
            Volatile.Write(ref _cleanupRunning, 0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _retentionTimer.Dispose();
        foreach (var active in _active.Values.ToArray())
        {
            try
            {
                await KillAsync(active, BackgroundTerminalStatus.Killed, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort shutdown.
            }
        }
    }

    private static ProcessStartInfo CreateStartInfo(BackgroundTerminalStartRequest request, ShellIdentity shell)
    {
        var psi = new ProcessStartInfo
        {
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        psi.FileName = shell.ExecutablePath;
        switch (shell.Family)
        {
            case ShellFamily.Cmd:
                psi.Arguments = "/d /s /c \"" + request.Command.Replace("\"", "\\\"") + "\"";
                break;
            case ShellFamily.PowerShell:
                var script = "$ProgressPreference = 'SilentlyContinue'\n[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n" + request.Command;
                var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                psi.Arguments = $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {encoded}";
                break;
        }

        return psi;
    }

    private static ShellIdentity ResolveHostDefaultShell()
    {
        if (!ShellIdentityResolver.Host.TryResolve(null, out var identity, out var reason))
            throw new InvalidOperationException(reason);
        return identity;
    }

    private async Task WatchProcessAsync(ActiveTerminal terminal)
    {
        try
        {
            await terminal.Process.WaitForExitAsync().ConfigureAwait(false);
            terminal.Process.WaitForExit();
            var status = terminal.Process.ExitCode == 0
                ? BackgroundTerminalStatus.Completed
                : BackgroundTerminalStatus.Failed;
            await CompleteAsync(terminal, status, terminal.Process.ExitCode).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Background terminal {SessionId} watcher failed.", terminal.SessionId);
            await CompleteAsync(terminal, BackgroundTerminalStatus.Failed, null).ConfigureAwait(false);
        }
    }

    private async Task KillAsync(ActiveTerminal terminal, string status, CancellationToken ct)
    {
        if (!terminal.TryBeginCompletion())
        {
            await terminal.WaitForCompletionMetadataAsync(ct).ConfigureAwait(false);
            return;
        }

        if (!terminal.Process.HasExited)
        {
            try
            {
                terminal.Process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }
        }

        try
        {
            await terminal.Process.WaitForExitAsync(ct).ConfigureAwait(false);
            terminal.Process.WaitForExit();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The process has already been killed. Completion still drains any accepted output.
        }

        await CompleteAsync(terminal, status, null, completionReserved: true).ConfigureAwait(false);
    }

    private async Task CompleteAsync(
        ActiveTerminal terminal,
        string status,
        int? exitCode,
        bool completionReserved = false)
    {
        if (!completionReserved && !terminal.TryBeginCompletion())
            return;

        try
        {
            Exception? outputFailure = null;
            try
            {
                await terminal.DrainOutputAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                outputFailure = ex;
                status = BackgroundTerminalStatus.Failed;
                exitCode = null;
                _logger?.LogWarning(ex, "Terminal output persistence failed for {SessionId}.", terminal.SessionId);
            }
            terminal.FinishCompletion(status, exitCode);

            _active.TryRemove(terminal.SessionId, out _);
            var metadata = terminal.ToMetadata(status);
            _metadata[terminal.SessionId] = metadata;
            await PersistMetadataAsync(metadata, CancellationToken.None).ConfigureAwait(false);
            Raise("completed", terminal.CreateSnapshot(maxOutputChars: _config.DefaultReadMaxOutputChars), null);
            if (outputFailure == null)
                terminal.SignalCompletionPublished();
            else
                terminal.SignalCompletionFailed(outputFailure);
        }
        catch (Exception ex)
        {
            terminal.SignalCompletionFailed(ex);
            throw;
        }
    }

    private async Task PersistMetadataAsync(BackgroundTerminalMetadata metadata, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(metadata.MetadataPath)!);
        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        await File.WriteAllTextAsync(metadata.MetadataPath, json + Environment.NewLine, Encoding.UTF8, ct).ConfigureAwait(false);
    }

    private BackgroundTerminalMetadata GetMetadata(string sessionId)
    {
        if (_metadata.TryGetValue(sessionId, out var metadata))
            return metadata;

        throw new KeyNotFoundException($"Background terminal '{sessionId}' was not found.");
    }

    private async Task<BackgroundTerminalSnapshot> SnapshotFromMetadataAsync(
        BackgroundTerminalMetadata metadata,
        int maxOutputChars,
        CancellationToken ct)
    {
        var buffer = await TerminalOutputBuffer.ReadAsync(metadata.OutputPath, ct).ConfigureAwait(false);
        var (limited, original, truncated) = buffer.Snapshot(maxOutputChars);
        return metadata.ToSnapshot(limited, original, truncated);
    }

    private void CleanupExpiredArtifactsOnStartup()
    {
        RunRetentionCleanup("startup");
    }

    private void RunRetentionCleanup(string reason = "scheduled")
    {
        if (Interlocked.Exchange(ref _cleanupRunning, 1) != 0)
            return;

        try
        {
            CleanupExpiredArtifactsCore(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to clean expired background terminal artifacts during {Reason} maintenance.", reason);
        }
        finally
        {
            Volatile.Write(ref _cleanupRunning, 0);
        }
    }

    private int CleanupExpiredArtifactsCore(CancellationToken ct)
    {
        var retentionDays = Math.Max(0, _config.OutputRetentionDays);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        var removed = 0;

        foreach (var metadata in _metadata.Values.ToArray())
        {
            ct.ThrowIfCancellationRequested();
            if (_active.ContainsKey(metadata.SessionId)
                || string.Equals(metadata.Status, BackgroundTerminalStatus.Running, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var completedAt = metadata.CompletedAt ?? metadata.StartedAt;
            if (completedAt > cutoff)
                continue;

            var directory = GetThreadDirectory(metadata.ThreadId);
            var deletedAny = false;
            var metadataPath = Path.Combine(directory, metadata.SessionId + MetadataExtension);
            var outputPath = Path.Combine(directory, metadata.SessionId + OutputExtension);
            deletedAny |= TryDeleteArtifact(metadataPath, metadata.SessionId);
            deletedAny |= TryDeleteArtifact(outputPath, metadata.SessionId);
            var artifactsRemain = File.Exists(metadataPath) || File.Exists(outputPath);
            if (deletedAny && !artifactsRemain)
                removed++;

            if (!artifactsRemain)
            {
                _metadata.TryRemove(metadata.SessionId, out _);
                TryDeleteEmptyDirectory(directory);
            }
        }

        return removed;
    }

    private bool TryDeleteArtifact(string path, string sessionId)
    {
        try
        {
            if (!File.Exists(path))
                return false;
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Failed to delete background terminal artifact {Path} for session {SessionId}.", path, sessionId);
            return false;
        }
    }

    private void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Failed to remove empty background terminal directory {Path}.", path);
        }
    }

    private void LoadMetadataAndMarkLost()
    {
        if (!Directory.Exists(_terminalRoot))
            return;

        foreach (var path in Directory.EnumerateFiles(_terminalRoot, "*" + MetadataExtension, SearchOption.AllDirectories))
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<BackgroundTerminalMetadata>(
                    File.ReadAllText(path),
                    JsonOptions);
                if (metadata == null || string.IsNullOrWhiteSpace(metadata.SessionId))
                    continue;

                if (metadata.Status == BackgroundTerminalStatus.Running)
                {
                    metadata = metadata with
                    {
                        Status = BackgroundTerminalStatus.Lost,
                        CompletedAt = DateTimeOffset.UtcNow
                    };
                    File.WriteAllText(path, JsonSerializer.Serialize(metadata, JsonOptions) + Environment.NewLine, Encoding.UTF8);
                }

                _metadata[metadata.SessionId] = metadata;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load background terminal metadata from {Path}.", path);
            }
        }
    }

    private void EnforceSessionLimits(string threadId)
    {
        var running = _active.Values.ToArray();
        var perThread = running.Count(t => string.Equals(t.ThreadId, threadId, StringComparison.Ordinal));
        if (perThread >= Math.Max(1, _config.MaxSessionsPerThread))
            throw new InvalidOperationException($"Thread '{threadId}' already has the maximum number of running background terminals.");
        if (running.Length >= Math.Max(1, _config.MaxSessionsPerWorkspace))
            throw new InvalidOperationException("The workspace already has the maximum number of running background terminals.");
    }

    private int NormalizeYield(int yieldTimeMs)
    {
        var requested = yieldTimeMs <= 0 ? _config.DefaultYieldTimeMs : yieldTimeMs;
        return Math.Clamp(requested, 0, Math.Max(1, _config.MaxYieldTimeMs));
    }

    private void Raise(string eventType, BackgroundTerminalSnapshot terminal, string? delta)
    {
        try
        {
            TerminalEvent?.Invoke(new BackgroundTerminalEvent
            {
                EventType = eventType,
                Terminal = terminal,
                Delta = delta
            });
        }
        catch
        {
            // Terminal listeners must not affect process management.
        }
    }

    private static async Task<ProcessWaitOutcome> WaitForExitOrDelayAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            return ProcessWaitOutcome.Exited;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ProcessWaitOutcome.CallerCancelled;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return ProcessWaitOutcome.TimedOut;
        }
    }

    internal string GetThreadDirectory(string threadId)
    {
        var segment = string.IsNullOrWhiteSpace(threadId)
            ? "workspace"
            : ThreadArtifactPathResolver.GetCanonicalThreadSegment(threadId);
        var path = Path.GetFullPath(Path.Combine(_terminalRoot, segment));
        var root = Path.GetFullPath(_terminalRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Background terminal path escaped its artifact root.");
        return path;
    }

    internal static string GetCanonicalThreadDirectoryName(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "workspace"
            : ThreadArtifactPathResolver.GetCanonicalThreadSegment(value);

}
