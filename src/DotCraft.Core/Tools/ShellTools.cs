using System.ComponentModel;
using System.Text;
using DotCraft.Security;
using DotCraft.Security.ShellCommands;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Sessions;

namespace DotCraft.Tools;

public sealed class ShellTools
{
    private readonly string _workingDirectory;

    private readonly int _timeoutSeconds;

    private readonly int _maxOutputLength;

    private readonly IBackgroundTerminalService _backgroundTerminals;

    private readonly ShellExecutionGate _gate;

    public ShellTools(
        string workingDirectory,
        IBackgroundTerminalService backgroundTerminals,
        int timeoutSeconds = 60,
        bool requireApprovalOutsideWorkspace = true,
        int maxOutputLength = 10000,
        IApprovalService? approvalService = null,
        PathBlacklist? blacklist = null,
        IReadOnlyList<string>? workspaceRoots = null,
        ShellPolicySource? policy = null)
    {
        _workingDirectory = Path.GetFullPath(workingDirectory);
        _timeoutSeconds = timeoutSeconds;
        _maxOutputLength = maxOutputLength;
        _backgroundTerminals = backgroundTerminals;
        var roots = (workspaceRoots ?? [_workingDirectory])
            .Select(Path.GetFullPath)
            .ToArray();
        _gate = new ShellExecutionGate(
            new ShellCommandSafetyKernel(),
            new WorkspaceBoundary(roots),
            policy ?? ShellPolicySource.Empty,
            blacklist,
            requireApprovalOutsideWorkspace,
            approvalService);
    }

    [Description("Execute a shell command and return its output. On Windows PowerShell, run inline Python by piping a here-string to stdin, for example @'\\nprint('hello')\\n'@ | python -, instead of python -c with nested escaped quotes.")]
    [Tool(Icon = "⌨️", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.Exec), MaxResultChars = 30_000)]
    [ToolRpc]
    public async Task<string> Exec(
        [Description("The shell command to execute.")] string command,
        [Description("Optional working directory for the command.")] string? workingDir = null,
        [Description("Run the command in the background and return a session ID for later WriteStdin calls.")] bool runInBackground = false,
        [Description("Milliseconds to wait for initial output before returning when runInBackground is true.")] int? yieldTimeMs = null,
        [Description("Maximum output characters to return in this tool result.")] int? maxOutputChars = null,
        [Description("Keep stdin open so WriteStdin can send input to the running process. This is pipe-based, not a full PTY.")] bool interactive = false,
        [Description("Optional shell override. On Windows use 'powershell', 'pwsh', or 'cmd'; on Unix use bash, sh, zsh, or pwsh.")] string? shell = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cwd = !string.IsNullOrWhiteSpace(workingDir)
            ? Path.GetFullPath(workingDir)
            : _workingDirectory;

        var commandExecution = CommandExecutionTracker.Begin(command, cwd, source: "host");
        ShellGateResult gate;
        try
        {
            gate = await _gate.AuthorizeAsync(command, shell, cwd, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            commandExecution?.Complete(string.Empty, status: "cancelled", exitCode: null);
            throw;
        }

        if (!gate.IsAllowed)
        {
            commandExecution?.Complete(gate.Error!, status: "failed", exitCode: null);
            return gate.Error!;
        }

        return await ExecWithBackgroundTerminalServiceAsync(
            command,
            cwd,
            runInBackground,
            yieldTimeMs,
            maxOutputChars,
            interactive,
            gate.Shell!,
            commandExecution,
            cancellationToken);
    }

    [Description("Write input to a running background terminal session, or pass an empty input string to poll for recent output.")]
    [Tool(Icon = "⌨️", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.Exec), MaxResultChars = 30_000)]
    [ToolRpc]
    public async Task<string> WriteStdin(
        [Description("Background terminal session ID returned by Exec.")] string sessionId,
        [Description("Characters to write to stdin. Include newlines when the process expects Enter.")] string input = "",
        [Description("Milliseconds to wait after writing before returning output.")] int? yieldTimeMs = null,
        [Description("Maximum output characters to return.")] int? maxOutputChars = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (RequiresStdinAuthorization(input)
                && _backgroundTerminals.GetStdinSession(sessionId) is { } stdinSession)
            {
                var stdinGate = await _gate.AuthorizeStdinAsync(stdinSession, input, cancellationToken);
                if (!stdinGate.IsAllowed)
                    return stdinGate.Error!;
            }

            var snapshot = await _backgroundTerminals.WriteStdinAsync(
                sessionId,
                input,
                yieldTimeMs ?? 1000,
                maxOutputChars ?? _maxOutputLength,
                cancellationToken);
            return FormatSnapshot(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"Error writing to background terminal: {ex.Message}";
        }
    }

    private const char EndOfText = '\u0003';

    // Empty input polls for output and a lone end-of-text is an interrupt; neither is a command.
    private static bool RequiresStdinAuthorization(string input) =>
        input.Trim('\r', '\n', EndOfText).Length > 0;

    private async Task<string> ExecWithBackgroundTerminalServiceAsync(
        string command,
        string cwd,
        bool runInBackground,
        int? yieldTimeMs,
        int? maxOutputChars,
        bool interactive,
        ShellIdentity shell,
        CommandExecutionTracker? commandExecution,
        CancellationToken cancellationToken)
    {
        try
        {
            var terminals = _backgroundTerminals;
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
                }, cancellationToken);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            commandExecution?.Complete(string.Empty, status: "cancelled", exitCode: null);
            throw;
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
        if (snapshot.Status == BackgroundTerminalStatus.TimedOut)
            return output + Environment.NewLine + "Error: Command timed out.";
        if (snapshot.ExitCode is { } exitCode and not 0)
            return output + Environment.NewLine + $"Exit code: {exitCode}";
        return output;
    }
}
