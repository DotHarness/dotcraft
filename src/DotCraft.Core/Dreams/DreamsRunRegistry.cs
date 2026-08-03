using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Sessions;
using SessionThread = DotCraft.Sessions.SessionThread;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using UserMessagePayload = DotCraft.Sessions.UserMessagePayload;

namespace DotCraft.Dreams;

/// <summary>
/// Holds per-run Dreams input snapshots and file-tool workspaces for internal Dreams threads.
/// </summary>
public sealed class DreamsRunRegistry(
    ThreadStore threadStore,
    MemoryStore memoryStore,
    DreamStore dreamStore)
{
    private const int MaxSearches = 20;
    private const int MaxReads = 25;
    private const int MaxToolResultChars = 12_000;
    private const int MaxTotalEvidenceChars = 120_000;
    private const int DefaultReadLimit = 120;
    private const int MaxReadLimit = 300;
    private const int MaxSearchMatches = 50;
    private const int MaxSnippetChars = 500;

    private readonly ConcurrentDictionary<string, RegisteredDreamRun> _runs = new(StringComparer.Ordinal);

    public async Task<DreamsRunWorkspace> PrepareRunWorkspaceAsync(
        string runId,
        DreamsRunInput input,
        DreamStoreDescriptor outputStore,
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        var inputDir = dreamStore.GetRunInputDirectory(runId);
        var manifestPath = Path.Combine(inputDir, "MANIFEST.md");
        var activeStoreId = dreamStore.GetActiveStoreId();
        var activeStorePath = string.IsNullOrWhiteSpace(activeStoreId)
            ? null
            : dreamStore.GetStoreDescriptor(activeStoreId).DirectoryPath;

        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outputStore.DirectoryPath);
        Directory.CreateDirectory(outputStore.TopicDirectoryPath);

        await WriteInputFileAsync(inputDir, "memory/MEMORY.md", memoryStore.ReadLongTerm(), cancellationToken)
            .ConfigureAwait(false);
        await WriteInputFileAsync(inputDir, "memory/HISTORY.md", memoryStore.ReadHistory(), cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(input.ExistingDream))
        {
            await WriteInputFileAsync(inputDir, "active-dream-store/INDEX.md", dreamStore.ReadDream(), cancellationToken)
                .ConfigureAwait(false);
            foreach (var topic in dreamStore.ListTopicFiles(previewChars: int.MaxValue))
            {
                await WriteInputFileAsync(
                        inputDir,
                        $"active-dream-store/memory/{topic.Path}",
                        dreamStore.ReadTopicFile(topic.Path),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        foreach (var thread in input.Threads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transcript = await ReadThreadTranscriptAsync(thread.ThreadId, cancellationToken).ConfigureAwait(false);
            if (transcript != null)
            {
                await WriteInputFileAsync(
                        inputDir,
                        $"sessions/{SafeFileName(thread.ThreadId)}.md",
                        transcript,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var manifest = BuildManifest(runId, input, workspacePath, inputDir, outputStore);
        await File.WriteAllTextAsync(manifestPath, manifest, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(
                Path.Combine(dreamStore.GetRunDirectory(runId), "input.json"),
                JsonSerializer.Serialize(input, AppConfig.SerializerOptions) + Environment.NewLine,
                Encoding.UTF8,
                cancellationToken)
            .ConfigureAwait(false);

        return new DreamsRunWorkspace(
            runId,
            outputStore.StoreId,
            outputStore.DirectoryPath,
            inputDir,
            manifestPath,
            activeStorePath);
    }

    public void Register(string threadId, string runId, DreamsRunInput input)
    {
        _runs[threadId] = new RegisteredDreamRun(runId, input);
    }

    public void Register(string threadId, DreamsRunWorkspace workspace, DreamsRunInput input)
    {
        _runs[threadId] = new RegisteredDreamRun(workspace.RunId, input, workspace);
    }

    public void Unregister(string threadId)
    {
        _runs.TryRemove(threadId, out _);
    }

    public DreamsRunDiagnostics GetDiagnostics(string? threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId) || !_runs.TryGetValue(threadId, out var run))
            return new DreamsRunDiagnostics(0, [], 0, 0);

        lock (run.SyncRoot)
        {
            return new DreamsRunDiagnostics(
                run.Input.Threads.Count,
                run.EvidenceThreadIds.OrderBy(static id => id, StringComparer.Ordinal).ToArray(),
                run.SearchCount,
                run.ReadCount);
        }
    }

    public bool TryGetFileWorkspace(string? threadId, out DreamsRunWorkspace workspace)
    {
        workspace = null!;
        if (string.IsNullOrWhiteSpace(threadId) || !_runs.TryGetValue(threadId, out var run) || run.Workspace == null)
            return false;

        workspace = run.Workspace;
        return true;
    }

    public string ListSources(string? threadId)
    {
        if (!TryGetRun(threadId, out var run, out var error))
            return error;

        var sb = new StringBuilder();
        sb.AppendLine($"Dream run {run.RunId} sources:");
        sb.AppendLine("- memory:MEMORY.md (type=memory)");
        sb.AppendLine("- memory:HISTORY.md (type=memory)");
        sb.AppendLine("- dream:index (type=dream)");

        foreach (var topic in run.Input.TopicFiles)
            sb.AppendLine($"- dream:memory/{topic.Path} (type=topic, size={topic.SizeBytes}, modified={topic.LastModifiedAt:O})");

        foreach (var thread in run.Input.Threads)
        {
            sb.Append("- thread:");
            sb.Append(thread.ThreadId);
            sb.Append(" (type=session");
            sb.Append($", title={thread.DisplayName ?? thread.ThreadId}");
            sb.Append($", origin={thread.OriginChannel}");
            sb.Append($", status={thread.Status}");
            sb.Append($", completedTurns={thread.CompletedTurnCount}");
            sb.Append($", lastActiveAt={thread.LastActiveAt:O}");
            sb.AppendLine(")");
        }

        return sb.ToString();
    }

    public async Task<string> SearchSourcesAsync(
        string? threadId,
        string query,
        IReadOnlyList<string>? sourceTypes = null,
        IReadOnlyList<string>? threadIds = null,
        int maxMatches = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRun(threadId, out var run, out var error))
            return error;
        if (string.IsNullOrWhiteSpace(query))
            return "Error: SearchDreamSources requires a non-empty query.";
        if (!TryStartSearch(run, out error))
            return error;

        maxMatches = Math.Clamp(maxMatches <= 0 ? 20 : maxMatches, 1, MaxSearchMatches);
        var typeSet = BuildSet(sourceTypes);
        var threadSet = BuildSet(threadIds);
        var matches = new List<string>();
        var truncated = false;

        foreach (var source in EnumerateSources(run, typeSet, threadSet))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = await ReadSourceContentAsync(run, source.SourceId, cancellationToken).ConfigureAwait(false);
            if (content == null)
                continue;

            var lineNumber = 0;
            foreach (var line in SplitLines(content))
            {
                lineNumber++;
                if (line.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                matches.Add($"{source.SourceId}:{lineNumber}: {TrimLine(line, MaxSnippetChars)}");
                run.RecordEvidenceSource(source.SourceId);
                if (matches.Count >= maxMatches)
                {
                    truncated = true;
                    break;
                }
            }

            if (truncated)
                break;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"SearchDreamSources query: {query}");
        if (matches.Count == 0)
        {
            sb.AppendLine("(no matches)");
        }
        else
        {
            foreach (var match in matches)
                sb.AppendLine(match);
        }

        if (truncated)
            sb.AppendLine("[truncated: maxMatches reached]");
        return run.RecordToolResult(sb.ToString());
    }

    public async Task<string> ReadSourceAsync(
        string? threadId,
        string sourceId,
        int offset = 1,
        int limit = DefaultReadLimit,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRun(threadId, out var run, out var error))
            return error;
        if (string.IsNullOrWhiteSpace(sourceId))
            return "Error: ReadDreamSource requires sourceId.";
        if (!TryStartRead(run, out error))
            return error;

        var normalizedSourceId = NormalizeSourceId(sourceId);
        if (!IsAllowedSource(run, normalizedSourceId))
            return $"Error: Source is not eligible for this Dream Run: {sourceId}";

        var content = await ReadSourceContentAsync(run, normalizedSourceId, cancellationToken).ConfigureAwait(false);
        if (content == null)
            return $"Error: Source not found: {sourceId}";

        run.RecordEvidenceSource(normalizedSourceId);
        offset = Math.Max(1, offset);
        limit = Math.Clamp(limit <= 0 ? DefaultReadLimit : limit, 1, MaxReadLimit);

        var lines = SplitLines(content).ToArray();
        if (lines.Length == 0)
            return run.RecordToolResult($"{normalizedSourceId}\n(empty)");
        if (offset > lines.Length)
            return run.RecordToolResult($"Error: Offset {offset} is out of range for {normalizedSourceId} ({lines.Length} lines).");

        var end = Math.Min(lines.Length, offset + limit - 1);
        var sb = new StringBuilder();
        sb.AppendLine($"{normalizedSourceId} lines {offset}-{end} of {lines.Length}:");
        for (var i = offset - 1; i < end; i++)
            sb.AppendLine($"{i + 1}: {TrimLine(lines[i], 2_000)}");
        if (end < lines.Length)
            sb.AppendLine($"[more available: call ReadDreamSource with offset={end + 1}]");
        else
            sb.AppendLine("[end of source]");

        return run.RecordToolResult(sb.ToString());
    }

    private async Task<string?> ReadSourceContentAsync(
        RegisteredDreamRun run,
        string sourceId,
        CancellationToken cancellationToken)
    {
        return sourceId switch
        {
            "memory:MEMORY.md" => memoryStore.ReadLongTerm(),
            "memory:HISTORY.md" => memoryStore.ReadHistory(),
            "dream:index" => dreamStore.ReadDream(),
            _ when sourceId.StartsWith("dream:memory/", StringComparison.Ordinal) =>
                dreamStore.ReadTopicFile(sourceId["dream:memory/".Length..]),
            _ when sourceId.StartsWith("thread:", StringComparison.Ordinal) =>
                await ReadThreadTranscriptAsync(run, sourceId["thread:".Length..], cancellationToken).ConfigureAwait(false),
            _ => null
        };
    }

    private async Task<string?> ReadThreadTranscriptAsync(string threadId, CancellationToken cancellationToken)
    {
        var thread = await threadStore.LoadThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        return thread == null ? null : FormatThreadTranscript(thread);
    }

    private async Task<string?> ReadThreadTranscriptAsync(
        RegisteredDreamRun run,
        string threadId,
        CancellationToken cancellationToken)
    {
        if (run.Input.Threads.All(t => !string.Equals(t.ThreadId, threadId, StringComparison.Ordinal)))
            return null;

        var thread = await threadStore.LoadThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (thread == null)
            return null;

        return FormatThreadTranscript(thread);
    }

    private static string FormatThreadTranscript(SessionThread thread)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Thread: {thread.DisplayName ?? thread.Id}");
        sb.AppendLine($"ThreadId: {thread.Id}");
        sb.AppendLine($"Origin: {thread.OriginChannel}");
        sb.AppendLine($"Status: {thread.Status}");
        sb.AppendLine($"CreatedAt: {thread.CreatedAt:O}");
        sb.AppendLine($"LastActiveAt: {thread.LastActiveAt:O}");
        sb.AppendLine();

        foreach (var turn in thread.Turns.Where(static turn => turn.Status == TurnStatus.Completed)
                     .OrderBy(static turn => turn.StartedAt))
        {
            sb.AppendLine($"TURN {turn.Id} ({turn.StartedAt:O})");
            foreach (var item in turn.Items)
                AppendTranscriptItem(sb, item);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static async Task WriteInputFileAsync(
        string inputDir,
        string relativePath,
        string content,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.Combine(inputDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content ?? string.Empty, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string BuildManifest(
        string runId,
        DreamsRunInput input,
        string workspacePath,
        string inputDir,
        DreamStoreDescriptor outputStore)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Dream Run Input Manifest");
        sb.AppendLine();
        sb.AppendLine($"RunId: {runId}");
        sb.AppendLine($"WorkspacePath: {workspacePath}");
        sb.AppendLine($"ReadOnlyInputPath: {inputDir}");
        sb.AppendLine($"WritableOutputStorePath: {outputStore.DirectoryPath}");
        sb.AppendLine($"WritableOutputIndex: {outputStore.IndexPath}");
        sb.AppendLine($"WritableOutputTopics: {outputStore.TopicDirectoryPath}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(input.AdditionalInstructions))
        {
            sb.AppendLine("## Additional Instructions");
            sb.AppendLine(input.AdditionalInstructions.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("## Candidate Sessions");
        if (input.Threads.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            foreach (var thread in input.Threads)
            {
                sb.AppendLine($"- sessions/{SafeFileName(thread.ThreadId)}.md | {thread.DisplayName ?? thread.ThreadId} | {thread.OriginChannel} | {thread.CompletedTurnCount} completed turns | lastActiveAt={thread.LastActiveAt:O}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Memory Sources");
        sb.AppendLine("- memory/MEMORY.md");
        sb.AppendLine("- memory/HISTORY.md");
        sb.AppendLine("- active-dream-store/INDEX.md when present");
        sb.AppendLine("- active-dream-store/memory/*.md when present");
        return sb.ToString();
    }

    private static string SafeFileName(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')
                sb.Append(ch);
            else
                sb.Append('_');
        }

        return sb.Length == 0 ? "source" : sb.ToString();
    }

    private static void AppendTranscriptItem(StringBuilder sb, SessionItem item)
    {
        switch (item.Payload)
        {
            case UserMessagePayload { Text: { } text } when !string.IsNullOrWhiteSpace(text):
                sb.AppendLine($"USER: {text.Trim()}");
                break;
            case AgentMessagePayload { Text: { } text } when !string.IsNullOrWhiteSpace(text):
                sb.AppendLine($"ASSISTANT: {text.Trim()}");
                break;
            case ToolCallPayload call:
                sb.AppendLine($"TOOL_CALL {call.ToolName}: {TrimLine(call.Arguments?.ToJsonString() ?? "{}", MaxSnippetChars)}");
                break;
            case ToolResultPayload result when !string.IsNullOrWhiteSpace(result.Result):
                sb.AppendLine($"TOOL_RESULT success={result.Success}: {TrimLine(result.Result, MaxSnippetChars)}");
                break;
        }
    }

    private static IEnumerable<DreamSourceDescriptor> EnumerateSources(
        RegisteredDreamRun run,
        IReadOnlySet<string>? sourceTypes,
        IReadOnlySet<string>? threadIds)
    {
        if (IncludesType(sourceTypes, "memory"))
        {
            yield return new DreamSourceDescriptor("memory:MEMORY.md", "memory", null);
            yield return new DreamSourceDescriptor("memory:HISTORY.md", "memory", null);
        }

        if (IncludesType(sourceTypes, "dream"))
            yield return new DreamSourceDescriptor("dream:index", "dream", null);

        if (IncludesType(sourceTypes, "topic"))
        {
            foreach (var topic in run.Input.TopicFiles)
                yield return new DreamSourceDescriptor($"dream:memory/{topic.Path}", "topic", null);
        }

        if (IncludesType(sourceTypes, "session") || IncludesType(sourceTypes, "thread"))
        {
            foreach (var thread in run.Input.Threads)
            {
                if (threadIds is { Count: > 0 } && !threadIds.Contains(thread.ThreadId))
                    continue;
                yield return new DreamSourceDescriptor($"thread:{thread.ThreadId}", "session", thread.ThreadId);
            }
        }
    }

    private static bool IsAllowedSource(RegisteredDreamRun run, string sourceId)
    {
        if (sourceId is "memory:MEMORY.md" or "memory:HISTORY.md" or "dream:index")
            return true;
        if (sourceId.StartsWith("dream:memory/", StringComparison.Ordinal))
        {
            var path = sourceId["dream:memory/".Length..];
            return run.Input.TopicFiles.Any(topic => string.Equals(topic.Path, path, StringComparison.OrdinalIgnoreCase));
        }
        if (sourceId.StartsWith("thread:", StringComparison.Ordinal))
        {
            var threadId = sourceId["thread:".Length..];
            return run.Input.Threads.Any(thread => string.Equals(thread.ThreadId, threadId, StringComparison.Ordinal));
        }

        return false;
    }

    private static string NormalizeSourceId(string sourceId) =>
        sourceId.Trim().Replace('\\', '/');

    private static IReadOnlySet<string>? BuildSet(IReadOnlyList<string>? values)
    {
        var filtered = values?
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToArray();
        return filtered is { Length: > 0 }
            ? filtered.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;
    }

    private static bool IncludesType(IReadOnlySet<string>? sourceTypes, string type) =>
        sourceTypes == null
        || sourceTypes.Contains(type)
        || (string.Equals(type, "session", StringComparison.OrdinalIgnoreCase) && sourceTypes.Contains("thread"));

    private static IEnumerable<string> SplitLines(string value) =>
        value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static string TrimLine(string value, int maxChars)
    {
        var singleLine = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return singleLine.Length <= maxChars ? singleLine : singleLine[..maxChars] + "...";
    }

    private bool TryGetRun(string? threadId, out RegisteredDreamRun run, out string error)
    {
        run = null!;
        if (string.IsNullOrWhiteSpace(threadId))
        {
            error = "Error: Dreams tool is not bound to a session thread.";
            return false;
        }

        if (!_runs.TryGetValue(threadId, out run!))
        {
            error = "Error: Dreams run context is not active for this thread.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryStartSearch(RegisteredDreamRun run, out string error)
    {
        lock (run.SyncRoot)
        {
            if (run.SearchCount >= MaxSearches)
            {
                error = "Error: Dreams evidence search budget exhausted.";
                return false;
            }

            run.SearchCount++;
            error = string.Empty;
            return true;
        }
    }

    private static bool TryStartRead(RegisteredDreamRun run, out string error)
    {
        lock (run.SyncRoot)
        {
            if (run.ReadCount >= MaxReads)
            {
                error = "Error: Dreams evidence read budget exhausted.";
                return false;
            }

            run.ReadCount++;
            error = string.Empty;
            return true;
        }
    }

    private sealed class RegisteredDreamRun(
        string runId,
        DreamsRunInput input,
        DreamsRunWorkspace? workspace = null)
    {
        public string RunId { get; } = runId;

        public DreamsRunInput Input { get; } = input;

        public DreamsRunWorkspace? Workspace { get; } = workspace;

        public object SyncRoot { get; } = new();

        public int SearchCount { get; set; }

        public int ReadCount { get; set; }

        public int EvidenceChars { get; set; }

        public HashSet<string> EvidenceThreadIds { get; } = new(StringComparer.Ordinal);

        public void RecordEvidenceSource(string sourceId)
        {
            if (!sourceId.StartsWith("thread:", StringComparison.Ordinal))
                return;

            lock (SyncRoot)
                EvidenceThreadIds.Add(sourceId["thread:".Length..]);
        }

        public string RecordToolResult(string text)
        {
            lock (SyncRoot)
            {
                if (EvidenceChars >= MaxTotalEvidenceChars)
                    return "Error: Dreams total evidence budget exhausted.";

                var remaining = MaxTotalEvidenceChars - EvidenceChars;
                var maxChars = Math.Min(MaxToolResultChars, remaining);
                var result = text.Length <= maxChars
                    ? text
                    : text[..maxChars] + "\n[truncated: Dreams evidence budget limit reached]";
                EvidenceChars += result.Length;
                return result;
            }
        }
    }

    private readonly record struct DreamSourceDescriptor(string SourceId, string SourceType, string? ThreadId);
}
