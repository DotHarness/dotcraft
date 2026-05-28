using System.ComponentModel;
using System.Text;
using DotCraft.Protocol;

namespace DotCraft.Tools.Sandbox;

/// <summary>
/// Shell command execution inside an OpenSandbox container.
/// Commands run in complete isolation from the host machine — no regex guards,
/// path blacklists, or approval flows are needed because the container boundary
/// is the security boundary.
/// </summary>
public sealed class SandboxShellTools
{
    private readonly SandboxSessionManager _sandboxManager;
    private readonly int _timeoutSeconds;
    private readonly int _maxOutputLength;

    public SandboxShellTools(
        SandboxSessionManager sandboxManager,
        int timeoutSeconds = 300,
        int maxOutputLength = 10000)
    {
        _sandboxManager = sandboxManager;
        _timeoutSeconds = timeoutSeconds;
        _maxOutputLength = maxOutputLength;
    }

    [Description("Execute a shell command and return its output.")]
    [Tool(Icon = "⌨️", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.Exec), MaxResultChars = 30_000)]
    public async Task<string> Exec(
        [Description("The shell command to execute.")] string command,
        [Description("Optional working directory inside the sandbox.")] string? workingDir = null)
    {
        if (string.IsNullOrWhiteSpace(command))
            return "Error: Command cannot be empty.";

        CommandExecutionTracker? commandExecution = null;
        try
        {
            commandExecution = SandboxCommandExecutionTracker.Begin(
                command,
                !string.IsNullOrWhiteSpace(workingDir) ? workingDir! : "/workspace",
                source: "sandbox");
            var sandbox = await _sandboxManager.GetOrCreateAsync();

            // If a working directory is specified, wrap the command with cd
            var effectiveCommand = !string.IsNullOrWhiteSpace(workingDir)
                ? $"cd {EscapeShellArg(workingDir)} && {command}"
                : $"cd /workspace && {command}";

            var execution = await sandbox.Commands.RunAsync(
                effectiveCommand,
                options: new OpenSandbox.Models.RunCommandOptions
                {
                    TimeoutSeconds = _timeoutSeconds
                });

            var output = FormatOutput(execution);
            commandExecution?.Append(output);
            commandExecution?.Complete(
                output,
                status: execution.Error == null ? "completed" : "failed",
                exitCode: execution.Error == null ? 0 : 1);
            return output;
        }
        catch (OpenSandbox.Core.SandboxException ex)
        {
            var error = $"Sandbox error: [{ex.Error.Code}] {ex.Error.Message}";
            commandExecution?.Complete(error, status: "failed", exitCode: null);
            return error;
        }
        catch (Exception ex)
        {
            var error = $"Error executing command in sandbox: {ex.Message}";
            commandExecution?.Complete(error, status: "failed", exitCode: null);
            return error;
        }
    }

    private string FormatOutput(OpenSandbox.Models.Execution execution)
    {
        var result = new StringBuilder();

        // Collect stdout
        foreach (var line in execution.Logs.Stdout)
        {
            if (line.Text != null)
                result.AppendLine(line.Text);
        }

        // Collect stderr
        var stderr = new StringBuilder();
        foreach (var line in execution.Logs.Stderr)
        {
            if (line.Text != null)
                stderr.AppendLine(line.Text);
        }

        if (stderr.Length > 0)
        {
            if (result.Length > 0) result.AppendLine();
            result.AppendLine("STDERR:");
            result.Append(stderr);
        }

        // Execution.Error indicates a failed command
        if (execution.Error != null)
        {
            if (result.Length > 0) result.AppendLine();
            result.AppendLine($"Error: {execution.Error.Name}: {execution.Error.Value}");
        }

        var output = result.Length > 0 ? result.ToString().TrimEnd() : "(no output)";

        if (output.Length > _maxOutputLength)
        {
            output = output[.._maxOutputLength]
                     + $"\n... (truncated, {output.Length - _maxOutputLength} more chars)";
        }

        return output;
    }

    private static string EscapeShellArg(string arg)
    {
        return "'" + arg.Replace("'", "'\\''") + "'";
    }
}

file static class SandboxCommandExecutionTracker
{
    public static CommandExecutionTracker? Begin(string command, string workingDirectory, string source) =>
        CommandExecutionTracker.Begin(command, workingDirectory, source);
}
