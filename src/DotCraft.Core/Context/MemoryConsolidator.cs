using System.Text;
using System.Text.Json;
using DotCraft.Memory;
using Microsoft.Extensions.AI;
using Spectre.Console;

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
    public async Task<MemoryConsolidationResult> ConsolidateAsync(
        IReadOnlyList<ChatMessage> messagesToArchive,
        CancellationToken cancellationToken = default)
    {
        if (messagesToArchive.Count == 0)
            return MemoryConsolidationResult.Skipped("empty_snapshot");

        var currentMemory = memoryStore.ReadLongTerm();
        var conversationText = FormatMessages(messagesToArchive);
        var prompt =
            $$"""
            Consolidate durable memory from this conversation.

            Return JSON text only with this exact object shape:
            {"history_entry":"[YYYY-MM-DD HH:MM] 2-5 sentence grep-searchable event paragraph","memory_update":"full updated MEMORY.md markdown"}

            If nothing new was learned, set memory_update to the current memory unchanged and leave history_entry empty.

            ## Current Long-term Memory
            {{(string.IsNullOrWhiteSpace(currentMemory) ? "(empty)" : currentMemory)}}

            ## Conversation to Process
            {{conversationText}}
            """;

        try
        {
            var response = await chatClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, prompt)],
                new ChatOptions { Instructions = SystemPrompt },
                cancellationToken);

            if (!TryParseStructuredResult(response.Text, out var historyEntry, out var memoryUpdate))
            {
                onStatus?.Invoke("[grey][[Memory]][/] [yellow]Consolidation: LLM did not return memory JSON, skipping.[/]");
                return MemoryConsolidationResult.Skipped("memory_json_not_returned");
            }

            var result = memoryStore.SaveConsolidation(historyEntry, memoryUpdate);
            if (!result.AnyWritten)
            {
                onStatus?.Invoke("[grey][[Memory]][/] [yellow]Consolidation: no memory changes, skipping.[/]");
                return MemoryConsolidationResult.Skipped("no_memory_changes");
            }

            onStatus?.Invoke("[grey][[Memory]][/] [green]Consolidation complete.[/]");
            return MemoryConsolidationResult.Succeeded(result.MemoryWritten, result.HistoryWritten);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            onStatus?.Invoke("[grey][[Memory]][/] [red]Consolidation failed: provider_timeout[/]");
            return MemoryConsolidationResult.Failed("provider_timeout");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onStatus?.Invoke($"[grey][[Memory]][/] [red]Consolidation failed: {Markup.Escape(ex.Message)}[/]");
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
                onStatus?.Invoke($"[grey][[Memory]][/] [red]Background consolidation error: {Markup.Escape(ex.Message)}[/]");
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
