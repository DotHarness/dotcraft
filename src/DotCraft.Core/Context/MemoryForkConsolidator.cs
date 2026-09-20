using System.Text.Json;
using DotCraft.Context.Compaction;
using DotCraft.Memory;
using Microsoft.Extensions.AI;

namespace DotCraft.Context;

/// <summary>
/// Memory consolidator that prefers a same-model prompt fork and falls back to
/// the legacy consolidator when the fork contract is unavailable.
/// </summary>
public sealed class MemoryForkConsolidator(
    MaintenanceForkRunner forkRunner,
    IMemoryConsolidator fallback,
    MemoryStore memoryStore,
    string? mainModelId,
    string? consolidationModelId,
    int fallbackInputTokenBudget = 0,
    string? workspaceRoot = null) : IMemoryForkConsolidator
{
    /// <inheritdoc />
    public Task<MemoryConsolidationResult> ConsolidateAsync(
        IReadOnlyList<ChatMessage> messagesToArchive,
        CancellationToken cancellationToken = default) =>
        ConsolidateAsync(messagesToArchive, snapshot: null, cancellationToken);

    /// <inheritdoc />
    public async Task<MemoryConsolidationResult> ConsolidateAsync(
        IReadOnlyList<ChatMessage> messagesToArchive,
        PromptRequestSnapshot? snapshot,
        CancellationToken cancellationToken = default)
    {
        if (messagesToArchive.Count == 0)
            return MemoryConsolidationResult.Skipped("empty_snapshot");

        cancellationToken.ThrowIfCancellationRequested();
        var current = memoryStore.CaptureSnapshot(ensureHistoryFile: true);
        if (GetFallbackReason(snapshot) is not null)
            return await RunFallbackAsync();

        var policy = new MemoryConsolidationToolPolicy(memoryStore, ResolveWorkspaceRoot());
        var result = await forkRunner.RunAsync(
            snapshot!,
            new MaintenanceForkTask(
                MaintenanceForkTaskKind.MemoryConsolidation,
                BuildTaskInstructions(memoryStore.LongTermFilePath, memoryStore.HistoryFilePath, current.Memory)),
            messagesBeforeTask: null,
            new MaintenanceForkToolExecutionOptions(policy.Evaluate) { IncludeDetailedErrors = true },
            cancellationToken);

        if (result.FallbackReason is not null)
            return await RunFallbackAsync();
        if (TryParseNoChangesStatus(result.Text))
            return MemoryConsolidationResult.Skipped("no_memory_changes");
        if (!TryParseStructuredResult(result.Text, out var historyEntry, out var memoryUpdate))
            return await RunFallbackAsync();

        cancellationToken.ThrowIfCancellationRequested();
        var commit = memoryStore.TrySaveConsolidation(current, historyEntry, memoryUpdate);
        if (commit.Outcome == MemoryStoreCommitOutcome.Reset)
            return MemoryConsolidationResult.Skipped("memory_reset");
        if (commit.Outcome == MemoryStoreCommitOutcome.Conflict)
            return MemoryConsolidationResult.Skipped("memory_version_conflict");
        return commit.Write.AnyWritten
            ? MemoryConsolidationResult.Succeeded(commit.Write.MemoryWritten, commit.Write.HistoryWritten)
            : MemoryConsolidationResult.Skipped("no_memory_changes");

        async Task<MemoryConsolidationResult> RunFallbackAsync()
        {
            if (memoryStore.CaptureSnapshot().Generation != current.Generation)
                return MemoryConsolidationResult.Skipped("memory_reset");
            var messages = TrimForFallback(messagesToArchive, current.Memory ?? string.Empty);
            return fallback is MemoryConsolidator consolidator
                ? await consolidator.ConsolidateAsync(messages, current.Generation, cancellationToken)
                : await fallback.ConsolidateAsync(messages, cancellationToken);
        }
    }

    private string? GetFallbackReason(PromptRequestSnapshot? snapshot)
    {
        if (snapshot is null)
            return "snapshot_unavailable";

        if (!string.IsNullOrWhiteSpace(mainModelId)
            && !string.IsNullOrWhiteSpace(consolidationModelId)
            && !string.Equals(mainModelId, consolidationModelId, StringComparison.Ordinal))
        {
            return "different_consolidation_model";
        }

        return null;
    }

    private string ResolveWorkspaceRoot()
    {
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
            return workspaceRoot!;

        var memoryDirectory = Path.GetFullPath(memoryStore.MemoryDirectoryPath);
        return Path.GetDirectoryName(memoryDirectory) ?? memoryDirectory;
    }

    private IReadOnlyList<ChatMessage> TrimForFallback(
        IReadOnlyList<ChatMessage> messages,
        string currentMemory)
    {
        if (fallbackInputTokenBudget <= 0)
            return messages;

        var candidate = messages.ToList();
        while (candidate.Count > 0 && EstimateFallbackRequest(candidate, currentMemory) > fallbackInputTokenBudget)
        {
            var trimmed = CompactionMessageTruncator.TruncateOldestGroups(candidate);
            if (trimmed.Count == 0 || trimmed.Count >= candidate.Count)
                break;

            candidate = trimmed;
        }

        return candidate;
    }

    private static int EstimateFallbackRequest(
        IReadOnlyList<ChatMessage> messages,
        string currentMemory)
    {
        const int promptOverheadTokens = 2_000;
        var estimate = (long)MessageTokenEstimator.Estimate(messages)
            + MessageTokenEstimator.RoughTokenCount(currentMemory)
            + promptOverheadTokens;
        return (int)Math.Min(int.MaxValue, estimate);
    }

    private static string BuildTaskInstructions(
        string memoryFilePath,
        string historyFilePath,
        string? currentMemory)
    {
        return $$"""
Consolidate durable memory from the completed conversation.

Memory files:
- MEMORY.md: {{FormatPathForPrompt(memoryFilePath)}}
- HISTORY.md: {{FormatPathForPrompt(historyFilePath)}}

Tool rules:
- Only read or search the two memory files above. Do not write or edit files.
- Do not access other workspace files, run shell commands, browse the web, spawn agents, or update goals/todos.

Return candidate changes as JSON: {"history_entry":"[YYYY-MM-DD HH:MM] event paragraph","memory_update":"complete updated MEMORY.md markdown"}.
The host checks for concurrent changes before applying the candidate. HISTORY.md is append-only; return at most one new timestamped paragraph, not its existing contents.
Preserve explicitly saved information, including test records, unless the user corrected or removed it. Do not restore facts the user asked to forget.
If nothing changed, return {"status":"unchanged"}.
Use the current memory below rather than any cached memory earlier in the conversation:

{{currentMemory ?? "(empty)"}}
""";
    }

    private static string FormatPathForPrompt(string path) =>
        Path.GetFullPath(path).Replace('\\', '/');

    private static bool TryParseNoChangesStatus(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(ExtractJsonObject(text));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (!root.TryGetProperty("status", out var statusElement))
                return false;

            var status = statusElement.GetString();
            return string.Equals(status, "unchanged", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "no_changes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "no_memory_changes", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryParseStructuredResult(
        string? text,
        out string? historyEntry,
        out string? memoryUpdate)
    {
        historyEntry = null;
        memoryUpdate = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(ExtractJsonObject(text));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (root.TryGetProperty("history_entry", out var historyElement))
                historyEntry = historyElement.GetString();
            if (root.TryGetProperty("memory_update", out var memoryElement))
                memoryUpdate = memoryElement.GetString();

            return !string.IsNullOrWhiteSpace(historyEntry)
                || !string.IsNullOrWhiteSpace(memoryUpdate);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static string ExtractJsonObject(string text)
    {
        var trimmed = text.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end >= start
            ? trimmed[start..(end + 1)]
            : trimmed;
    }

}
