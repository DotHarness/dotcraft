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

        memoryStore.EnsureHistoryFile();
        var memoryFileExisted = File.Exists(memoryStore.LongTermFilePath);
        var currentMemory = memoryStore.ReadLongTerm();
        var currentHistory = memoryStore.ReadHistory();
        var fallbackReason = GetFallbackReason(snapshot);
        if (fallbackReason is not null)
            return await fallback.ConsolidateAsync(
                TrimForFallback(messagesToArchive, currentMemory),
                cancellationToken);

        var policy = new MemoryConsolidationToolPolicy(memoryStore, ResolveWorkspaceRoot());
        var result = await forkRunner.RunAsync(
            snapshot!,
            new MaintenanceForkTask(
                MaintenanceForkTaskKind.MemoryConsolidation,
                BuildTaskInstructions(memoryStore.LongTermFilePath, memoryStore.HistoryFilePath)),
            messagesBeforeTask: null,
            new MaintenanceForkToolExecutionOptions(policy.Evaluate)
            {
                IncludeDetailedErrors = true
            },
            cancellationToken);

        if (!TryEvaluateFileWrites(currentMemory, memoryFileExisted, currentHistory, out var fileResult))
        {
            return await fallback.ConsolidateAsync(
                TrimForFallback(messagesToArchive, currentMemory),
                cancellationToken);
        }

        if (fileResult != null)
            return fileResult;

        if (result.FallbackReason is not null)
            return await fallback.ConsolidateAsync(
                TrimForFallback(messagesToArchive, currentMemory),
                cancellationToken);

        if (TryParseNoChangesStatus(result.Text))
            return MemoryConsolidationResult.Skipped("no_memory_changes");

        if (!TryParseStructuredResult(result.Text, out var historyEntry, out var memoryUpdate))
            return await fallback.ConsolidateAsync(
                TrimForFallback(messagesToArchive, currentMemory),
                cancellationToken);

        var write = memoryStore.SaveConsolidation(historyEntry, memoryUpdate);
        return write.AnyWritten
            ? MemoryConsolidationResult.Succeeded(write.MemoryWritten, write.HistoryWritten)
            : MemoryConsolidationResult.Skipped("no_memory_changes");
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

    private bool TryEvaluateFileWrites(
        string previousMemory,
        bool previousMemoryFileExisted,
        string previousHistory,
        out MemoryConsolidationResult? result)
    {
        result = null;
        var currentHistory = memoryStore.ReadHistory();
        if (!currentHistory.StartsWith(previousHistory, StringComparison.Ordinal))
        {
            memoryStore.RestoreLongTermForConsolidation(previousMemory, previousMemoryFileExisted);
            memoryStore.RestoreHistoryForConsolidation(previousHistory);
            return false;
        }

        var memoryWritten = !string.Equals(memoryStore.ReadLongTerm(), previousMemory, StringComparison.Ordinal);
        var historyTail = currentHistory[previousHistory.Length..];
        var historyWritten = !string.IsNullOrWhiteSpace(historyTail);
        result = memoryWritten || historyWritten
            ? MemoryConsolidationResult.Succeeded(memoryWritten, historyWritten)
            : null;
        return true;
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
        string historyFilePath)
    {
        return $$"""
Consolidate durable memory from the completed conversation.

Memory files:
- MEMORY.md: {{FormatPathForPrompt(memoryFilePath)}}
- HISTORY.md: {{FormatPathForPrompt(historyFilePath)}}

Tool rules:
- Use file tools only for the two memory files above.
- MEMORY.md may be replaced with the complete updated markdown.
- HISTORY.md is append-only. Append at most one timestamped grep-searchable event paragraph.
- Do not read or modify other workspace files, run shell commands, browse the web, spawn agents, or update goals/todos.

Allowed output:
- Prefer editing the files directly, then return a short JSON status such as {"status":"updated"}.
- If nothing durable was learned, leave the files unchanged and return {"status":"unchanged"}.
- Do not include the full MEMORY.md or HISTORY.md contents in the final response.
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
        catch (JsonException)
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
        catch (JsonException)
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
