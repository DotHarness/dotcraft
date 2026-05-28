using System.Diagnostics;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotCraft.Configuration;

namespace DotCraft.Agents;

public sealed class CliOneshotRuntime : ISubAgentRuntime
{
    private const int DefaultTimeoutSeconds = 300;
    private const int DefaultMaxOutputBytes = 1024 * 1024;
    private static readonly TimeSpan PipeDrainGracePeriod = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan BinaryProbeCacheTtl = TimeSpan.FromSeconds(60);
    private const string TruncationSuffix = "\n... (output truncated)";
    private static readonly ConcurrentDictionary<string, BinaryProbeCacheEntry> BinaryProbeCache =
        new(StringComparer.Ordinal);

    private static readonly string[] WindowsLaunchableExtensions = [".exe", ".cmd", ".bat", ".com"];
    private static readonly string[] WindowsMinimalEnvironmentKeys =
    [
        "PATH",
        "PATHEXT",
        "SystemRoot",
        "COMSPEC",
        "USERPROFILE",
        "HOME",
        "HOMEDRIVE",
        "HOMEPATH",
        "TEMP",
        "TMP",
        "APPDATA",
        "LOCALAPPDATA",
        "ProgramData",
        "CURSOR_API_KEY",
        "ANTHROPIC_API_KEY",
        "CODEX_API_KEY",
        "OPENAI_API_KEY",
        "OPENAI_BASE_URL",
        "NO_COLOR",
        "FORCE_COLOR"
    ];
    private static readonly string[] UnixMinimalEnvironmentKeys =
    [
        "PATH",
        "HOME",
        "SHELL",
        "TMPDIR",
        "LANG",
        "LC_ALL",
        "TERM",
        "CURSOR_API_KEY",
        "ANTHROPIC_API_KEY",
        "CODEX_API_KEY",
        "OPENAI_API_KEY",
        "OPENAI_BASE_URL",
        "NO_COLOR",
        "FORCE_COLOR"
    ];

    public const string RuntimeTypeName = "cli-oneshot";

    public string RuntimeType => RuntimeTypeName;

    public Task<SubAgentSessionHandle> CreateSessionAsync(
        SubAgentProfile profile,
        SubAgentLaunchContext context,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new SubAgentSessionHandle(
            RuntimeType,
            profile.Name,
            new CliOneshotSessionState(profile, context.ExtraLaunchArgs, context.ResumeSessionId)));
    }

    public async Task<SubAgentRunResult> RunAsync(
        SubAgentSessionHandle session,
        SubAgentTaskRequest request,
        ISubAgentEventSink sink,
        CancellationToken cancellationToken)
    {
        if (session.State is not CliOneshotSessionState state)
            return Error("Invalid CLI oneshot runtime session state.");

        var profile = state.Profile;
        var workingDirectory = request.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            sink.OnFailed($"Subagent profile '{profile.Name}' is missing a working directory.");
            return Error($"Subagent profile '{profile.Name}' is missing a working directory.");
        }

        if (!Directory.Exists(workingDirectory))
        {
            sink.OnFailed($"Subagent working directory '{workingDirectory}' does not exist.");
            return Error($"Subagent working directory '{workingDirectory}' does not exist.");
        }

        if (string.IsNullOrWhiteSpace(profile.Bin))
        {
            sink.OnFailed($"Subagent profile '{profile.Name}' is missing required field 'bin'.");
            return Error($"Subagent profile '{profile.Name}' is missing required field 'bin'.");
        }

        string resolvedBinary;
        try
        {
            resolvedBinary = ResolveExecutablePath(profile.Bin);
        }
        catch (Exception ex)
        {
            var message = $"{ex.Message} Profile='{profile.Name}'.";
            sink.OnFailed(message);
            return Error(message);
        }

        string? outputFilePath = null;
        try
        {
            var invocation = BuildInvocation(
                profile,
                request.Task,
                state.ExtraLaunchArgs,
                state.ResumeSessionId,
                ref outputFilePath);
            sink.OnProgress(
                "external-cli",
                $"Launching {Path.GetFileName(resolvedBinary)} ({profile.Name}) in {workingDirectory} via {invocation.InputMode}.");
            var psi = new ProcessStartInfo
            {
                FileName = resolvedBinary,
                WorkingDirectory = workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (var argument in invocation.Arguments)
                psi.ArgumentList.Add(argument);

            ConfigureEnvironment(psi, profile, request.Task);

            using var process = Process.Start(psi);
            if (process == null)
            {
                var message =
                    $"Failed to start external subagent binary '{resolvedBinary}'. Profile='{profile.Name}'.";
                sink.OnFailed(message);
                return Error(message);
            }

            state.Attach(process);
            sink.OnProgress(
                "external-cli",
                $"Running {Path.GetFileName(resolvedBinary)} ({profile.Name}) in {workingDirectory}.");

            var timeout = GetTimeout(profile);
            using var executionCts = timeout.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;
            if (timeout.HasValue)
                executionCts!.CancelAfter(timeout.Value);
            var executionToken = executionCts?.Token ?? cancellationToken;

            Task? stdinTask = null;
            if (invocation.WriteTaskToStdin)
            {
                sink.OnProgress("external-cli", $"Sending task over stdin to {Path.GetFileName(resolvedBinary)}.");
                stdinTask = WriteTaskToStdinAsync(process, request.Task, executionToken);
            }
            else
            {
                process.StandardInput.Close();
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(executionToken);
            var stderrTask = process.StandardError.ReadToEndAsync(executionToken);
            var waitTask = process.WaitForExitAsync(executionToken);

            try
            {
                sink.OnProgress("external-cli", $"Waiting for {Path.GetFileName(resolvedBinary)} to finish.");
                await waitTask;

                if (stdinTask != null)
                    await stdinTask;

                var outputDrainTask = Task.WhenAll(stdoutTask, stderrTask);
                var outputDrained = await TryWaitWithTimeoutAsync(
                    outputDrainTask,
                    PipeDrainGracePeriod,
                    cancellationToken);
                if (!outputDrained)
                {
                    state.KillCurrentProcessTree();
                    await AwaitQuietly(process, stdoutTask, stderrTask, PipeDrainGracePeriod);
                    var message =
                        $"External subagent exited but output pipe did not close within grace period; process tree terminated. Profile='{profile.Name}', Binary='{resolvedBinary}'.";
                    sink.OnFailed(message);
                    return Error(message);
                }
            }
            catch (OperationCanceledException)
                when (executionCts?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
            {
                state.KillCurrentProcessTree();
                await AwaitQuietly(process, stdoutTask, stderrTask, PipeDrainGracePeriod);
                var message =
                    $"External subagent timed out after {(int)timeout!.Value.TotalSeconds} seconds. Profile='{profile.Name}', Binary='{resolvedBinary}'.";
                sink.OnFailed(message);
                return Error(message);
            }
            catch (OperationCanceledException)
            {
                state.KillCurrentProcessTree();
                await AwaitQuietly(process, stdoutTask, stderrTask, PipeDrainGracePeriod);
                var message = $"External subagent cancelled. Profile='{profile.Name}', Binary='{resolvedBinary}'.";
                sink.OnFailed(message);
                return Error(message);
            }
            finally
            {
                state.Clear(process);
            }

            var stdout = TryGetCompletedTaskResult(stdoutTask);
            var stderr = TryGetCompletedTaskResult(stderrTask);

            if (process.ExitCode != 0)
            {
                var message = BuildNonZeroExitMessage(
                    process.ExitCode,
                    stdout,
                    stderr,
                    profile.Name,
                    resolvedBinary,
                    profile.MaxOutputBytes);
                sink.OnFailed($"Failed ({profile.Name}, exit {process.ExitCode}).");
                return Error(message);
            }

            var result = await BuildSuccessfulResultAsync(
                profile,
                stdout,
                state.ResumeSessionId,
                outputFilePath,
                cancellationToken,
                sink);
            sink.OnCompleted(
                $"Completed {profile.Name} via {Path.GetFileName(resolvedBinary)}.",
                result.TokensUsed);
            return new SubAgentRunResult
            {
                Text = TruncateToMaxBytes(result.Text, profile.MaxOutputBytes ?? DefaultMaxOutputBytes),
                IsError = false,
                TokensUsed = result.TokensUsed,
                SessionId = result.SessionId
            };
        }
        catch (OperationCanceledException)
        {
            sink.OnFailed($"External subagent cancelled. Profile='{profile.Name}'.");
            return Error($"External subagent cancelled. Profile='{profile.Name}'.");
        }
        catch (Exception ex)
        {
            sink.OnFailed(ex.Message);
            return Error(ex.Message);
        }
        finally
        {
            if (profile.DeleteOutputFileAfterRead == true && !string.IsNullOrWhiteSpace(outputFilePath))
            {
                TryDeleteFile(outputFilePath);
            }
        }
    }

    public Task CancelAsync(SubAgentSessionHandle session, CancellationToken cancellationToken)
    {
        if (session.State is CliOneshotSessionState state)
            state.KillCurrentProcessTree();

        return Task.CompletedTask;
    }

    public Task DisposeSessionAsync(SubAgentSessionHandle session, CancellationToken cancellationToken)
    {
        if (session.State is CliOneshotSessionState state)
            state.KillCurrentProcessTree();

        return Task.CompletedTask;
    }

    private static TimeSpan? GetTimeout(SubAgentProfile profile)
    {
        var timeoutSeconds = profile.Timeout ?? DefaultTimeoutSeconds;
        return timeoutSeconds > 0 ? TimeSpan.FromSeconds(timeoutSeconds) : null;
    }

    private static async Task WriteTaskToStdinAsync(Process process, string task, CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteAsync(task.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
        process.StandardInput.Close();
    }

    private static async Task AwaitQuietly(
        Process process,
        Task<string> stdoutTask,
        Task<string> stderrTask,
        TimeSpan maxWait)
    {
        try
        {
            if (!process.HasExited)
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(maxWait, CancellationToken.None);
        }
        catch
        {
            // ignored
        }

        if (maxWait <= TimeSpan.Zero)
            return;

        try { await stdoutTask.WaitAsync(maxWait, CancellationToken.None); } catch { }
        try { await stderrTask.WaitAsync(maxWait, CancellationToken.None); } catch { }
    }

    private static async Task<bool> TryWaitWithTimeoutAsync(
        Task task,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (task.IsCompleted)
            return true;

        try
        {
            await task.WaitAsync(timeout, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static string TryGetCompletedTaskResult(Task<string> task)
    {
        if (task.IsCompletedSuccessfully)
            return task.Result;

        return string.Empty;
    }

    private static async Task<SubAgentRunResult> BuildSuccessfulResultAsync(
        SubAgentProfile profile,
        string stdout,
        string? resumeSessionId,
        string? outputFilePath,
        CancellationToken cancellationToken,
        ISubAgentEventSink sink)
    {
        var resolvedSessionId = TryExtractSessionId(stdout, profile) ?? resumeSessionId;

        if (profile.ReadOutputFile == true)
        {
            sink.OnProgress("external-cli", "Reading output file.");
            var fileResult = await TryReadOutputFileAsync(outputFilePath, stdout, profile, cancellationToken);
            if (fileResult is { } capturedOutput)
            {
                return new SubAgentRunResult
                {
                    Text = capturedOutput.DisplayText,
                    IsError = false,
                    TokensUsed = TryExtractTokenUsage(capturedOutput.TokenSourceText, profile),
                    SessionId = resolvedSessionId
                };
            }
        }

        if (string.Equals(profile.OutputFormat, "json", StringComparison.OrdinalIgnoreCase))
        {
            sink.OnProgress("external-cli", "Parsing JSON output.");
            return new SubAgentRunResult
            {
                Text = ParseJsonOutputOrFallback(stdout, profile.OutputJsonPath),
                IsError = false,
                TokensUsed = TryExtractTokenUsage(stdout, profile),
                SessionId = resolvedSessionId
            };
        }

        return new SubAgentRunResult
        {
            Text = string.IsNullOrWhiteSpace(stdout) ? "(no output)" : stdout.TrimEnd(),
            IsError = false,
            TokensUsed = TryExtractTokenUsage(stdout, profile),
            SessionId = resolvedSessionId
        };
    }

    private static async Task<OutputReadResult?> TryReadOutputFileAsync(
        string? outputFilePath,
        string stdout,
        SubAgentProfile profile,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(outputFilePath))
        {
            var fallback = BuildWarningWithFallback(
                "Configured output file capture was enabled, but no output file path was generated.",
                stdout);
            return new OutputReadResult(fallback, stdout);
        }

        if (!File.Exists(outputFilePath))
        {
            var fallback = BuildWarningWithFallback(
                $"Expected output file '{outputFilePath}' was not created. Falling back to stdout.",
                stdout);
            return new OutputReadResult(fallback, stdout);
        }

        var content = await File.ReadAllTextAsync(outputFilePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            var fallback = BuildWarningWithFallback(
                $"Output file '{outputFilePath}' was empty. Falling back to stdout.",
                stdout);
            return new OutputReadResult(fallback, stdout);
        }

        if (string.Equals(profile.OutputFormat, "json", StringComparison.OrdinalIgnoreCase))
            return new OutputReadResult(ParseJsonOutputOrFallback(content, profile.OutputJsonPath), content);

        return new OutputReadResult(content.TrimEnd(), content);
    }

    private static string ParseJsonOutputOrFallback(string output, string? jsonPath)
    {
        if (string.IsNullOrWhiteSpace(output))
            return "(no output)";

        if (string.IsNullOrWhiteSpace(jsonPath))
            return Warning("JSON output parsing was requested, but outputJsonPath was not configured.") + "\n" + output.TrimEnd();

        try
        {
            using var doc = JsonDocument.Parse(output);
            JsonElement current = doc.RootElement;
            foreach (var segment in jsonPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                {
                    return Warning($"Failed to extract JSON path '{jsonPath}'. Falling back to raw stdout.")
                           + "\n" + output.TrimEnd();
                }
            }

            return current.ValueKind switch
            {
                JsonValueKind.String => current.GetString() ?? string.Empty,
                JsonValueKind.Null => string.Empty,
                _ => current.GetRawText()
            };
        }
        catch (JsonException ex)
        {
            return Warning($"Failed to parse JSON output ({ex.Message}). Falling back to raw stdout.")
                   + "\n" + output.TrimEnd();
        }
    }

    private static SubAgentTokenUsage? TryExtractTokenUsage(string? output, SubAgentProfile profile)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        if (string.IsNullOrWhiteSpace(profile.OutputInputTokensJsonPath)
            && string.IsNullOrWhiteSpace(profile.OutputOutputTokensJsonPath)
            && string.IsNullOrWhiteSpace(profile.OutputTotalTokensJsonPath))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(output);
            long inputTokens = TryReadLongPath(doc.RootElement, profile.OutputInputTokensJsonPath) ?? 0;
            long outputTokens = TryReadLongPath(doc.RootElement, profile.OutputOutputTokensJsonPath) ?? 0;
            var totalTokens = TryReadLongPath(doc.RootElement, profile.OutputTotalTokensJsonPath);

            if (inputTokens == 0 && outputTokens == 0 && totalTokens.HasValue)
                outputTokens = totalTokens.Value;

            return inputTokens == 0 && outputTokens == 0
                ? null
                : new SubAgentTokenUsage(inputTokens, outputTokens);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long? TryReadLongPath(JsonElement root, string? jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath))
            return null;

        JsonElement current = root;
        foreach (var segment in jsonPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                return null;
        }

        return current.ValueKind switch
        {
            JsonValueKind.Number when current.TryGetInt64(out var value) => value,
            JsonValueKind.String when long.TryParse(current.GetString(), out var value) => value,
            _ => null
        };
    }

    private static string BuildNonZeroExitMessage(
        int exitCode,
        string stdout,
        string stderr,
        string profileName,
        string resolvedBinary,
        int? maxOutputBytes)
    {
        var builder = new StringBuilder();
        builder.Append($"Error: External subagent exited with code {exitCode}. ");
        builder.Append($"Profile='{profileName}', Binary='{resolvedBinary}'.");

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("STDOUT:");
            builder.Append(stdout.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("STDERR:");
            builder.Append(stderr.TrimEnd());
        }

        return TruncateToMaxBytes(builder.ToString(), maxOutputBytes ?? DefaultMaxOutputBytes);
    }

    private static InvocationPlan BuildInvocation(
        SubAgentProfile profile,
        string task,
        IReadOnlyList<string> extraLaunchArgs,
        string? resumeSessionId,
        ref string? outputFilePath)
    {
        var arguments = new List<string>();
        if (profile.Args != null)
            arguments.AddRange(profile.Args);
        if (!string.IsNullOrWhiteSpace(resumeSessionId))
        {
            if (string.IsNullOrWhiteSpace(profile.ResumeArgTemplate))
            {
                throw new InvalidOperationException(
                    $"Subagent profile '{profile.Name}' requested resume but does not define resumeArgTemplate.");
            }

            arguments.AddRange(ExpandTemplateArguments(
                profile.ResumeArgTemplate,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sessionId"] = resumeSessionId.Trim()
                }));
        }
        if (extraLaunchArgs.Count > 0)
            arguments.AddRange(extraLaunchArgs);

        if (profile.ReadOutputFile == true)
        {
            if (string.IsNullOrWhiteSpace(profile.OutputFileArgTemplate))
            {
                throw new InvalidOperationException(
                    $"Subagent profile '{profile.Name}' enables output file capture but does not define outputFileArgTemplate.");
            }

            outputFilePath = Path.Combine(Path.GetTempPath(), $"dotcraft-subagent-{Guid.NewGuid():N}.txt");
            arguments.AddRange(ExpandTemplateArguments(
                profile.OutputFileArgTemplate,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["path"] = outputFilePath
                }));
        }

        var inputMode = profile.InputMode?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(inputMode))
            inputMode = "arg";

        var writeTaskToStdin = false;
        switch (inputMode)
        {
            case "stdin":
                writeTaskToStdin = true;
                break;
            case "arg":
                arguments.Add(task);
                break;
            case "arg-template":
                if (string.IsNullOrWhiteSpace(profile.InputArgTemplate))
                {
                    throw new InvalidOperationException(
                        $"Subagent profile '{profile.Name}' uses inputMode 'arg-template' but does not define inputArgTemplate.");
                }

                arguments.AddRange(ExpandTemplateArguments(
                    profile.InputArgTemplate,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["task"] = task
                    }));
                break;
            case "env":
                if (string.IsNullOrWhiteSpace(profile.InputEnvKey))
                {
                    throw new InvalidOperationException(
                        $"Subagent profile '{profile.Name}' uses inputMode 'env' but does not define inputEnvKey.");
                }
                break;
            default:
                throw new InvalidOperationException(
                    $"Subagent profile '{profile.Name}' has unsupported inputMode '{profile.InputMode}'.");
        }

        return new InvocationPlan(arguments, writeTaskToStdin, inputMode);
    }

    private static string? TryExtractSessionId(string output, SubAgentProfile profile)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        var fromJsonPath = TryExtractSessionIdByJsonPath(output, profile.ResumeSessionIdJsonPath);
        if (!string.IsNullOrWhiteSpace(fromJsonPath))
            return fromJsonPath;

        if (string.IsNullOrWhiteSpace(profile.ResumeSessionIdRegex))
            return null;

        try
        {
            var match = Regex.Match(output, profile.ResumeSessionIdRegex, RegexOptions.Multiline);
            if (!match.Success)
                return null;

            if (match.Groups["sessionId"] is { Success: true } namedGroup)
                return namedGroup.Value;

            if (match.Groups.Count > 1)
                return match.Groups[1].Value;
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string? TryExtractSessionIdByJsonPath(string output, string? jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(output);
            JsonElement current = doc.RootElement;
            foreach (var segment in jsonPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                    return null;
            }

            return current.ValueKind switch
            {
                JsonValueKind.String => current.GetString(),
                JsonValueKind.Number => current.GetRawText(),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ConfigureEnvironment(ProcessStartInfo psi, SubAgentProfile profile, string task)
    {
        psi.Environment.Clear();

        foreach (var key in OperatingSystem.IsWindows() ? WindowsMinimalEnvironmentKeys : UnixMinimalEnvironmentKeys)
        {
            TryCopyEnvironmentVariable(psi, key);
        }

        if (profile.EnvPassthrough != null)
        {
            foreach (var key in profile.EnvPassthrough)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                TryCopyEnvironmentVariable(psi, key);
            }
        }

        if (string.Equals(profile.InputMode, "env", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(profile.InputEnvKey))
        {
            psi.Environment[profile.InputEnvKey] = task;
        }

        if (profile.Env == null)
            return;

        foreach (var (key, value) in profile.Env)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            psi.Environment[key] = value;
        }
    }

    private static void TryCopyEnvironmentVariable(ProcessStartInfo psi, string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrEmpty(value))
            psi.Environment[key] = value;
    }

    private static IEnumerable<string> ExpandTemplateArguments(
        string template,
        IReadOnlyDictionary<string, string> replacements)
    {
        var templateArguments = SplitArguments(template);
        if (templateArguments.Count == 0 || replacements.Count == 0)
            return templateArguments;

        var expandedArguments = new List<string>(templateArguments.Count);
        foreach (var argument in templateArguments)
        {
            var expandedArgument = argument;
            foreach (var (key, value) in replacements)
                expandedArgument = expandedArgument.Replace("{" + key + "}", value, StringComparison.Ordinal);

            expandedArguments.Add(expandedArgument);
        }

        return expandedArguments;
    }

    internal static IReadOnlyList<string> SplitArguments(string commandLine)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine))
            return result;

        var current = new StringBuilder();
        bool inQuotes = false;
        for (var i = 0; i < commandLine.Length; i++)
        {
            var ch = commandLine[i];
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }

    internal static bool TryResolveExecutablePath(string bin, out string? resolvedBinary)
    {
        resolvedBinary = null;
        if (string.IsNullOrWhiteSpace(bin))
            return false;

        var normalizedBin = bin.Trim();
        var now = DateTimeOffset.UtcNow;
        if (BinaryProbeCache.TryGetValue(normalizedBin, out var cached) && cached.ExpiresAt > now)
        {
            resolvedBinary = cached.ResolvedBinary;
            return cached.IsResolved;
        }

        bool isResolved;
        try
        {
            resolvedBinary = ResolveExecutablePathCore(normalizedBin);
            isResolved = true;
        }
        catch
        {
            resolvedBinary = null;
            isResolved = false;
        }

        BinaryProbeCache[normalizedBin] = new BinaryProbeCacheEntry(
            now.Add(BinaryProbeCacheTtl),
            isResolved,
            resolvedBinary);

        return isResolved;
    }

    internal static string ResolveExecutablePath(string bin)
    {
        if (string.IsNullOrWhiteSpace(bin))
            throw new InvalidOperationException("External subagent binary was not configured.");

        return ResolveExecutablePathCore(bin.Trim());
    }

    private static string ResolveExecutablePathCore(string bin)
    {
        if (Path.IsPathRooted(bin))
            return ValidateExecutablePath(Path.GetFullPath(bin), bin);

        if (bin.Contains(Path.DirectorySeparatorChar) || bin.Contains(Path.AltDirectorySeparatorChar))
            return ValidateExecutablePath(Path.GetFullPath(bin), bin);

        if (OperatingSystem.IsWindows())
        {
            var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var extensions = GetWindowsExecutableExtensions(bin);
            foreach (var directory in pathEntries)
            {
                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(directory, bin + extension);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            throw new InvalidOperationException(
                $"External subagent binary '{bin}' was not found on PATH as a launchable executable.");
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, bin);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException($"External subagent binary '{bin}' was not found on PATH.");
    }

    private static string ValidateExecutablePath(string candidatePath, string originalValue)
    {
        if (!File.Exists(candidatePath))
            throw new InvalidOperationException($"External subagent binary '{originalValue}' does not exist.");

        if (OperatingSystem.IsWindows())
        {
            var extension = Path.GetExtension(candidatePath);
            if (!WindowsLaunchableExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"External subagent binary '{candidatePath}' is not directly launchable on Windows. Use a .cmd or .exe wrapper instead.");
            }
        }

        return candidatePath;
    }

    private static IReadOnlyList<string> GetWindowsExecutableExtensions(string bin)
    {
        var configuredExtensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(ext => WindowsLaunchableExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (configuredExtensions.Count == 0)
            configuredExtensions.AddRange(WindowsLaunchableExtensions);

        if (!string.IsNullOrEmpty(Path.GetExtension(bin)))
            return [string.Empty];

        return configuredExtensions;
    }

    internal static string TruncateToMaxBytes(string text, int maxOutputBytes)
    {
        if (maxOutputBytes <= 0)
            return text;

        var utf8 = Encoding.UTF8;
        if (utf8.GetByteCount(text) <= maxOutputBytes)
            return text;

        var suffixBytes = utf8.GetByteCount(TruncationSuffix);
        var targetBytes = Math.Max(0, maxOutputBytes - suffixBytes);
        var length = text.Length;
        while (length > 0 && utf8.GetByteCount(text.AsSpan(0, length)) > targetBytes)
            length--;

        return text[..length] + TruncationSuffix;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignored
        }
    }

    private static string Warning(string message) => $"Warning: {message}";

    private static string BuildWarningWithFallback(string warning, string fallback)
    {
        fallback = string.IsNullOrWhiteSpace(fallback) ? "(no output)" : fallback.TrimEnd();
        return Warning(warning) + "\n" + fallback;
    }

    private static SubAgentRunResult Error(string message)
        => new()
        {
            Text = message.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ? message : $"Error: {message}",
            IsError = true
        };

    private sealed class CliOneshotSessionState(
        SubAgentProfile profile,
        IReadOnlyList<string>? extraLaunchArgs,
        string? resumeSessionId)
    {
        private readonly Lock _lock = new();
        private Process? _process;
        private SafeJobHandle? _jobHandle;

        public SubAgentProfile Profile { get; } = profile;
        public IReadOnlyList<string> ExtraLaunchArgs { get; } = extraLaunchArgs ?? [];
        public string? ResumeSessionId { get; } = string.IsNullOrWhiteSpace(resumeSessionId) ? null : resumeSessionId.Trim();

        public void Attach(Process process)
        {
            lock (_lock)
            {
                _process = process;
                if (OperatingSystem.IsWindows())
                    _jobHandle = CreateKillOnCloseJobAndAssign(process);
            }
        }

        public void Clear(Process process)
        {
            lock (_lock)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                    _jobHandle?.Dispose();
                    _jobHandle = null;
                }
            }
        }

        public void KillCurrentProcessTree()
        {
            Process? process;
            SafeJobHandle? jobHandle;
            lock (_lock)
            {
                process = _process;
                jobHandle = _jobHandle;
            }

            if (process == null)
                return;

            try
            {
                if (jobHandle is { IsInvalid: false })
                {
                    TerminateJobObject(jobHandle, 1);
                    return;
                }

                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignored
            }
        }

        private static SafeJobHandle CreateKillOnCloseJobAndAssign(Process process)
        {
            var jobHandle = CreateJobObject(IntPtr.Zero, null);
            if (jobHandle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create Windows Job Object.");

            try
            {
                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                {
                    BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                    {
                        LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                    }
                };

                var length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
                var buffer = Marshal.AllocHGlobal(length);
                try
                {
                    Marshal.StructureToPtr(info, buffer, fDeleteOld: false);
                    if (!SetInformationJobObject(
                            jobHandle,
                            JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
                            buffer,
                            (uint)length))
                    {
                        throw new Win32Exception(
                            Marshal.GetLastWin32Error(),
                            "Failed to configure Windows Job Object with kill-on-close.");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }

                if (!AssignProcessToJobObject(jobHandle, process.Handle))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        $"Failed to assign child process {process.Id} to Windows Job Object.");
                }

                return jobHandle;
            }
            catch
            {
                jobHandle.Dispose();
                throw;
            }
        }

        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

        private enum JOBOBJECTINFOCLASS
        {
            JobObjectExtendedLimitInformation = 9
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public nuint Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        private sealed class SafeJobHandle() : SafeHandle(IntPtr.Zero, ownsHandle: true)
        {
            public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

            protected override bool ReleaseHandle() => CloseHandle(handle);
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeJobHandle CreateJobObject(IntPtr jobAttributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(
            SafeJobHandle job,
            JOBOBJECTINFOCLASS jobObjectInfoClass,
            IntPtr jobObjectInfo,
            uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(SafeJobHandle job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateJobObject(SafeJobHandle hJob, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }

    private readonly record struct InvocationPlan(
        IReadOnlyList<string> Arguments,
        bool WriteTaskToStdin,
        string InputMode);

    private readonly record struct OutputReadResult(string DisplayText, string TokenSourceText);

    private readonly record struct BinaryProbeCacheEntry(
        DateTimeOffset ExpiresAt,
        bool IsResolved,
        string? ResolvedBinary);
}
