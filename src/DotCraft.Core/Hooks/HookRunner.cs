using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotCraft.Diagnostics;

namespace DotCraft.Hooks;

/// <summary>
/// Result returned by a hook execution.
/// </summary>
public sealed class HookResult
{
    /// <summary>
    /// Whether the hook blocked the action (exit code 2).
    /// </summary>
    public bool Blocked { get; set; }

    /// <summary>
    /// Reason for blocking (stderr content when exit code is 2).
    /// </summary>
    public string? BlockReason { get; set; }

    /// <summary>
    /// Standard output from the hook command.
    /// For SessionStart hooks, this is injected as additional context.
    /// </summary>
    public string? Output { get; set; }

    /// <summary>
    /// Standard error output from the hook command.
    /// </summary>
    public string? StdErr { get; set; }

    /// <summary>
    /// Exit code of the hook process.
    /// </summary>
    public int ExitCode { get; set; }
}

/// <summary>
/// Input data passed to hook commands as JSON on stdin.
/// </summary>
public sealed class HookInput
{
    public string? SessionId { get; set; }
    public string? ToolName { get; set; }
    public object? ToolArgs { get; set; }
    public string? ToolResult { get; set; }
    public string? Prompt { get; set; }
    public string? Response { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Core hook execution engine.
/// Finds matching hooks for an event, spawns shell processes,
/// passes JSON context via stdin, and interprets exit codes.
/// <para>
/// Exit code semantics:
/// <list type="bullet">
///   <item>0 = success, continue</item>
///   <item>2 = block the action (PreToolUse / PrePrompt only)</item>
///   <item>other = warning logged, continue (fail-open)</item>
/// </list>
/// </para>
/// </summary>
public sealed class HookRunner
{
    private readonly HooksFileConfig _config;
    private readonly string _workspacePath;
    private Action<string>? _debugLogger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Optional logger for debug/warning output. When null, falls back to Console.Error.WriteLine.
    /// Set this in the app layer to route output through the rendering pipeline (e.g. AgentRenderer)
    /// and avoid corrupting live Status spinners with direct stderr writes.
    /// </summary>
    public Action<string>? DebugLogger
    {
        get => _debugLogger;
        set => _debugLogger = value;
    }

    private void WriteDebug(string message) =>
        (_debugLogger ?? Console.Error.WriteLine).Invoke(message);

    public HookRunner(HooksFileConfig config, string workspacePath)
    {
        _config = config;
        _workspacePath = workspacePath;

        if (DebugModeService.IsEnabled())
        {
            var eventNames = string.Join(", ", config.Hooks.Keys);
            Console.Error.WriteLine($"[Hooks] Loaded {config.Hooks.Count} event(s): {eventNames}");
            Console.Error.WriteLine($"[Hooks] HasToolHooks={HasToolHooks}, WorkspacePath={workspacePath}");
        }
    }

    /// <summary>
    /// Whether any PreToolUse, PostToolUse, or PostToolUseFailure hooks are configured.
    /// Used to decide whether to wrap tools with <see cref="HookWrappedFunction"/>.
    /// </summary>
    public bool HasToolHooks =>
        _config.Hooks.ContainsKey(nameof(HookEvent.PreToolUse)) ||
        _config.Hooks.ContainsKey(nameof(HookEvent.PostToolUse)) ||
        _config.Hooks.ContainsKey(nameof(HookEvent.PostToolUseFailure));

    /// <summary>
    /// Whether any hooks are configured at all.
    /// </summary>
    public bool HasAnyHooks => _config.Hooks.Count > 0;

    /// <summary>
    /// Runs all matching hooks for the given event.
    /// For tool-related events, <see cref="HookInput.ToolName"/> is used to match against
    /// the matcher regex in each <see cref="HookMatcherGroup"/>.
    /// </summary>
    /// <returns>
    /// A <see cref="HookResult"/> representing the aggregate outcome.
    /// If any hook returns exit code 2, <see cref="HookResult.Blocked"/> is true.
    /// </returns>
    public async Task<HookResult> RunAsync(HookEvent evt, HookInput input, CancellationToken ct)
    {
        var eventName = evt.ToString();
        if (!_config.Hooks.TryGetValue(eventName, out var matcherGroups))
            return new HookResult();

        var isDebug = DebugModeService.IsEnabled();
        var aggregateResult = new HookResult();
        var outputParts = new List<string>();

        foreach (var group in matcherGroups)
        {
            // Check matcher against tool name (for tool events)
            if (!MatchesTool(group.Matcher, input.ToolName))
            {
                if (isDebug)
                    WriteDebug($"[Hooks] {eventName}: matcher '{group.Matcher}' did not match tool '{input.ToolName}', skipping");
                continue;
            }

            foreach (var hookEntry in group.Hooks)
            {
                if (hookEntry.Type != "command")
                    continue;

                if (isDebug)
                    WriteDebug($"[Hooks] {eventName}: executing '{hookEntry.Command}' (matcher='{group.Matcher}', tool='{input.ToolName}')");

                var result = await ExecuteHookCommandAsync(hookEntry, input, ct);

                if (isDebug)
                    WriteDebug($"[Hooks] {eventName}: exit={result.ExitCode}, blocked={result.Blocked}, stderr='{result.StdErr}'");

                // Log warnings for non-zero, non-2 exit codes (fail-open)
                if (result.ExitCode != 0 && result.ExitCode != 2)
                    WriteDebug($"[Hooks] Warning: hook '{hookEntry.Command}' exited with code {result.ExitCode}: {result.StdErr}");

                if (!string.IsNullOrWhiteSpace(result.Output))
                    outputParts.Add(result.Output);

                if (result.Blocked)
                {
                    aggregateResult.Blocked = true;
                    aggregateResult.BlockReason = result.BlockReason;
                    aggregateResult.ExitCode = 2;
                    aggregateResult.Output = string.Join("\n", outputParts);
                    return aggregateResult; // Stop processing remaining hooks
                }
            }
        }

        aggregateResult.Output = outputParts.Count > 0 ? string.Join("\n", outputParts) : null;
        return aggregateResult;
    }

    /// <summary>
    /// Checks if a tool name matches the given matcher regex.
    /// Empty matcher matches everything.
    /// </summary>
    private static bool MatchesTool(string matcher, string? toolName)
    {
        if (string.IsNullOrEmpty(matcher))
            return true; // Empty matcher matches all

        if (string.IsNullOrEmpty(toolName))
            return true; // Non-tool events always match

        try
        {
            return Regex.IsMatch(toolName, matcher, RegexOptions.IgnoreCase);
        }
        catch (RegexParseException)
        {
            // Invalid regex — treat as no match
            return false;
        }
    }

    /// <summary>
    /// Executes a single hook command as a shell process.
    /// </summary>
    private async Task<HookResult> ExecuteHookCommandAsync(
        HookEntry hookEntry, HookInput input, CancellationToken ct)
    {
        var result = new HookResult();
        string? tempScript = null;

        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var stdinJson = JsonSerializer.Serialize(input, JsonOptions);

            var psi = new ProcessStartInfo
            {
                WorkingDirectory = _workspacePath,
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
                // PowerShell's -Command parameter normalizes all non-zero exit codes to 1,
                // losing the specific exit code (e.g., exit 2 for blocking).
                // Using -File with a wrapper script preserves the actual exit code.
                tempScript = Path.Combine(Path.GetTempPath(), $"dotcraft_hook_{Guid.NewGuid():N}.ps1");
                File.WriteAllText(tempScript, hookEntry.Command, Encoding.UTF8);
                psi.FileName = "powershell.exe";
                psi.Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tempScript}\"";
            }
            else
            {
                psi.FileName = "/bin/bash";
                psi.Arguments = $"-c {EscapeShellArg(hookEntry.Command)}";
            }

            using var process = Process.Start(psi);
            if (process == null)
            {
                result.StdErr = "Failed to start hook process";
                WriteDebug($"[Hooks] Error: failed to start process for '{hookEntry.Command}'");
                return result;
            }

            // Write JSON context to stdin
            await process.StandardInput.WriteAsync(stdinJson);
            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(hookEntry.Timeout));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout — kill the process
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                result.StdErr = $"Hook timed out after {hookEntry.Timeout} seconds";
                return result;
            }

            var stdout = (await stdoutTask).TrimEnd();
            var stderr = (await stderrTask).TrimEnd();

            result.Output = string.IsNullOrEmpty(stdout) ? null : stdout;
            result.StdErr = string.IsNullOrEmpty(stderr) ? null : stderr;
            result.ExitCode = process.ExitCode;

            if (process.ExitCode == 2)
            {
                result.Blocked = true;
                result.BlockReason = !string.IsNullOrWhiteSpace(stderr) ? stderr : "Blocked by hook";
            }
        }
        catch (OperationCanceledException)
        {
            throw; // Propagate cancellation
        }
        catch (Exception ex)
        {
            // Fail-open: log error but don't block
            result.StdErr = $"Hook execution error: {ex.Message}";
            WriteDebug($"[Hooks] Error executing '{hookEntry.Command}': {ex.Message}");
        }
        finally
        {
            // Clean up temporary wrapper script
            if (tempScript != null)
            {
                try { File.Delete(tempScript); } catch { /* best effort */ }
            }
        }

        return result;
    }

    /// <summary>
    /// Escapes a string for safe use as a single shell argument.
    /// </summary>
    private static string EscapeShellArg(string arg)
    {
        return "'" + arg.Replace("'", "'\\''") + "'";
    }
}
