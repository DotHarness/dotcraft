using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotCraft.Diagnostics;
using DotCraft.Utilities;

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
    /// Parsed model-visible additional context.
    /// </summary>
    public string? AdditionalContext { get; set; }

    /// <summary>
    /// Parsed hook-origin continuation feedback.
    /// </summary>
    public string? ContinuationPrompt { get; set; }

    /// <summary>
    /// Optional user-visible system message emitted by the hook.
    /// </summary>
    public string? SystemMessage { get; set; }

    /// <summary>
    /// Optional per-run rewake summary.
    /// </summary>
    public string? RewakeSummary { get; set; }

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
    public string? TurnId { get; set; }
    public string? Cwd { get; set; }
    public string? HookEventName { get; set; }
    public string? ToolName { get; set; }
    public object? ToolArgs { get; set; }
    public object? ToolInput { get; set; }
    public string? ToolResult { get; set; }
    public string? ToolResponse { get; set; }
    public string? Prompt { get; set; }
    public string? RuntimeContext { get; set; }
    public string? Response { get; set; }
    public string? LastAssistantMessage { get; set; }
    public string? Error { get; set; }
    public bool StopHookActive { get; set; }
    public string? Source { get; set; }
    public string? PluginId { get; set; }
    public string? Model { get; set; }
    public string? PermissionMode { get; set; }
    public string? TranscriptPath { get; set; }
}

/// <summary>
/// Request emitted when an async rewake hook asks DotCraft to continue a thread.
/// </summary>
public sealed class HookRewakeRequest
{
    public string ThreadId { get; init; } = string.Empty;

    public string? TurnId { get; init; }

    public string HookKey { get; init; } = string.Empty;

    public string EventName { get; init; } = string.Empty;

    public string Prompt { get; init; } = string.Empty;

    public string? Summary { get; init; }
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
    private HookDiscoveryResult _snapshot;
    private readonly string _workspacePath;
    private Action<string>? _debugLogger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly Encoding PowerShellScriptEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    private static readonly Regex BareBashInvocationRegex = new(
        "(^|[\\r\\n;|&])\\s*[\"']?bash(?:\\.exe)?[\"']?(?=\\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private const string MissingBashMessage = "Hook requires bash, but bash.exe was not found. Install Git for Windows or add bash.exe to PATH.";

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

    /// <summary>
    /// Optional callback used by async rewake hooks to enqueue thread feedback.
    /// </summary>
    public Func<HookRewakeRequest, CancellationToken, Task>? RewakeHandler { get; set; }

    public HookRunner(HooksFileConfig config, string workspacePath)
        : this(new HookDiscoveryResult { RuntimeConfig = config }, workspacePath)
    {
    }

    public HookRunner(HookDiscoveryResult discovery, string workspacePath)
    {
        _snapshot = discovery;
        _workspacePath = workspacePath;

        if (DebugModeService.IsEnabled())
        {
            var eventNames = string.Join(", ", discovery.RuntimeConfig.Hooks.Keys);
            Console.Error.WriteLine($"[Hooks] Loaded {discovery.RuntimeConfig.Hooks.Count} event(s): {eventNames}");
            Console.Error.WriteLine($"[Hooks] HasToolHooks={HasToolHooks}, WorkspacePath={workspacePath}");
        }
    }

    /// <summary>
    /// Replaces the current hook snapshot after config/plugin state changes.
    /// </summary>
    public void ReplaceSnapshot(HookDiscoveryResult discovery)
    {
        Volatile.Write(ref _snapshot, discovery);
        if (DebugModeService.IsEnabled())
        {
            var eventNames = string.Join(", ", discovery.RuntimeConfig.Hooks.Keys);
            WriteDebug($"[Hooks] Snapshot refreshed: {discovery.RuntimeConfig.Hooks.Count} event(s): {eventNames}");
        }
    }

    /// <summary>
    /// Client-visible metadata for the current snapshot.
    /// </summary>
    public IReadOnlyList<HookMetadata> Hooks => Volatile.Read(ref _snapshot).Hooks;

    public IReadOnlyList<string> Warnings => Volatile.Read(ref _snapshot).Warnings;

    public IReadOnlyList<HookErrorInfo> Errors => Volatile.Read(ref _snapshot).Errors;

    /// <summary>
    /// Whether any PreToolUse, PostToolUse, or PostToolUseFailure hooks are configured.
    /// Used to decide whether to wrap tools with <see cref="HookWrappedFunction"/>.
    /// </summary>
    public bool HasToolHooks
    {
        get
        {
            var hooks = Volatile.Read(ref _snapshot).RuntimeConfig.Hooks;
            return hooks.ContainsKey(nameof(HookEvent.PreToolUse)) ||
                   hooks.ContainsKey(nameof(HookEvent.PostToolUse)) ||
                   hooks.ContainsKey(nameof(HookEvent.PostToolUseFailure));
        }
    }

    /// <summary>
    /// Whether any hooks are configured at all.
    /// </summary>
    public bool HasAnyHooks => Volatile.Read(ref _snapshot).RuntimeConfig.Hooks.Count > 0;

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
        var snapshot = Volatile.Read(ref _snapshot);
        if (!snapshot.RuntimeConfig.Hooks.TryGetValue(eventName, out var matcherGroups))
            return new HookResult();

        var isDebug = DebugModeService.IsEnabled();
        var canBlock = evt is HookEvent.PreToolUse or HookEvent.PrePrompt or HookEvent.UserPromptSubmit or HookEvent.PermissionRequest or HookEvent.PreCompact;
        var aggregateResult = new HookResult();
        var outputParts = new List<string>();
        var contextParts = new List<string>();
        var blockReasonParts = new List<string>();
        var tool = HookToolAdapter.Build(input.ToolName, input.ToolArgs);

        foreach (var group in matcherGroups)
        {
            if (!HookToolAdapter.MatchesMatcher(group.Matcher, tool))
            {
                if (isDebug)
                    WriteDebug($"[Hooks] {eventName}: matcher '{group.Matcher}' did not match tool '{input.ToolName}', skipping");
                continue;
            }

            foreach (var hookEntry in group.Hooks)
            {
                if (!string.Equals(hookEntry.Type, "command", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!HookToolAdapter.MatchesCondition(hookEntry.If, tool))
                {
                    if (isDebug)
                        WriteDebug($"[Hooks] {eventName}: condition '{hookEntry.If}' did not match tool '{input.ToolName}', skipping");
                    continue;
                }

                if (isDebug)
                    WriteDebug($"[Hooks] {eventName}: executing '{hookEntry.Command}' (matcher='{group.Matcher}', tool='{input.ToolName}')");

                if (hookEntry.Async || hookEntry.AsyncRewake)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var asyncInput = PrepareInput(evt, input, hookEntry, tool);
                            var asyncResult = await ExecuteHookCommandAsync(evt, hookEntry, asyncInput, tool, CancellationToken.None).ConfigureAwait(false);
                            await DispatchRewakeIfNeededAsync(evt, hookEntry, asyncInput, asyncResult, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            WriteDebug($"[Hooks] Error executing async hook '{hookEntry.Command}': {ex.Message}");
                        }
                    }, CancellationToken.None);
                    continue;
                }

                var preparedInput = PrepareInput(evt, input, hookEntry, tool);
                var result = await ExecuteHookCommandAsync(evt, hookEntry, preparedInput, tool, ct);

                if (isDebug)
                    WriteDebug($"[Hooks] {eventName}: exit={result.ExitCode}, blocked={result.Blocked}, stderr='{result.StdErr}'");

                // Log warnings for non-zero, non-2 exit codes (fail-open)
                if (result.ExitCode != 0 && result.ExitCode != 2)
                    WriteDebug($"[Hooks] Warning: hook '{hookEntry.Command}' exited with code {result.ExitCode}: {result.StdErr}");

                if (!string.IsNullOrWhiteSpace(result.Output))
                    outputParts.Add(result.Output);

                if (!string.IsNullOrWhiteSpace(result.AdditionalContext))
                    contextParts.Add(result.AdditionalContext);

                if (!string.IsNullOrWhiteSpace(result.SystemMessage))
                    aggregateResult.SystemMessage = result.SystemMessage;

                if (!string.IsNullOrWhiteSpace(result.RewakeSummary))
                    aggregateResult.RewakeSummary = result.RewakeSummary;

                await DispatchRewakeIfNeededAsync(evt, hookEntry, preparedInput, result, ct).ConfigureAwait(false);

                if (result.Blocked && !string.IsNullOrWhiteSpace(result.BlockReason))
                    blockReasonParts.Add(result.BlockReason);

                if (result.Blocked && canBlock)
                {
                    aggregateResult.Blocked = true;
                    aggregateResult.BlockReason = result.BlockReason;
                    aggregateResult.ExitCode = 2;
                    aggregateResult.Output = string.Join("\n", outputParts);
                    aggregateResult.AdditionalContext = contextParts.Count > 0 ? string.Join("\n", contextParts) : null;
                    return aggregateResult; // Stop processing remaining hooks
                }
            }
        }

        aggregateResult.Output = outputParts.Count > 0 ? string.Join("\n", outputParts) : null;
        aggregateResult.AdditionalContext = contextParts.Count > 0 ? string.Join("\n", contextParts) : null;
        if (blockReasonParts.Count > 0)
        {
            aggregateResult.BlockReason = string.Join("\n", blockReasonParts);
            aggregateResult.ExitCode = 2;
        }
        return aggregateResult;
    }

    /// <summary>
    /// Executes a single hook command as a shell process.
    /// </summary>
    private async Task<HookResult> ExecuteHookCommandAsync(
        HookEvent evt, HookEntry hookEntry, HookInput input, HookToolView tool, CancellationToken ct)
    {
        var result = new HookResult();
        string? tempScript = null;

        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var stdinJson = JsonSerializer.Serialize(BuildPayload(evt, input, hookEntry, tool, _workspacePath), JsonOptions);

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
            psi.EnvironmentVariables["DOTCRAFT_WORKSPACE_ROOT"] = _workspacePath;
            psi.EnvironmentVariables["CLAUDE_PROJECT_DIR"] = _workspacePath;
            foreach (var (key, value) in hookEntry.EnvironmentVariables)
                psi.EnvironmentVariables[key] = value;

            if (isWindows && CommandReferencesBareBash(hookEntry.Command) && !TryConfigureWindowsBashPath(psi))
            {
                result.ExitCode = 127;
                result.StdErr = MissingBashMessage;
                WriteDebug($"[Hooks] Error executing '{hookEntry.Command}': {MissingBashMessage}");
                return result;
            }

            if (!string.IsNullOrWhiteSpace(hookEntry.Shell))
            {
                ConfigureShellOverride(psi, hookEntry.Shell!, hookEntry.Command, isWindows);
            }
            else if (isWindows)
            {
                // PowerShell's -Command parameter normalizes all non-zero exit codes to 1,
                // losing the specific exit code (e.g., exit 2 for blocking).
                // Using -File with a wrapper script preserves the actual exit code.
                tempScript = Path.Combine(Path.GetTempPath(), $"dotcraft_hook_{Guid.NewGuid():N}.ps1");
                File.WriteAllText(tempScript, BuildWindowsPowerShellScript(hookEntry.Command), PowerShellScriptEncoding);
                psi.FileName = "powershell.exe";
                psi.Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -OutputFormat Text -File \"{tempScript}\"";
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
            var stderr = isWindows
                ? PowerShellStderrSanitizer.Sanitize((await stderrTask).TrimEnd())
                : (await stderrTask).TrimEnd();

            result.Output = string.IsNullOrEmpty(stdout) ? null : stdout;
            result.StdErr = string.IsNullOrEmpty(stderr) ? null : stderr;
            result.ExitCode = process.ExitCode;

            var parsed = HookOutputParser.Parse(stdout, stderr, evt, hookEntry.AsyncRewake);
            result.AdditionalContext = parsed.AdditionalContext;
            result.SystemMessage = parsed.SystemMessage;
            result.RewakeSummary = parsed.RewakeSummary;

            if (process.ExitCode == 2)
            {
                result.Blocked = true;
                result.BlockReason = parsed.BlockReason
                    ?? (!string.IsNullOrWhiteSpace(stderr) ? stderr : "Blocked by hook");
            }
            else if (!string.IsNullOrWhiteSpace(parsed.BlockReason))
            {
                result.Blocked = true;
                result.BlockReason = parsed.BlockReason;
                result.ExitCode = 2;
            }

            if (hookEntry.AsyncRewake
                && parsed.Continue
                && (result.Blocked
                    || !string.IsNullOrWhiteSpace(parsed.BlockReason)
                    || !string.IsNullOrWhiteSpace(parsed.AdditionalContext)))
            {
                result.ContinuationPrompt = BuildContinuationPrompt(
                    hookEntry,
                    FirstNonWhiteSpace(parsed.BlockReason, stderr, parsed.AdditionalContext, stdout),
                    parsed.RewakeSummary);
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

    private static HookInput PrepareInput(HookEvent evt, HookInput input, HookEntry hookEntry, HookToolView tool)
    {
        return new HookInput
        {
            SessionId = input.SessionId,
            TurnId = input.TurnId,
            Cwd = string.IsNullOrWhiteSpace(input.Cwd) ? null : input.Cwd,
            HookEventName = evt.ToString(),
            ToolName = tool.HookName ?? input.ToolName,
            ToolArgs = input.ToolArgs,
            ToolInput = tool.ToolInput,
            ToolResult = input.ToolResult,
            ToolResponse = input.ToolResponse ?? input.ToolResult,
            Prompt = input.Prompt,
            RuntimeContext = input.RuntimeContext,
            Response = input.Response,
            LastAssistantMessage = input.LastAssistantMessage ?? input.Response,
            Error = input.Error,
            StopHookActive = input.StopHookActive,
            Source = hookEntry.Source,
            PluginId = hookEntry.PluginId,
            Model = input.Model,
            PermissionMode = input.PermissionMode,
            TranscriptPath = input.TranscriptPath
        };
    }

    private async Task DispatchRewakeIfNeededAsync(
        HookEvent evt,
        HookEntry hookEntry,
        HookInput input,
        HookResult result,
        CancellationToken ct)
    {
        if (!hookEntry.AsyncRewake || RewakeHandler == null)
            return;

        if (evt == HookEvent.Stop && input.StopHookActive)
            return;

        if (string.IsNullOrWhiteSpace(result.ContinuationPrompt) || string.IsNullOrWhiteSpace(input.SessionId))
            return;

        await RewakeHandler(
            new HookRewakeRequest
            {
                ThreadId = input.SessionId!,
                TurnId = input.TurnId,
                HookKey = hookEntry.Key,
                EventName = evt.ToString(),
                Prompt = result.ContinuationPrompt!,
                Summary = result.RewakeSummary ?? hookEntry.RewakeSummary
            },
            ct).ConfigureAwait(false);
    }

    private static string BuildContinuationPrompt(HookEntry hookEntry, string? feedback, string? rewakeSummary)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(hookEntry.RewakeMessage))
            parts.Add(hookEntry.RewakeMessage!.Trim());
        if (!string.IsNullOrWhiteSpace(feedback))
            parts.Add(feedback!.Trim());
        if (parts.Count == 0 && !string.IsNullOrWhiteSpace(rewakeSummary))
            parts.Add(rewakeSummary!.Trim());
        return string.Join("\n\n", parts);
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static Dictionary<string, object?> BuildPayload(HookEvent evt, HookInput input, HookEntry hookEntry, HookToolView tool, string workspacePath)
    {
        var sessionId = input.SessionId;
        var turnId = input.TurnId;
        var eventName = evt.ToString();
        var cwd = string.IsNullOrWhiteSpace(input.Cwd) ? workspacePath : input.Cwd;
        var toolInput = input.ToolInput ?? tool.ToolInput;
        var toolResult = input.ToolResult;
        var toolResponse = input.ToolResponse ?? toolResult;
        var lastAssistantMessage = input.LastAssistantMessage ?? input.Response;

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["sessionId"] = sessionId,
            ["session_id"] = sessionId,
            ["turnId"] = turnId,
            ["turn_id"] = turnId,
            ["cwd"] = cwd,
            ["hookEventName"] = eventName,
            ["hook_event_name"] = eventName,
            ["source"] = hookEntry.Source,
            ["pluginId"] = hookEntry.PluginId,
            ["plugin_id"] = hookEntry.PluginId,
            ["transcriptPath"] = input.TranscriptPath,
            ["transcript_path"] = input.TranscriptPath,
            ["model"] = input.Model,
            ["permissionMode"] = input.PermissionMode,
            ["permission_mode"] = input.PermissionMode,
            ["prompt"] = input.Prompt,
            ["runtimeContext"] = input.RuntimeContext,
            ["runtime_context"] = input.RuntimeContext,
            ["toolName"] = input.ToolName,
            ["tool_name"] = input.ToolName,
            ["toolArgs"] = input.ToolArgs,
            ["tool_args"] = input.ToolArgs,
            ["toolInput"] = toolInput,
            ["tool_input"] = toolInput,
            ["toolResult"] = toolResult,
            ["tool_result"] = toolResult,
            ["toolResponse"] = toolResponse,
            ["tool_response"] = toolResponse,
            ["response"] = input.Response,
            ["lastAssistantMessage"] = lastAssistantMessage,
            ["last_assistant_message"] = lastAssistantMessage,
            ["error"] = input.Error,
            ["stopHookActive"] = input.StopHookActive,
            ["stop_hook_active"] = input.StopHookActive
        };

        return payload
            .Where(kvp => kvp.Value != null)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);
    }

    private static void ConfigureShellOverride(ProcessStartInfo psi, string shell, string command, bool isWindows)
    {
        if (isWindows && (string.Equals(shell, "powershell", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(shell, "powershell.exe", StringComparison.OrdinalIgnoreCase)))
        {
            psi.FileName = "powershell.exe";
            psi.Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command {EscapeShellArg(command)}";
            return;
        }

        if (isWindows && (string.Equals(shell, "cmd", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(shell, "cmd.exe", StringComparison.OrdinalIgnoreCase)))
        {
            psi.FileName = "cmd.exe";
            psi.Arguments = $"/d /s /c \"{command.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
            return;
        }

        psi.FileName = shell;
        psi.Arguments = $"-c {EscapeShellArg(command)}";
    }

    /// <summary>
    /// Escapes a string for safe use as a single shell argument.
    /// </summary>
    private static string EscapeShellArg(string arg)
    {
        return "'" + arg.Replace("'", "'\\''") + "'";
    }

    private static string BuildWindowsPowerShellScript(string command) =>
        "$ProgressPreference = 'SilentlyContinue'\n" +
        "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n" +
        "$OutputEncoding = [System.Text.Encoding]::UTF8\n" +
        command;

    internal static bool CommandReferencesBareBash(string command) =>
        !string.IsNullOrWhiteSpace(command) && BareBashInvocationRegex.IsMatch(command);

    internal static bool TryConfigureWindowsBashPath(ProcessStartInfo psi)
    {
        if (FindExecutableOnPath("bash.exe", GetPathEnvironment(psi)) != null)
            return true;

        var gitBashDir = GetWindowsGitBashCandidateDirectories()
            .FirstOrDefault(dir => File.Exists(Path.Combine(dir, "bash.exe")));
        if (gitBashDir == null)
            return false;

        var currentPath = GetPathEnvironment(psi);
        psi.EnvironmentVariables["PATH"] = string.IsNullOrWhiteSpace(currentPath)
            ? gitBashDir
            : gitBashDir + Path.PathSeparator + currentPath;
        return true;
    }

    private static string? GetPathEnvironment(ProcessStartInfo psi) =>
        psi.EnvironmentVariables["PATH"] ??
        psi.EnvironmentVariables["Path"] ??
        Environment.GetEnvironmentVariable("PATH");

    private static string? FindExecutableOnPath(string executableName, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var rawDir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var dir = rawDir.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            var candidate = Path.Combine(dir, executableName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> GetWindowsGitBashCandidateDirectories()
    {
        yield return @"C:\Program Files\Git\bin";
        yield return @"C:\Program Files\Git\usr\bin";
        yield return @"C:\Program Files (x86)\Git\bin";
        yield return @"C:\Program Files (x86)\Git\usr\bin";
    }
}
