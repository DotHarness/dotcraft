using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotCraft.Configuration;
using Microsoft.Extensions.Logging;

namespace DotCraft.Tools.BackgroundTerminals;

/// <summary>
/// Status values for a server-managed background terminal session.
/// </summary>
public static class BackgroundTerminalStatus
{
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Killed = "killed";
    public const string TimedOut = "timedOut";
    public const string Lost = "lost";
}

/// <summary>
/// Request used to start a background-terminal capable command.
/// </summary>
public sealed record BackgroundTerminalStartRequest
{
    public string ThreadId { get; init; } = "workspace";

    public string? TurnId { get; init; }

    public string? CallId { get; init; }

    public string Command { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;

    public string Source { get; init; } = "host";

    public string? Shell { get; init; }

    public bool RunInBackground { get; init; }

    public bool Interactive { get; init; }

    public int TimeoutSeconds { get; init; } = 300;

    public int YieldTimeMs { get; init; } = 1000;

    public int MaxOutputChars { get; init; } = 10000;
}

/// <summary>
/// Snapshot returned by terminal operations.
/// </summary>
public sealed record BackgroundTerminalSnapshot
{
    public string SessionId { get; init; } = string.Empty;

    public string ThreadId { get; init; } = string.Empty;

    public string? TurnId { get; init; }

    public string? CallId { get; init; }

    public string Command { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;

    public string Source { get; init; } = "host";

    public string Status { get; init; } = BackgroundTerminalStatus.Running;

    public string Output { get; init; } = string.Empty;

    public string OutputPath { get; init; } = string.Empty;

    public int? ExitCode { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public long WallTimeMs { get; init; }

    public int OriginalOutputChars { get; init; }

    public bool Truncated { get; init; }

    public string? BackgroundReason { get; init; }
}

/// <summary>
/// Notification raised when a terminal lifecycle event occurs.
/// </summary>
public sealed record BackgroundTerminalEvent
{
    public string EventType { get; init; } = string.Empty;

    public required BackgroundTerminalSnapshot Terminal { get; init; }

    public string? Delta { get; init; }
}

/// <summary>
/// Service contract for server-managed background terminals.
/// </summary>
public interface IBackgroundTerminalService
{
    event Action<BackgroundTerminalEvent>? TerminalEvent;

    Task<BackgroundTerminalSnapshot> StartAsync(BackgroundTerminalStartRequest request, CancellationToken ct = default);

    Task<BackgroundTerminalSnapshot> ReadAsync(string sessionId, int waitMs = 0, int? maxOutputChars = null, CancellationToken ct = default);

    Task<BackgroundTerminalSnapshot> WriteStdinAsync(string sessionId, string input, int yieldTimeMs = 1000, int? maxOutputChars = null, CancellationToken ct = default);

    Task<IReadOnlyList<BackgroundTerminalSnapshot>> ListAsync(string? threadId = null, CancellationToken ct = default);

    Task<BackgroundTerminalSnapshot> StopAsync(string sessionId, CancellationToken ct = default);

    Task<IReadOnlyList<BackgroundTerminalSnapshot>> CleanThreadAsync(string threadId, CancellationToken ct = default);
}

/// <summary>
/// Pipe-based process manager for host shell commands that may outlive a single tool call.
/// </summary>
public sealed class BackgroundTerminalService : IBackgroundTerminalService, IAsyncDisposable
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
    private readonly ConcurrentDictionary<string, ActiveTerminal> _active = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, BackgroundTerminalMetadata> _metadata = new(StringComparer.Ordinal);

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
    }

    public event Action<BackgroundTerminalEvent>? TerminalEvent;

    public async Task<BackgroundTerminalSnapshot> StartAsync(
        BackgroundTerminalStartRequest request,
        CancellationToken ct = default)
    {
        if (!_config.Enabled)
            throw new InvalidOperationException("Background terminals are disabled by Tools.Shell.Background.Enabled.");
        if (string.IsNullOrWhiteSpace(request.Command))
            throw new ArgumentException("Command is required.", nameof(request));

        EnforceSessionLimits(request.ThreadId);

        var sessionId = "term_" + Guid.NewGuid().ToString("N")[..12];
        var threadId = SanitizePathSegment(request.ThreadId);
        var sessionDir = Path.Combine(_terminalRoot, threadId);
        Directory.CreateDirectory(sessionDir);
        var outputPath = Path.Combine(sessionDir, sessionId + OutputExtension);
        var metadataPath = Path.Combine(sessionDir, sessionId + MetadataExtension);

        var psi = CreateStartInfo(request);
        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start process.");

        var terminal = new ActiveTerminal(
            sessionId,
            metadataPath,
            outputPath,
            request,
            process,
            DateTimeOffset.UtcNow,
            this);

        _active[sessionId] = terminal;
        _metadata[sessionId] = terminal.ToMetadata(BackgroundTerminalStatus.Running);
        await PersistMetadataAsync(_metadata[sessionId], ct).ConfigureAwait(false);
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
            var completed = await WaitForExitOrDelayAsync(process, TimeSpan.FromMilliseconds(yieldMs), ct).ConfigureAwait(false);
            if (!completed)
                return terminal.CreateSnapshot(BackgroundTerminalStatus.Running, request.MaxOutputChars, "runInBackground");
        }
        else
        {
            var timeoutSeconds = Math.Max(1, request.TimeoutSeconds);
            var completed = await WaitForExitOrDelayAsync(process, TimeSpan.FromSeconds(timeoutSeconds), ct).ConfigureAwait(false);
            if (!completed)
            {
                await KillAsync(terminal, BackgroundTerminalStatus.TimedOut, ct).ConfigureAwait(false);
                return terminal.CreateSnapshot(BackgroundTerminalStatus.TimedOut, request.MaxOutputChars);
            }
        }

        await terminal.WaitForCompletionMetadataAsync(ct).ConfigureAwait(false);
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
                await WaitForExitOrDelayAsync(active.Process, waitFor, ct).ConfigureAwait(false);
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

    public async ValueTask DisposeAsync()
    {
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

    private static ProcessStartInfo CreateStartInfo(BackgroundTerminalStartRequest request)
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

        if (OperatingSystem.IsWindows())
        {
            var shell = string.IsNullOrWhiteSpace(request.Shell) ? "powershell" : request.Shell.Trim();
            if (string.Equals(shell, "cmd", StringComparison.OrdinalIgnoreCase)
                || string.Equals(shell, "cmd.exe", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = "cmd.exe";
                psi.Arguments = "/d /s /c \"" + request.Command.Replace("\"", "\\\"") + "\"";
            }
            else
            {
                var script = "$ProgressPreference = 'SilentlyContinue'\n[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n" + request.Command;
                var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                psi.FileName = "powershell.exe";
                psi.Arguments = $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {encoded}";
            }
        }
        else
        {
            psi.FileName = string.IsNullOrWhiteSpace(request.Shell) ? "/bin/bash" : request.Shell.Trim();
        }

        return psi;
    }

    private async Task WatchProcessAsync(ActiveTerminal terminal)
    {
        try
        {
            await terminal.Process.WaitForExitAsync().ConfigureAwait(false);
            await Task.Delay(100).ConfigureAwait(false);
            var status = terminal.Process.ExitCode == 0
                ? BackgroundTerminalStatus.Completed
                : BackgroundTerminalStatus.Failed;
            await CompleteAsync(terminal, status, terminal.Process.ExitCode, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Background terminal {SessionId} watcher failed.", terminal.SessionId);
            await CompleteAsync(terminal, BackgroundTerminalStatus.Failed, null, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task KillAsync(ActiveTerminal terminal, string status, CancellationToken ct)
    {
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

        await CompleteAsync(terminal, status, null, ct).ConfigureAwait(false);
    }

    private async Task CompleteAsync(ActiveTerminal terminal, string status, int? exitCode, CancellationToken ct)
    {
        if (!terminal.TryComplete(status, exitCode))
            return;

        _active.TryRemove(terminal.SessionId, out _);
        var metadata = terminal.ToMetadata(status);
        _metadata[terminal.SessionId] = metadata;
        await PersistMetadataAsync(metadata, ct).ConfigureAwait(false);
        Raise("completed", terminal.CreateSnapshot(maxOutputChars: _config.DefaultReadMaxOutputChars), null);
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
        var output = File.Exists(metadata.OutputPath)
            ? await File.ReadAllTextAsync(metadata.OutputPath, ct).ConfigureAwait(false)
            : string.Empty;
        var (limited, original, truncated) = LimitOutput(output.TrimEnd('\r', '\n'), maxOutputChars);
        return metadata.ToSnapshot(limited, original, truncated);
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

    private static async Task<bool> WaitForExitOrDelayAsync(Process process, TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return false;
        }
    }

    internal static (string Output, int OriginalChars, bool Truncated) LimitOutput(string output, int maxOutputChars)
    {
        if (maxOutputChars <= 0 || output.Length <= maxOutputChars)
            return (output.Length == 0 ? "(no output)" : output, output.Length, false);

        var truncated = output[^maxOutputChars..];
        return ($"... (truncated, {output.Length - maxOutputChars} earlier chars){Environment.NewLine}{truncated}", output.Length, true);
    }

    private static string SanitizePathSegment(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "workspace" : value.Trim();
        return Regex.Replace(normalized, @"[^A-Za-z0-9_.-]", "_");
    }

    private sealed class ActiveTerminal
    {
        private readonly BackgroundTerminalService _owner;
        private readonly object _sync = new();
        private readonly StringBuilder _output = new();
        private bool _completed;
        private string _status = BackgroundTerminalStatus.Running;
        private int? _exitCode;
        private DateTimeOffset? _completedAt;

        public ActiveTerminal(
            string sessionId,
            string metadataPath,
            string outputPath,
            BackgroundTerminalStartRequest request,
            Process process,
            DateTimeOffset startedAt,
            BackgroundTerminalService owner)
        {
            SessionId = sessionId;
            MetadataPath = metadataPath;
            OutputPath = outputPath;
            Request = request;
            Process = process;
            StartedAt = startedAt;
            _owner = owner;
        }

        public string SessionId { get; }

        public string MetadataPath { get; }

        public string OutputPath { get; }

        public string ThreadId => Request.ThreadId;

        public BackgroundTerminalStartRequest Request { get; }

        public Process Process { get; }

        public DateTimeOffset StartedAt { get; }

        public TaskCompletionSource MetadataCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void BeginReading()
        {
            Process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    Append(e.Data + Environment.NewLine);
            };
            Process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    Append(e.Data + Environment.NewLine);
            };
            Process.EnableRaisingEvents = true;
            Process.BeginOutputReadLine();
            Process.BeginErrorReadLine();
        }

        public bool TryComplete(string status, int? exitCode)
        {
            lock (_sync)
            {
                if (_completed)
                    return false;

                _completed = true;
                _status = status;
                _exitCode = exitCode;
                _completedAt = DateTimeOffset.UtcNow;
            }

            MetadataCompleted.TrySetResult();
            return true;
        }

        public async Task WaitForCompletionMetadataAsync(CancellationToken ct)
        {
            await MetadataCompleted.Task.WaitAsync(ct).ConfigureAwait(false);
        }

        public BackgroundTerminalMetadata ToMetadata(string? status = null)
        {
            lock (_sync)
            {
                return new BackgroundTerminalMetadata
                {
                    SessionId = SessionId,
                    ThreadId = Request.ThreadId,
                    TurnId = Request.TurnId,
                    CallId = Request.CallId,
                    Command = Request.Command,
                    WorkingDirectory = Request.WorkingDirectory,
                    Source = Request.Source,
                    Status = status ?? _status,
                    OutputPath = OutputPath,
                    MetadataPath = MetadataPath,
                    ExitCode = _exitCode,
                    StartedAt = StartedAt,
                    CompletedAt = _completedAt
                };
            }
        }

        public BackgroundTerminalSnapshot CreateSnapshot(
            string? status = null,
            int? maxOutputChars = null,
            string? backgroundReason = null)
        {
            string output;
            int? exitCode;
            DateTimeOffset? completedAt;
            string effectiveStatus;
            lock (_sync)
            {
                output = _output.ToString().TrimEnd('\r', '\n');
                exitCode = _exitCode;
                completedAt = _completedAt;
                effectiveStatus = status ?? _status;
            }

            var (limited, original, truncated) = LimitOutput(output, maxOutputChars ?? Request.MaxOutputChars);
            return new BackgroundTerminalSnapshot
            {
                SessionId = SessionId,
                ThreadId = Request.ThreadId,
                TurnId = Request.TurnId,
                CallId = Request.CallId,
                Command = Request.Command,
                WorkingDirectory = Request.WorkingDirectory,
                Source = Request.Source,
                Status = effectiveStatus,
                Output = limited,
                OutputPath = OutputPath,
                ExitCode = effectiveStatus == BackgroundTerminalStatus.Running ? null : exitCode,
                StartedAt = StartedAt,
                CompletedAt = completedAt,
                WallTimeMs = (long)Math.Max(0, ((completedAt ?? DateTimeOffset.UtcNow) - StartedAt).TotalMilliseconds),
                OriginalOutputChars = original,
                Truncated = truncated,
                BackgroundReason = backgroundReason
            };
        }

        private void Append(string text)
        {
            BackgroundTerminalSnapshot snapshot;
            lock (_sync)
            {
                _output.Append(text);
                Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);
                File.AppendAllText(OutputPath, text, Encoding.UTF8);
                snapshot = CreateSnapshot(maxOutputChars: _owner._config.DefaultReadMaxOutputChars);
            }

            _owner.Raise("outputDelta", snapshot, text);
        }
    }

    private sealed record BackgroundTerminalMetadata
    {
        public string SessionId { get; init; } = string.Empty;

        public string ThreadId { get; init; } = string.Empty;

        public string? TurnId { get; init; }

        public string? CallId { get; init; }

        public string Command { get; init; } = string.Empty;

        public string WorkingDirectory { get; init; } = string.Empty;

        public string Source { get; init; } = "host";

        public string Status { get; init; } = BackgroundTerminalStatus.Running;

        public string OutputPath { get; init; } = string.Empty;

        public string MetadataPath { get; init; } = string.Empty;

        public int? ExitCode { get; init; }

        public DateTimeOffset StartedAt { get; init; }

        public DateTimeOffset? CompletedAt { get; init; }

        public BackgroundTerminalSnapshot ToSnapshot(string output, int originalChars, bool truncated) => new()
        {
            SessionId = SessionId,
            ThreadId = ThreadId,
            TurnId = TurnId,
            CallId = CallId,
            Command = Command,
            WorkingDirectory = WorkingDirectory,
            Source = Source,
            Status = Status,
            Output = output,
            OutputPath = OutputPath,
            ExitCode = ExitCode,
            StartedAt = StartedAt,
            CompletedAt = CompletedAt,
            WallTimeMs = (long)Math.Max(0, ((CompletedAt ?? DateTimeOffset.UtcNow) - StartedAt).TotalMilliseconds),
            OriginalOutputChars = originalChars,
            Truncated = truncated
        };
    }
}
