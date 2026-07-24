using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Utilities;

namespace DotCraft.Tools;

/// <summary>
/// Shell command execution with safety guards.
/// </summary>
public sealed class ShellTools
{
    private readonly string _workingDirectory;

    private readonly int _timeoutSeconds;

    private readonly bool _requireApprovalOutsideWorkspace;

    private readonly int _maxOutputLength;

    private readonly IApprovalService? _approvalService;

    private readonly PathBlacklist? _blacklist;

    private readonly ShellCommandInspector _inspector;

    private readonly IReadOnlyList<string> _workspaceRoots;

    private readonly IBackgroundTerminalService? _backgroundTerminals;

    public ShellTools(
        string workingDirectory,
        int timeoutSeconds = 60,
        bool requireApprovalOutsideWorkspace = true,
        int maxOutputLength = 10000,
        IApprovalService? approvalService = null,
        PathBlacklist? blacklist = null,
        IBackgroundTerminalService? backgroundTerminals = null,
        IReadOnlyList<string>? workspaceRoots = null)
    {
        _workingDirectory = Path.GetFullPath(workingDirectory);
        _timeoutSeconds = timeoutSeconds;
        _requireApprovalOutsideWorkspace = requireApprovalOutsideWorkspace;
        _maxOutputLength = maxOutputLength;
        _approvalService = approvalService;
        _blacklist = blacklist;
        _backgroundTerminals = backgroundTerminals;
        _workspaceRoots = (workspaceRoots ?? [_workingDirectory])
            .Select(Path.GetFullPath)
            .ToArray();
        _inspector = new ShellCommandInspector(_workingDirectory, _workspaceRoots);
    }

    [Description("Execute a shell command and return its output. On Windows PowerShell, run inline Python by piping a here-string to stdin, for example @'\\nprint('hello')\\n'@ | python -, instead of python -c with nested escaped quotes.")]
    [Tool(Icon = "⌨️", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.Exec), MaxResultChars = 30_000)]
    public async Task<string> Exec(
        [Description("The shell command to execute.")] string command,
        [Description("Optional working directory for the command.")] string? workingDir = null,
        [Description("Run the command in the background and return a session ID for later WriteStdin calls.")] bool runInBackground = false,
        [Description("Milliseconds to wait for initial output before returning when runInBackground is true.")] int? yieldTimeMs = null,
        [Description("Maximum output characters to return in this tool result.")] int? maxOutputChars = null,
        [Description("Keep stdin open so WriteStdin can send input to the running process. This is pipe-based, not a full PTY.")] bool interactive = false,
        [Description("Optional shell override. On Windows use 'powershell' or 'cmd'; on Unix provide a shell path such as /bin/bash.")] string? shell = null)
    {
        var cwd = !string.IsNullOrWhiteSpace(workingDir)
            ? Path.GetFullPath(workingDir)
            : _workingDirectory;

        var commandExecution = CommandExecutionTracker.Begin(command, cwd, source: "host");
        var guardError = await GuardCommandAsync(command, cwd);
        if (guardError != null)
        {
            commandExecution?.Complete(guardError, status: "failed", exitCode: null);
            return guardError;
        }

        if (_backgroundTerminals != null)
            return await ExecWithBackgroundTerminalServiceAsync(
                command,
                cwd,
                runInBackground,
                yieldTimeMs,
                maxOutputChars,
                interactive,
                shell,
                commandExecution);

        try
        {
            var isWindows = OperatingSystem.IsWindows();

            var psi = new ProcessStartInfo
            {
                WorkingDirectory = cwd,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            if (isWindows)
            {
                var script = "$ProgressPreference = 'SilentlyContinue'\n[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n" + command;
                var bytes = Encoding.Unicode.GetBytes(script);
                var encoded = Convert.ToBase64String(bytes);
                psi.FileName = "powershell.exe";
                psi.Arguments = $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {encoded}";
            }
            else
            {
                psi.FileName = "/bin/bash";
            }

            using var process = Process.Start(psi);
            if (process == null)
            {
                commandExecution?.Complete("Error: Failed to start process.", status: "failed", exitCode: null);
                return "Error: Failed to start process.";
            }

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            var outputLock = new object();
            var stderrHeaderWritten = false;

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (outputLock)
                {
                    outputBuilder.AppendLine(e.Data);
                }
                commandExecution?.Append(e.Data + Environment.NewLine);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (outputLock)
                {
                    errorBuilder.AppendLine(e.Data);
                    if (!stderrHeaderWritten)
                    {
                        stderrHeaderWritten = true;
                        var prefix = outputBuilder.Length > 0 ? Environment.NewLine + "STDERR:" + Environment.NewLine : "STDERR:" + Environment.NewLine;
                        commandExecution?.Append(prefix);
                    }
                }
                commandExecution?.Append(e.Data + Environment.NewLine);
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!isWindows)
            {
                await process.StandardInput.WriteLineAsync(command);
                process.StandardInput.Close();
            }

            var completed = await process.WaitForExitAsync(
                TimeSpan.FromSeconds(_timeoutSeconds));

            if (!completed)
            {
                process.Kill(entireProcessTree: true);
                var timeoutOutput = $"Error: Command timed out after {_timeoutSeconds} seconds.";
                commandExecution?.Complete(timeoutOutput, status: "cancelled", exitCode: null);
                return timeoutOutput;
            }

            var result = new StringBuilder();
            var stdout = outputBuilder.ToString().TrimEnd();
            var stderr = isWindows
                ? PowerShellStderrSanitizer.Sanitize(errorBuilder.ToString().TrimEnd())
                : errorBuilder.ToString().TrimEnd();

            if (!string.IsNullOrWhiteSpace(stdout))
                result.Append(stdout);

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                if (result.Length > 0) result.AppendLine();
                result.AppendLine("STDERR:");
                result.Append(stderr);
            }

            if (process.ExitCode != 0)
            {
                if (result.Length > 0) result.AppendLine();
                result.AppendLine($"Exit code: {process.ExitCode}");
            }

            var output = result.Length > 0 ? result.ToString() : "(no output)";

            if (output.Length > _maxOutputLength)
            {
                output = output[.._maxOutputLength]
                         + $"\n... (truncated, {output.Length - _maxOutputLength} more chars)";
            }

            commandExecution?.Complete(
                output,
                status: process.ExitCode == 0 ? "completed" : "failed",
                exitCode: process.ExitCode);

            return output;
        }
        catch (Exception ex)
        {
            var error = $"Error executing command: {ex.Message}";
            commandExecution?.Complete(error, status: "failed", exitCode: null);
            return error;
        }
    }

    [Description("Write input to a running background terminal session, or pass an empty input string to poll for recent output.")]
    [Tool(Icon = "⌨️", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.Exec), MaxResultChars = 30_000)]
    public async Task<string> WriteStdin(
        [Description("Background terminal session ID returned by Exec.")] string sessionId,
        [Description("Characters to write to stdin. Include newlines when the process expects Enter.")] string input = "",
        [Description("Milliseconds to wait after writing before returning output.")] int? yieldTimeMs = null,
        [Description("Maximum output characters to return.")] int? maxOutputChars = null)
    {
        if (_backgroundTerminals == null)
            return "Error: Background terminals are not available.";

        try
        {
            var snapshot = await _backgroundTerminals.WriteStdinAsync(
                sessionId,
                input,
                yieldTimeMs ?? 1000,
                maxOutputChars ?? _maxOutputLength);
            return FormatSnapshot(snapshot);
        }
        catch (Exception ex)
        {
            return $"Error writing to background terminal: {ex.Message}";
        }
    }

    private async Task<string> ExecWithBackgroundTerminalServiceAsync(
        string command,
        string cwd,
        bool runInBackground,
        int? yieldTimeMs,
        int? maxOutputChars,
        bool interactive,
        string? shell,
        CommandExecutionTracker? commandExecution)
    {
        try
        {
            var terminals = _backgroundTerminals
                ?? throw new InvalidOperationException("Background terminals are not available.");
            commandExecution ??= CommandExecutionTracker.Begin(command, cwd, source: "host");
            var runtime = CommandExecutionRuntimeScope.Current;
            var shellExecution = runtime?.TryClaimPendingShellExecution(command, cwd);
            var callId = shellExecution?.CallId ?? commandExecution?.CallId;
            void ForwardForegroundTerminalDelta(BackgroundTerminalEvent evt)
            {
                if (!string.Equals(evt.EventType, "outputDelta", StringComparison.Ordinal))
                    return;
                if (string.IsNullOrEmpty(evt.Delta))
                    return;
                if (!string.Equals(evt.Terminal.CallId, callId, StringComparison.Ordinal))
                    return;
                if (string.Equals(evt.Terminal.BackgroundReason, "runInBackground", StringComparison.Ordinal))
                    return;

                commandExecution?.Append(evt.Delta, mirrorsTerminalOutput: true);
            }

            var shouldForwardTerminalDelta =
                !runInBackground
                && commandExecution != null
                && !string.IsNullOrWhiteSpace(callId);
            if (shouldForwardTerminalDelta)
                terminals.TerminalEvent += ForwardForegroundTerminalDelta;

            BackgroundTerminalSnapshot snapshot;
            try
            {
                snapshot = await terminals.StartAsync(new BackgroundTerminalStartRequest
                {
                    ThreadId = runtime?.ThreadId ?? "workspace",
                    TurnId = runtime?.TurnId,
                    CallId = callId,
                    Command = command,
                    WorkingDirectory = cwd,
                    Source = shellExecution?.Source ?? "host",
                    RunInBackground = runInBackground,
                    Interactive = interactive,
                    Shell = shell,
                    TimeoutSeconds = _timeoutSeconds,
                    YieldTimeMs = yieldTimeMs ?? 1000,
                    MaxOutputChars = maxOutputChars ?? _maxOutputLength
                });
            }
            finally
            {
                if (shouldForwardTerminalDelta)
                    terminals.TerminalEvent -= ForwardForegroundTerminalDelta;
            }

            var toolResult = runInBackground || snapshot.Status == BackgroundTerminalStatus.Running
                ? FormatSnapshot(snapshot)
                : FormatForegroundSnapshot(snapshot);
            var status = snapshot.Status == BackgroundTerminalStatus.Running
                ? "backgrounded"
                : snapshot.Status == BackgroundTerminalStatus.Completed
                    ? "completed"
                    : snapshot.Status == BackgroundTerminalStatus.Killed || snapshot.Status == BackgroundTerminalStatus.TimedOut
                        ? "cancelled"
                        : "failed";
            commandExecution?.Complete(
                toolResult,
                status,
                snapshot.ExitCode,
                snapshot.SessionId,
                snapshot.OutputPath,
                snapshot.OriginalOutputChars,
                snapshot.Truncated,
                snapshot.BackgroundReason);
            return toolResult;
        }
        catch (Exception ex)
        {
            var error = $"Error executing command: {ex.Message}";
            commandExecution?.Complete(error, status: "failed", exitCode: null);
            return error;
        }
    }

    private static string FormatSnapshot(BackgroundTerminalSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Session ID: {snapshot.SessionId}");
        sb.AppendLine($"Status: {snapshot.Status}");
        sb.AppendLine($"Command: {snapshot.Command}");
        sb.AppendLine($"Working directory: {snapshot.WorkingDirectory}");
        sb.AppendLine($"Output path: {snapshot.OutputPath}");
        if (snapshot.ExitCode != null)
            sb.AppendLine($"Exit code: {snapshot.ExitCode}");
        if (snapshot.Status == BackgroundTerminalStatus.Running)
            sb.AppendLine("The command is still running in the background.");
        if (snapshot.Truncated)
            sb.AppendLine($"Output truncated from {snapshot.OriginalOutputChars} chars.");
        sb.AppendLine();
        sb.Append(snapshot.Output);
        return sb.ToString().TrimEnd();
    }

    private static string FormatForegroundSnapshot(BackgroundTerminalSnapshot snapshot)
    {
        var output = string.IsNullOrWhiteSpace(snapshot.Output) ? "(no output)" : snapshot.Output;
        if (snapshot.ExitCode is { } exitCode and not 0)
            return output + Environment.NewLine + $"Exit code: {exitCode}";
        return output;
    }

    private async Task<string?> GuardCommandAsync(string command, string cwd)
    {
        var normalized = command.Trim();

        if (_blacklist != null && _blacklist.CommandReferencesBlacklistedPath(command))
        {
            return "Error: Command references a blacklisted path and cannot be executed.";
        }

        var hasPathTraversal = normalized.Contains("..\\") || normalized.Contains("../");

        var cwdPath = new DirectoryInfo(cwd).FullName;
        var isOutsideWorkspace = !_workspaceRoots.Any(root => IsWithinBoundary(cwdPath, root));

        // Detect absolute, ~, and $HOME paths that resolve outside the workspace
        var outsidePaths = _inspector.DetectOutsideWorkspacePaths(command);
        var referencesOutsidePaths = outsidePaths.Count > 0;

        if (hasPathTraversal || isOutsideWorkspace || referencesOutsidePaths)
        {
            if (!_requireApprovalOutsideWorkspace)
            {
                if (referencesOutsidePaths)
                    return $"Error: Command references paths outside workspace: {string.Join(", ", outsidePaths)}";
                if (hasPathTraversal)
                    return "Error: Command blocked by safety guard (path traversal detected).";
                if (isOutsideWorkspace)
                    return "Error: Working directory is outside workspace boundary.";
            }

            if (_approvalService != null)
            {
                var context = ApprovalContextScope.Current;
                var approved = await _approvalService.RequestShellApprovalAsync(command, cwd, context);
                if (!approved)
                {
                    return "Error: Command execution was rejected by user.";
                }
            }
        }

        return null;
    }

    private static bool IsWithinBoundary(string path, string root)
    {
        var boundary = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return path.Equals(boundary, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(boundary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(boundary + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

file static class ProcessExtensions
{
    public static async Task<bool> WaitForExitAsync(this Process process, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
