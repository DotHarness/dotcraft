using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Tools.BackgroundTerminals;

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

    private readonly IBackgroundTerminalService? _backgroundTerminals;

    public ShellTools(
        string workingDirectory,
        int timeoutSeconds = 60,
        bool requireApprovalOutsideWorkspace = true,
        int maxOutputLength = 10000,
        IApprovalService? approvalService = null,
        PathBlacklist? blacklist = null,
        IBackgroundTerminalService? backgroundTerminals = null)
    {
        _workingDirectory = Path.GetFullPath(workingDirectory);
        _timeoutSeconds = timeoutSeconds;
        _requireApprovalOutsideWorkspace = requireApprovalOutsideWorkspace;
        _maxOutputLength = maxOutputLength;
        _approvalService = approvalService;
        _blacklist = blacklist;
        _backgroundTerminals = backgroundTerminals;
        _inspector = new ShellCommandInspector(_workingDirectory);
    }

    [Description("Execute a shell command and return its output.")]
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
                ? SanitizePowerShellStderr(errorBuilder.ToString().TrimEnd())
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
            commandExecution ??= CommandExecutionTracker.Begin(command, cwd, source: "host");
            var runtime = CommandExecutionRuntimeScope.Current;
            var snapshot = await _backgroundTerminals!.StartAsync(new BackgroundTerminalStartRequest
            {
                ThreadId = runtime?.ThreadId ?? "workspace",
                TurnId = runtime?.TurnId,
                Command = command,
                WorkingDirectory = cwd,
                Source = "host",
                RunInBackground = runInBackground,
                Interactive = interactive,
                Shell = shell,
                TimeoutSeconds = _timeoutSeconds,
                YieldTimeMs = yieldTimeMs ?? 1000,
                MaxOutputChars = maxOutputChars ?? _maxOutputLength
            });

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

    /// <summary>
    /// Parses PowerShell CLIXML stderr output and extracts only the human-readable error text.
    /// Progress records are stripped entirely; error strings are extracted and decoded.
    /// Falls back to regex-based stripping if XML parsing fails.
    /// </summary>
    private static string SanitizePowerShellStderr(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return stderr;

        // Only process CLIXML-formatted output
        var trimmed = stderr.TrimStart('\r', '\n');
        if (!trimmed.StartsWith("#< CLIXML", StringComparison.Ordinal))
            return stderr;

        try
        {
            // Strip the CLIXML header line and parse the XML body
            var xmlStart = trimmed.IndexOf('<');
            if (xmlStart < 0)
                return string.Empty;

            var xml = trimmed[xmlStart..];
            var doc = XDocument.Parse(xml);

            XNamespace ns = "http://schemas.microsoft.com/powershell/2004/04";
            var errors = doc.Descendants(ns + "S")
                .Where(e => (string?)e.Attribute("S") == "Error")
                .Select(e => DecodeCLIXMLString(e.Value))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            return errors.Count > 0
                ? string.Join(Environment.NewLine, errors).TrimEnd()
                : string.Empty;
        }
        catch
        {
            // Fallback: strip the CLIXML header and any XML blocks, keep other lines
            var lines = stderr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var kept = lines
                .Where(l => !l.TrimStart().StartsWith("#< CLIXML", StringComparison.Ordinal)
                         && !l.TrimStart().StartsWith('<'))
                .ToArray();
            return kept.Length > 0 ? string.Join(Environment.NewLine, kept) : string.Empty;
        }
    }

    /// <summary>
    /// Decodes CLIXML escape sequences such as _x000D__x000A_ (CR+LF) back to their characters.
    /// </summary>
    private static string DecodeCLIXMLString(string value)
    {
        // Replace encoded carriage-return/linefeed pairs with a single newline
        value = value.Replace("_x000D__x000A_", "\n");

        // Decode remaining _xHHHH_ Unicode escapes
        return Regex.Replace(value, @"_x([0-9A-Fa-f]{4})_", m =>
        {
            var codePoint = Convert.ToInt32(m.Groups[1].Value, 16);
            return ((char)codePoint).ToString();
        });
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
        var workspace = new DirectoryInfo(_workingDirectory).FullName;
        var isOutsideWorkspace = !cwdPath.StartsWith(workspace, StringComparison.OrdinalIgnoreCase);

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
