using System.Text;
using System.Text.Json.Nodes;
using DotCraft.Protocol.AppServer;
using DotCraft.SourceControl;
using DotCraft.Tools;
using DotCraft.Utilities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SessionThread = DotCraft.Sessions.SessionThread;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using UserMessagePayload = DotCraft.Sessions.UserMessagePayload;

namespace DotCraft.Sessions;

/// <summary>
/// Orchestrates source-control summary suggestion via an ephemeral thread and the <see cref="CommitSuggestToolSource"/> profile.
/// </summary>
public interface ICommitMessageSuggestService
{
    Task<CommitMessageSuggestionResult> SuggestAsync(
        CommitMessageSuggestionRequest parameters,
        CancellationToken cancellationToken = default);
}

public sealed class CommitMessageSuggestService(
    ISessionService sessionService,
    string workspaceRoot,
    ILogger<CommitMessageSuggestService>? logger = null,
    Func<SourceControlConfig>? sourceControlConfigProvider = null,
    Func<string, string, TimeSpan, IDictionary<string, string>?, ILogger?, IPerforceCommandRunner>? perforceRunnerFactory = null) : ICommitMessageSuggestService
{
    private const int DefaultMaxDiffChars = 100_000;
    private const int MaxContextChars = 60_000;
    private static readonly TimeSpan SuggestTimeout = TimeSpan.FromMinutes(2);

    public async Task<CommitMessageSuggestionResult> SuggestAsync(
        CommitMessageSuggestionRequest parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrWhiteSpace(parameters.ThreadId))
            throw new InvalidOperationException("threadId is required.");
        if (parameters.Paths is not { Length: > 0 })
            throw new InvalidOperationException("paths must contain at least one path.");

        var ws = Path.GetFullPath(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        ValidateRelativePaths(ws, parameters.Paths);

        var sourceThread = await sessionService.GetThreadAsync(parameters.ThreadId, cancellationToken)
            .ConfigureAwait(false);

        var sourceWs = Path.GetFullPath(sourceThread.WorkspacePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.Equals(sourceWs, ws, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source thread belongs to a different workspace.");

        using var timeoutCts = new CancellationTokenSource(SuggestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var provider = NormalizeProvider(parameters.Provider);
        var maxDiff = parameters.MaxDiffChars is > 0 and int m ? m : DefaultMaxDiffChars;
        var changeContext = await BuildChangeContextAsync(provider, ws, parameters.Paths, maxDiff, logger, linked.Token)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(changeContext))
        {
            var message = provider == SourceControlProviders.Perforce
                ? "No Perforce diff for the given paths."
                : "No diff for the given paths (nothing to commit or not a git repository).";
            throw new InvalidOperationException(message);
        }

        var history = BuildHistoryMessages(sourceThread, MaxContextChars);
        if (history.Count == 0)
            history.Add(new ChatMessage(ChatRole.User, "[No prior user or agent messages in this thread.]"));
        var userPrompt = BuildUserPrompt(provider, parameters.Paths, changeContext);

        string? tempThreadId = null;
        try
        {
            var identity = new SessionIdentity
            {
                ChannelName = CommitMessageSuggestConstants.ChannelName,
                UserId = CommitMessageSuggestConstants.InternalUserId,
                WorkspacePath = ws,
                ChannelContext = $"commit-suggest:{parameters.ThreadId}"
            };

            var config = new ThreadConfiguration
            {
                Mode = "agent",
                ToolProfile = CommitMessageSuggestConstants.ToolProfileName,
                UseToolProfileOnly = true,
                ApprovalPolicy = ApprovalPolicy.AutoApprove,
                AgentInstructions = CommitMessageSuggestInstructions.SystemPrompt
            };

            var tempThread = await sessionService.CreateThreadAsync(
                    identity,
                    config,
                    HistoryMode.Client,
                    displayName: "[internal] Commit suggestion",
                    ct: linked.Token)
                .ConfigureAwait(false);

            tempThreadId = tempThread.Id;
            tempThread.Metadata[CommitMessageSuggestConstants.InternalMetadataKey] =
                CommitMessageSuggestConstants.InternalMetadataValue;

            string? summary = null;
            string? body = null;

            await foreach (var evt in sessionService.SubmitInputAsync(
                               tempThreadId,
                               userPrompt,
                               messages: history.ToArray(),
                               ct: linked.Token).ConfigureAwait(false))
            {
                if (evt.EventType != SessionEventType.ItemCompleted || evt.ItemPayload == null)
                    continue;
                if (evt.ItemPayload.Type != ItemType.ToolCall)
                    continue;
                var tc = evt.ItemPayload.AsToolCall;
                if (tc == null || !string.Equals(tc.ToolName, CommitSuggestMethods.ToolName, StringComparison.Ordinal))
                    continue;
                (summary, body) = ParseCommitSuggestArgs(tc.Arguments);
                if (!string.IsNullOrWhiteSpace(summary))
                    break;
            }

            if (string.IsNullOrWhiteSpace(summary))
                throw new InvalidOperationException(
                    "The model did not call CommitSuggest with a summary. Try again or edit the message manually.");

            var message = string.IsNullOrWhiteSpace(body)
                ? summary.Trim()
                : summary.Trim() + Environment.NewLine + Environment.NewLine + body.Trim();

            return new CommitMessageSuggestionResult(message);
        }
        finally
        {
            if (tempThreadId != null)
            {
                try
                {
                    // Use a dedicated timeout for cleanup to ensure we don't leak threads
                    // even if the main operation was cancelled
                    using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await sessionService.DeleteThreadPermanentlyAsync(tempThreadId, cleanupCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    logger?.LogWarning("Timeout while deleting ephemeral commit-suggest thread {ThreadId}", tempThreadId);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to delete ephemeral commit-suggest thread {ThreadId}", tempThreadId);
                }
            }
        }
    }

    private async Task<string> BuildChangeContextAsync(
        string provider,
        string workspaceRoot,
        string[] paths,
        int maxChars,
        ILogger? log,
        CancellationToken ct)
    {
        return provider switch
        {
            SourceControlProviders.Git => await RunGitDiffAsync(workspaceRoot, paths, maxChars, log, ct)
                .ConfigureAwait(false),
            SourceControlProviders.Perforce => await RunPerforceDiffAsync(workspaceRoot, paths, maxChars, log, ct)
                .ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported source-control provider '{provider}'.")
        };
    }

    private static string NormalizeProvider(string? provider)
    {
        var value = provider?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return SourceControlProviders.Git;
        if (string.Equals(value, SourceControlProviders.Git, StringComparison.OrdinalIgnoreCase))
            return SourceControlProviders.Git;
        if (string.Equals(value, SourceControlProviders.Perforce, StringComparison.OrdinalIgnoreCase))
            return SourceControlProviders.Perforce;
        throw new InvalidOperationException($"Unsupported source-control provider '{value}'.");
    }

    private static string BuildUserPrompt(string provider, string[] paths, string changeContext)
    {
        var sb = new StringBuilder();
        if (provider == SourceControlProviders.Perforce)
        {
            sb.AppendLine("Produce a Perforce pending changelist description for the following changes.");
            sb.AppendLine("Use a concise first line and optional detail lines. Do not use Conventional Commits unless the project context clearly asks for it.");
        }
        else
        {
            sb.AppendLine("Produce a git commit message for the following changes.");
            sb.AppendLine("Use Conventional Commits style and imperative mood.");
        }
        sb.AppendLine("Paths (relative to workspace): " + string.Join(", ", paths));
        sb.AppendLine();
        sb.AppendLine(provider == SourceControlProviders.Perforce
            ? "--- perforce opened/diff context ---"
            : "--- unified diff ---");
        sb.AppendLine(changeContext);
        return sb.ToString();
    }

    private static (string? Summary, string? Body) ParseCommitSuggestArgs(JsonObject? arguments)
    {
        if (arguments == null)
            return (null, null);
        string? summary = null;
        string? body = null;
        if (arguments.TryGetPropertyValue("summary", out var sNode))
            summary = sNode?.GetValue<string>();
        if (arguments.TryGetPropertyValue("body", out var bNode))
            body = bNode?.GetValue<string>();
        return (summary, body);
    }

    private static List<ChatMessage> BuildHistoryMessages(SessionThread thread, int maxChars)
    {
        var pairs = new List<(ChatRole Role, string Text)>();
        foreach (var turn in thread.Turns)
        {
            foreach (var item in turn.Items)
            {
                if (item.Type == ItemType.UserMessage && item.Payload is UserMessagePayload u &&
                    !string.IsNullOrWhiteSpace(u.Text))
                    pairs.Add((ChatRole.User, u.Text.Trim()));
                else if (item.Type == ItemType.AgentMessage && item.Payload is AgentMessagePayload a &&
                         !string.IsNullOrWhiteSpace(a.Text))
                    pairs.Add((ChatRole.Assistant, a.Text.Trim()));
            }
        }

        while (pairs.Count > 0)
        {
            var total = pairs.Sum(p => p.Text.Length);
            if (total <= maxChars)
                break;
            pairs.RemoveAt(0);
        }

        return pairs.Select(p => new ChatMessage(p.Role, p.Text)).ToList();
    }

    private static void ValidateRelativePaths(string workspaceRoot, string[] paths)
    {
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p))
                throw new InvalidOperationException("paths must not contain empty entries.");
            if (p.Contains("..", StringComparison.Ordinal))
                throw new InvalidOperationException("paths must not contain '..'.");

            var combined = Path.GetFullPath(Path.Combine(workspaceRoot, p));

            // Resolve symbolic links and junctions to prevent path traversal attacks
            string resolvedPath;
            try
            {
                resolvedPath = ResolveSymbolicLink(combined);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Path doesn't exist or cannot be accessed - validate the path itself
                resolvedPath = combined;
            }

            // Resolve workspace root as well to handle symlinks in the workspace path
            string resolvedWorkspace = ResolveSymbolicLink(workspaceRoot);

            if (!resolvedPath.StartsWith(resolvedWorkspace, StringComparison.OrdinalIgnoreCase) ||
                (resolvedPath.Length > resolvedWorkspace.Length &&
                 resolvedPath[resolvedWorkspace.Length] != Path.DirectorySeparatorChar &&
                 resolvedPath[resolvedWorkspace.Length] != Path.AltDirectorySeparatorChar))
                throw new InvalidOperationException($"Path escapes workspace: {p}");
        }
    }

    /// <summary>Maximum symlink hops to follow (defense in depth alongside cycle detection).</summary>
    private const int MaxSymlinkResolveDepth = 64;

    /// <summary>
    /// Resolves symbolic links, junctions, and other reparse points to get the canonical path.
    /// Detects cycles so circular symlinks cannot cause unbounded recursion or <see cref="StackOverflowException"/>.
    /// </summary>
    private static string ResolveSymbolicLink(string path)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return ResolveSymbolicLinkCore(Path.GetFullPath(path), visited, depth: 0);
    }

    private static string ResolveSymbolicLinkCore(string path, HashSet<string> visited, int depth)
    {
        if (depth >= MaxSymlinkResolveDepth)
            throw new InvalidOperationException("Symbolic link resolution exceeded maximum depth.");

        if (!File.Exists(path) && !Directory.Exists(path))
            return path;

        if (!visited.Add(path))
            throw new InvalidOperationException("Circular symbolic link detected.");

        try
        {
            if (Directory.Exists(path))
            {
                var dirInfo = new DirectoryInfo(path);
                if (dirInfo.LinkTarget is { } linkTarget)
                {
                    var resolved = Path.GetFullPath(Path.Combine(
                        Path.GetDirectoryName(path) ?? path,
                        linkTarget));
                    return ResolveSymbolicLinkCore(resolved, visited, depth + 1);
                }
            }
            else if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                if (fileInfo.LinkTarget is { } linkTarget)
                {
                    var resolved = Path.GetFullPath(Path.Combine(
                        Path.GetDirectoryName(path) ?? path,
                        linkTarget));
                    return ResolveSymbolicLinkCore(resolved, visited, depth + 1);
                }
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            // If we can't resolve, fall back to the original path
        }

        return Path.GetFullPath(path);
    }

    private async Task<string> RunPerforceDiffAsync(
        string workspaceRoot,
        string[] paths,
        int maxChars,
        ILogger? log,
        CancellationToken ct)
    {
        var config = sourceControlConfigProvider?.Invoke() ?? new SourceControlConfig();
        var effective = SourceControlResolver.ResolveEffectiveProvider(config, workspaceRoot);
        if (!string.Equals(effective, SourceControlProviders.Perforce, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Perforce summary generation requires a Perforce source control binding.");
        if (!config.Perforce.Online)
            throw new InvalidOperationException("Perforce source control is offline. Test the connection and save it online before generating a changelist description.");

        var perforce = config.Perforce;
        var timeoutSeconds = perforce.TimeoutSeconds > 0 ? perforce.TimeoutSeconds : 30;
        var executable = string.IsNullOrWhiteSpace(perforce.P4ExecutablePath) ? "p4" : perforce.P4ExecutablePath.Trim();
        var mode = SourceControlConnectionModes.Normalize(config.ConnectionMode);
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        if (mode == SourceControlConnectionModes.P4Config && !string.IsNullOrWhiteSpace(perforce.P4ConfigName))
            env["P4CONFIG"] = perforce.P4ConfigName.Trim();

        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        var runner = perforceRunnerFactory?.Invoke(executable, workspaceRoot, timeout, env, log)
            ?? new DefaultPerforceCommandRunner(executable, workspaceRoot, timeout, env, log);
        var globals = BuildPerforceGlobals(mode, perforce);

        var sb = new StringBuilder();
        var hasDiff = false;
        foreach (var path in paths)
        {
            var fullPath = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(workspaceRoot, path.Replace('/', Path.DirectorySeparatorChar)));

            var opened = await runner.RunAsync([.. globals, "opened", fullPath], null, ct)
                .ConfigureAwait(false);
            var openedText = TrimPerforceOutput(opened);
            if (!opened.Ok && !IsPerforceNotOpened(openedText))
                throw new InvalidOperationException(MapPerforceFailure(opened, "Unable to read Perforce opened file state."));
            if (!opened.Ok)
                continue;

            var diff = await runner.RunAsync([.. globals, "diff", "-du", fullPath], null, ct)
                .ConfigureAwait(false);
            var diffText = diff.StdOut.TrimEnd();
            if (!diff.Ok && string.IsNullOrWhiteSpace(diffText))
                throw new InvalidOperationException(MapPerforceFailure(diff, "Unable to read Perforce diff."));
            if (string.IsNullOrWhiteSpace(diffText))
                continue;

            hasDiff = true;
            if (sb.Length > 0)
                sb.AppendLine().AppendLine();
            sb.AppendLine($"--- path: {path} ---");
            if (!string.IsNullOrWhiteSpace(openedText))
            {
                sb.AppendLine("--- p4 opened ---");
                sb.AppendLine(openedText);
            }
            sb.AppendLine("--- p4 diff -du ---");
            sb.AppendLine(diffText);

            if (sb.Length > maxChars)
                return sb.ToString(0, maxChars) + "\n\n[diff truncated]";
        }

        return hasDiff ? sb.ToString() : string.Empty;
    }

    private static List<string> BuildPerforceGlobals(string mode, PerforceConnectionConfig perforce)
    {
        var args = new List<string>();
        if (mode == SourceControlConnectionModes.Manual)
        {
            AddPerforceOption(args, "-p", perforce.Port);
            AddPerforceOption(args, "-c", perforce.Client);
            AddPerforceOption(args, "-u", perforce.User);
        }
        AddPerforceOption(args, "-C", perforce.Charset);
        return args;
    }

    private static void AddPerforceOption(List<string> args, string flag, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        args.Add(flag);
        args.Add(value.Trim());
    }

    private static string TrimPerforceOutput(PerforceCommandResult result)
    {
        var stdout = result.StdOut.Trim();
        var stderr = result.StdErr.Trim();
        if (string.IsNullOrWhiteSpace(stdout))
            return stderr;
        if (string.IsNullOrWhiteSpace(stderr))
            return stdout;
        return stdout + "\n" + stderr;
    }

    private static bool IsPerforceNotOpened(string text) =>
        text.Contains("not opened", StringComparison.OrdinalIgnoreCase);

    private static string MapPerforceFailure(PerforceCommandResult result, string fallback)
    {
        if (result.ExecutableMissing)
            return "p4 was not found in the server environment.";
        if (result.TimedOut)
            return "The Perforce command timed out.";
        var detail = FirstNonEmptyLine(result.StdErr) ?? FirstNonEmptyLine(result.StdOut);
        if (detail?.Contains("login", StringComparison.OrdinalIgnoreCase) == true
            || detail?.Contains("password", StringComparison.OrdinalIgnoreCase) == true
            || detail?.Contains("ticket", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Perforce login is required before generating a changelist description.";
        }
        return string.IsNullOrWhiteSpace(detail) ? fallback : detail;
    }

    private static string? FirstNonEmptyLine(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length > 0)
                return line;
        }
        return null;
    }

    private static async Task<string> RunGitDiffAsync(
        string workspaceRoot,
        string[] paths,
        int maxChars,
        ILogger? logger,
        CancellationToken ct)
    {
        string stdout;
        try
        {
            stdout = await RunGitDiffAgainstHeadAsync(workspaceRoot, paths, logger, ct)
                .ConfigureAwait(false);
        }
        catch (GitProcessTimeoutException ex)
        {
            logger?.LogWarning(ex, "git diff timed out while preparing commit message suggestion context");
            throw new InvalidOperationException("git diff timed out while preparing commit-message context.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "git diff execution failed");
            throw new InvalidOperationException("Failed to run git diff. Ensure git is installed and the workspace is a repository.");
        }

        if (string.IsNullOrWhiteSpace(stdout))
            stdout = await RunGitDiffNoIndexUntrackedAsync(workspaceRoot, paths, logger, ct)
                .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(stdout))
            return string.Empty;

        if (stdout.Length > maxChars)
            return stdout[..maxChars] + "\n\n[diff truncated]";
        return stdout;
    }

    /// <summary>Working tree vs <c>HEAD</c> for the given paths (tracked / indexed content).</summary>
    private static async Task<string> RunGitDiffAgainstHeadAsync(
        string workspaceRoot,
        string[] paths,
        ILogger? logger,
        CancellationToken ct)
    {
        var args = new List<string>
        {
            "diff",
            "--no-color",
            "HEAD",
            "--"
        };
        foreach (var p in paths)
            args.Add(p.Replace('/', Path.DirectorySeparatorChar));

        var result = await GitProcessRunner.RunAsync(
                workspaceRoot,
                args,
                timeout: TimeSpan.FromSeconds(30),
                ct: ct,
                logger: logger)
            .ConfigureAwait(false);

        if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(result.StdOut))
        {
            logger?.LogWarning("git diff failed: {Stderr}", result.StdErr);
            return string.Empty;
        }

        return result.StdOut;
    }

    /// <summary>
    /// For <b>untracked</b> files, <c>git diff HEAD -- path</c> is empty; use <c>git diff --no-index</c> from the null device.
    /// </summary>
    private static async Task<string> RunGitDiffNoIndexUntrackedAsync(
        string workspaceRoot,
        string[] paths,
        ILogger? logger,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        var nullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";

        foreach (var p in paths)
        {
            var rel = p.Replace('/', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(workspaceRoot, rel));
            if (!File.Exists(full))
                continue;
            if (await IsGitPathTrackedAsync(workspaceRoot, rel, logger, ct).ConfigureAwait(false))
                continue;

            var chunk = await RunGitDiffNoIndexSingleAsync(workspaceRoot, nullDevice, rel, logger, ct)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(chunk))
                continue;
            if (sb.Length > 0)
                sb.AppendLine().AppendLine();
            sb.AppendLine($"--- untracked: {rel} ---");
            sb.Append(chunk.TrimEnd());
        }

        return sb.ToString();
    }

    private static async Task<bool> IsGitPathTrackedAsync(
        string workspaceRoot,
        string relativePath,
        ILogger? logger,
        CancellationToken ct)
    {
        try
        {
            var result = await GitProcessRunner.RunAsync(
                    workspaceRoot,
                    ["ls-files", "--error-unmatch", "--", relativePath],
                    timeout: TimeSpan.FromSeconds(10),
                    ct: ct,
                    logger: logger)
                .ConfigureAwait(false);
            return result.ExitCode == 0;
        }
        catch (GitProcessTimeoutException ex)
        {
            logger?.LogWarning(ex, "git ls-files timed out for {Path}", relativePath);
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "git ls-files failed for {Path}", relativePath);
            return true;
        }
    }

    private static async Task<string> RunGitDiffNoIndexSingleAsync(
        string workspaceRoot,
        string nullDevice,
        string relativePath,
        ILogger? logger,
        CancellationToken ct)
    {
        try
        {
            var result = await GitProcessRunner.RunAsync(
                    workspaceRoot,
                    ["diff", "--no-color", "--no-index", nullDevice, relativePath],
                    timeout: TimeSpan.FromSeconds(30),
                    ct: ct,
                    logger: logger)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(result.StdOut))
                return string.Empty;
            return result.StdOut;
        }
        catch (GitProcessTimeoutException ex)
        {
            logger?.LogWarning(ex, "git diff --no-index timed out for {Path}", relativePath);
            return string.Empty;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "git diff --no-index failed for {Path}", relativePath);
            return string.Empty;
        }
    }
}
