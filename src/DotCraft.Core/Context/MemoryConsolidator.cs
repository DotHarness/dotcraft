using System.Text;
using System.Text.Json;
using DotCraft.Memory;
using Microsoft.Extensions.AI;

namespace DotCraft.Context;

/// <summary>
/// Consolidates thread history into dual-layer long-term memory:
/// MEMORY.md is updated with durable facts, and HISTORY.md receives a grep-searchable event paragraph.
/// </summary>
public sealed class MemoryConsolidator(
    IChatClient chatClient,
    MemoryStore memoryStore,
    Action<string>? onStatus = null)
    : IMemoryConsolidator
{
    private const string SystemPrompt =
        "You are a memory consolidation agent. Return JSON only.";

    /// <summary>
    /// Consolidate the given thread-history snapshot into MEMORY.md and HISTORY.md.
    /// Runs the LLM consolidation call and writes results to disk.
    /// </summary>
    public Task<MemoryConsolidationResult> ConsolidateAsync(
        IReadOnlyList<ChatMessage> messagesToArchive,
        CancellationToken cancellationToken = default) =>
        ConsolidateAsync(messagesToArchive, memoryStore.CaptureSnapshot().Generation, cancellationToken);

    internal async Task<MemoryConsolidationResult> ConsolidateAsync(
        IReadOnlyList<ChatMessage> messagesToArchive,
        long generation,
        CancellationToken cancellationToken)
    {
        if (messagesToArchive.Count == 0)
            return MemoryConsolidationResult.Skipped("empty_snapshot");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = memoryStore.CaptureSnapshot();
            if (snapshot.Generation != generation)
                return MemoryConsolidationResult.Skipped("memory_reset");
            var prompt = $$"""
                Consolidate durable memory from this conversation. Preserve explicitly saved information, including test records, unless the user corrected or removed it. Do not restore facts the user asked to forget.
                Return JSON only: {"history_entry":"[YYYY-MM-DD HH:MM] 2-5 sentence grep-searchable event paragraph","memory_update":"full updated MEMORY.md markdown"}.
                If nothing new was learned, leave history_entry empty and return the current memory unchanged.

                ## Current Long-term Memory
                {{snapshot.Memory ?? "(empty)"}}

                ## Conversation to Process
                {{FormatMessages(messagesToArchive)}}
                """;
            var response = await chatClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, prompt)],
                new ChatOptions { Instructions = SystemPrompt }, cancellationToken);

            if (!TryParseStructuredResult(response.Text, out var historyEntry, out var memoryUpdate))
            {
                onStatus?.Invoke("[Memory] Consolidation skipped: the model did not return memory JSON.");
                return MemoryConsolidationResult.Skipped("memory_json_not_returned");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var commit = memoryStore.TrySaveConsolidation(snapshot, historyEntry, memoryUpdate);
            if (commit.Outcome == MemoryStoreCommitOutcome.Reset)
                return MemoryConsolidationResult.Skipped("memory_reset");
            if (commit.Outcome == MemoryStoreCommitOutcome.Conflict)
                return MemoryConsolidationResult.Skipped("memory_version_conflict");

            onStatus?.Invoke(commit.Write.AnyWritten
                ? "[Memory] Consolidation complete."
                : "[Memory] Consolidation skipped: no memory changes.");
            return commit.Write.AnyWritten
                ? MemoryConsolidationResult.Succeeded(commit.Write.MemoryWritten, commit.Write.HistoryWritten)
                : MemoryConsolidationResult.Skipped("no_memory_changes");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            onStatus?.Invoke("[Memory] Consolidation failed: provider_timeout");
            return MemoryConsolidationResult.Failed("provider_timeout");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onStatus?.Invoke($"[Memory] Consolidation failed: {ex.Message}");
            return MemoryConsolidationResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Fire-and-forget consolidation that does not block the caller.
    /// </summary>
    public void ConsolidateInBackground(IReadOnlyList<ChatMessage> messagesToArchive)
    {
        if (messagesToArchive.Count == 0)
            return;

        var snapshot = messagesToArchive.ToList();
        _ = Task.Run(async () =>
        {
            try
            {
                await ConsolidateAsync(snapshot);
            }
            catch (Exception ex)
            {
                onStatus?.Invoke($"[Memory] Background consolidation error: {ex.Message}");
            }
        });
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

    private static string FormatMessages(IReadOnlyList<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        var now = DateTime.Now;

        foreach (var msg in messages)
        {
            var role = msg.Role == ChatRole.User ? "USER"
                : msg.Role == ChatRole.Assistant ? "ASSISTANT"
                : msg.Role.ToString().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(msg.Text))
                continue;

            sb.AppendLine($"[{now:yyyy-MM-dd}] {role}: {msg.Text.Trim()}");
        }

        return sb.ToString();
    }
}
