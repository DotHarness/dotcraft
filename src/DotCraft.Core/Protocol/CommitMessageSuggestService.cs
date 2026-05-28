using System.Text;
using System.Text.Json.Nodes;
using DotCraft.Protocol.AppServer;
using DotCraft.Tools;
using DotCraft.Utilities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Protocol;

/// <summary>
/// Orchestrates commit-message suggestion via an ephemeral thread and the <see cref="CommitSuggestToolProvider"/> profile.
/// </summary>
public interface ICommitMessageSuggestService
{
    Task<WorkspaceCommitMessageSuggestResult> SuggestAsync(
        WorkspaceCommitMessageSuggestParams parameters,
        CancellationToken cancellationToken = default);
}

public sealed class CommitMessageSuggestService(
    ISessionService sessionService,
    string workspaceRoot,
    ILogger<CommitMessageSuggestService>? logger = null) : ICommitMessageSuggestService
{
    private const int DefaultMaxDiffChars = 100_000;
    private const int MaxContextChars = 60_000;
    private static readonly TimeSpan SuggestTimeout = TimeSpan.FromMinutes(2);

    public async Task<WorkspaceCommitMessageSuggestResult> SuggestAsync(
        WorkspaceCommitMessageSuggestParams parameters,
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

        var maxDiff = parameters.MaxDiffChars is > 0 and int m ? m : DefaultMaxDiffChars;
        var diffText = await RunGitDiffAsync(ws, parameters.Paths, maxDiff, logger, linked.Token)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(diffText))
            throw new InvalidOperationException("No diff for the given paths (nothing to commit or not a git repository).");

        var history = BuildHistoryMessages(sourceThread, MaxContextChars);
        if (history.Count == 0)
            history.Add(new ChatMessage(ChatRole.User, "[No prior user or agent messages in this thread.]"));
        var userPrompt = BuildUserPrompt(parameters.Paths, diffText);

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

            return new WorkspaceCommitMessageSuggestResult { Message = message };
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

    private static string BuildUserPrompt(string[] paths, string diffText)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Produce a git commit message for the following changes.");
        sb.AppendLine("Paths (relative to workspace): " + string.Join(", ", paths));
        sb.AppendLine();
        sb.AppendLine("--- unified diff ---");
        sb.AppendLine(diffText);
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
