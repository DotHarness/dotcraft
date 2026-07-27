using System.ClientModel.Primitives;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Protocol;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace DotCraft.Agents;

internal interface IProviderConversationHistoryBridge
{
    ValueTask HistoryReplacedAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        string reason,
        CancellationToken cancellationToken);

    void MarkProjectionCovered(IReadOnlyList<ChatMessage> messages);

    string? BeginAttempt();

    ValueTask AbortAttemptAsync(string? attemptId, CancellationToken cancellationToken);

    void EndAttempt(string? attemptId);
}

internal sealed class OpenAIResponsesProviderHistoryBridge : IProviderConversationHistoryBridge
{
    public static OpenAIResponsesProviderHistoryBridge Instance { get; } = new();

    private OpenAIResponsesProviderHistoryBridge()
    {
    }

    public ValueTask HistoryReplacedAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        string reason,
        CancellationToken cancellationToken) =>
        OpenAIResponsesProviderHistoryRuntimeScope.Current is { } context
            ? context.ReplaceAsync(messages, options, reason, cancellationToken)
            : ValueTask.CompletedTask;

    public void MarkProjectionCovered(IReadOnlyList<ChatMessage> messages) =>
        OpenAIResponsesProviderHistoryRuntimeScope.Current?.MarkProjectionCovered(messages);

    public string? BeginAttempt() =>
        OpenAIResponsesProviderHistoryRuntimeScope.Current?.BeginAttempt();

    public ValueTask AbortAttemptAsync(string? attemptId, CancellationToken cancellationToken) =>
        OpenAIResponsesProviderHistoryRuntimeScope.Current is { } context
            ? context.AbortAttemptAsync(attemptId, cancellationToken)
            : ValueTask.CompletedTask;

    public void EndAttempt(string? attemptId) =>
        OpenAIResponsesProviderHistoryRuntimeScope.Current?.EndAttempt(attemptId);
}

internal sealed class OpenAIResponsesProviderHistoryContext
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ThreadConversationIdentity _identity;
    private readonly Func<ProviderHistoryItemsAppendedPayload, CancellationToken, Task>? _appendAsync;
    private readonly Func<ProviderHistoryReplacedPayload, CancellationToken, Task>? _replaceAsync;
    private readonly Func<ProviderHistoryAttemptAbortedPayload, CancellationToken, Task>? _abortAsync;
    private readonly List<RuntimeEntry> _entries;
    private int _coveredMessageCount;
    private string _generationId;
    private string _contextWindowId;
    private string? _coveredThroughTurnId;
    private string? _currentAttemptId;

    public OpenAIResponsesProviderHistoryContext(
        ThreadConversationIdentity identity,
        ProviderHistorySnapshot snapshot,
        int coveredMessageCount,
        Func<ProviderHistoryItemsAppendedPayload, CancellationToken, Task>? appendAsync,
        Func<ProviderHistoryReplacedPayload, CancellationToken, Task>? replaceAsync,
        Func<ProviderHistoryAttemptAbortedPayload, CancellationToken, Task>? abortAsync)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        ArgumentNullException.ThrowIfNull(snapshot);
        _generationId = snapshot.GenerationId;
        _contextWindowId = snapshot.ContextWindowId;
        _coveredThroughTurnId = snapshot.CoveredThroughTurnId;
        _coveredMessageCount = Math.Max(0, coveredMessageCount);
        _appendAsync = appendAsync;
        _replaceAsync = replaceAsync;
        _abortAsync = abortAsync;
        _entries = snapshot.Entries
            .Select(entry => new RuntimeEntry(ProviderHistoryReplayer.CloneEntry(entry), AttemptId: null))
            .ToList();
    }

    public async ValueTask<CanonicalResponsesInput> PrepareInputAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_coveredMessageCount > messages.Count)
            {
                throw new InvalidDataException(
                    "responses_provider_history_corrupt: MEAI sampling history is shorter than its canonical coverage.");
            }

            var tail = messages.Skip(_coveredMessageCount).ToList();
            if (tail.Count > 0)
            {
                var correlations = BuildCallCorrelationIndex();
                var mapped = ResponsesToolSearchMapper.BuildInputItems(
                    tail,
                    options,
                    correlations,
                    itemOrdinalOffset: _entries.Count);
                var entries = CreateEntries(
                    mapped.Input,
                    ProviderHistorySources.LocalInput,
                    attemptId: null);
                if (entries.Count > 0)
                {
                    await PersistAppendAsync(
                            entries,
                            ProviderHistorySources.LocalInput,
                            attemptId: null,
                            cancellationToken)
                        .ConfigureAwait(false);
                    _entries.AddRange(entries.Select(entry => new RuntimeEntry(entry, AttemptId: null)));
                    _coveredThroughTurnId = _identity.TurnId;
                }
            }

            _coveredMessageCount = messages.Count;
            var input = BuildInputArray();
            return new CanonicalResponsesInput(
                input,
                OpenAIResponsesItemIdentityDiagnostics.FromInput(input));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask AppendProviderOutputAsync(
        ResponseItem item,
        int outputIndex,
        long sequenceNumber,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var raw = ModelReaderWriter.Write(item).ToString();
        using var document = JsonDocument.Parse(raw);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var attemptId = _currentAttemptId ??= NewAttemptId();
            var entryId = CreateEntryId(
                ProviderHistorySources.ProviderOutput,
                attemptId,
                outputIndex,
                sequenceNumber,
                document.RootElement.GetRawText());
            if (_entries.Any(entry => string.Equals(entry.Entry.EntryId, entryId, StringComparison.Ordinal)))
                return;

            var entry = new ProviderHistoryEntry
            {
                EntryId = entryId,
                Item = document.RootElement.Clone()
            };
            await PersistAppendAsync(
                    [entry],
                    ProviderHistorySources.ProviderOutput,
                    attemptId,
                    cancellationToken)
                .ConfigureAwait(false);
            _entries.Add(new RuntimeEntry(entry, attemptId));
            _coveredThroughTurnId = _identity.TurnId;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask ReplaceAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        string reason,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var mapped = ResponsesToolSearchMapper.BuildInputItems(messages, options);
            var entries = CreateEntries(mapped.Input, ProviderHistorySources.LocalInput, attemptId: null);
            var currentIdentity = OpenAIResponsesCodexRuntimeScope.Current?.ConversationIdentity ?? _identity;
            var windowId = currentIdentity.ContextWindowId;
            var generationId = windowId;
            var replacement = new ProviderHistoryReplacedPayload
            {
                SchemaVersion = ProviderHistorySchema.CurrentSchemaVersion,
                ThreadId = _identity.CurrentThreadId,
                Protocol = ProviderHistorySchema.OpenAIResponsesProtocol,
                GenerationId = generationId,
                ContextWindowId = windowId,
                CoveredThroughTurnId = _identity.TurnId,
                Reason = string.IsNullOrWhiteSpace(reason) ? "history_replaced" : reason,
                Entries = entries
            };
            if (_replaceAsync != null)
                await _replaceAsync(replacement, cancellationToken).ConfigureAwait(false);

            _entries.Clear();
            _entries.AddRange(entries.Select(entry => new RuntimeEntry(entry, AttemptId: null)));
            _generationId = generationId;
            _contextWindowId = windowId;
            _coveredThroughTurnId = _identity.TurnId;
            _coveredMessageCount = messages.Count;
            _currentAttemptId = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void MarkProjectionCovered(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _coveredMessageCount = messages.Count;
    }

    public string BeginAttempt()
    {
        var attemptId = NewAttemptId();
        _currentAttemptId = attemptId;
        return attemptId;
    }

    public async ValueTask AbortAttemptAsync(string? attemptId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(attemptId))
            return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_entries.Any(entry => string.Equals(entry.AttemptId, attemptId, StringComparison.Ordinal)))
            {
                if (string.Equals(_currentAttemptId, attemptId, StringComparison.Ordinal))
                    _currentAttemptId = null;
                return;
            }

            var payload = new ProviderHistoryAttemptAbortedPayload
            {
                SchemaVersion = ProviderHistorySchema.CurrentSchemaVersion,
                ThreadId = _identity.CurrentThreadId,
                TurnId = _identity.TurnId ?? string.Empty,
                Protocol = ProviderHistorySchema.OpenAIResponsesProtocol,
                GenerationId = _generationId,
                AttemptId = attemptId
            };
            if (_abortAsync != null)
                await _abortAsync(payload, cancellationToken).ConfigureAwait(false);

            _entries.RemoveAll(entry => string.Equals(entry.AttemptId, attemptId, StringComparison.Ordinal));
            if (string.Equals(_currentAttemptId, attemptId, StringComparison.Ordinal))
                _currentAttemptId = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void EndAttempt(string? attemptId)
    {
        if (string.Equals(_currentAttemptId, attemptId, StringComparison.Ordinal))
            _currentAttemptId = null;
    }

    public ProviderHistorySnapshot CaptureSnapshot() =>
        new(
            _generationId,
            _contextWindowId,
            _entries.Select(entry => ProviderHistoryReplayer.CloneEntry(entry.Entry)).ToList(),
            _coveredThroughTurnId);

    private async Task PersistAppendAsync(
        IReadOnlyList<ProviderHistoryEntry> entries,
        string source,
        string? attemptId,
        CancellationToken cancellationToken)
    {
        if (_appendAsync == null)
            return;

        var payload = new ProviderHistoryItemsAppendedPayload
        {
            SchemaVersion = ProviderHistorySchema.CurrentSchemaVersion,
            ThreadId = _identity.CurrentThreadId,
            TurnId = _identity.TurnId ?? string.Empty,
            Protocol = ProviderHistorySchema.OpenAIResponsesProtocol,
            GenerationId = _generationId,
            ContextWindowId = _contextWindowId,
            Source = source,
            AttemptId = attemptId,
            Entries = entries.Select(ProviderHistoryReplayer.CloneEntry).ToList()
        };
        await _appendAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private Dictionary<string, string> BuildCallCorrelationIndex()
    {
        var correlations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var runtimeEntry in _entries)
        {
            var item = runtimeEntry.Entry.Item;
            if (!item.TryGetProperty("call_id", out var callIdElement)
                || callIdElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(callIdElement.GetString()))
            {
                continue;
            }

            var type = item.TryGetProperty("type", out var typeElement)
                && typeElement.ValueKind == JsonValueKind.String
                    ? typeElement.GetString()
                    : null;
            var name = string.Equals(type, "tool_search_call", StringComparison.Ordinal)
                ? NativeToolSearchTool.ToolName
                : item.TryGetProperty("name", out var nameElement)
                  && nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString()
                    : null;
            if (!string.IsNullOrWhiteSpace(name))
                correlations[callIdElement.GetString()!] = name!;
        }
        return correlations;
    }

    private JsonArray BuildInputArray()
    {
        var input = new JsonArray();
        foreach (var entry in _entries)
            input.Add(JsonNode.Parse(entry.Entry.Item.GetRawText()));
        NormalizeCallOutputs(input);
        return input;
    }

    private static void NormalizeCallOutputs(JsonArray input)
    {
        var functionCalls = new HashSet<string>(StringComparer.Ordinal);
        var toolSearchCalls = new HashSet<string>(StringComparer.Ordinal);
        var functionOutputs = new HashSet<string>(StringComparer.Ordinal);
        var toolSearchOutputs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in input.OfType<JsonObject>())
        {
            var type = ReadString(item, "type");
            var callId = ReadString(item, "call_id");
            if (string.IsNullOrWhiteSpace(callId))
                continue;
            switch (type)
            {
                case "function_call":
                    functionCalls.Add(callId);
                    break;
                case "tool_search_call" when
                    !string.Equals(ReadString(item, "execution"), "server", StringComparison.Ordinal):
                    toolSearchCalls.Add(callId);
                    break;
                case "function_call_output":
                    functionOutputs.Add(callId);
                    break;
                case "tool_search_output" when
                    !string.Equals(ReadString(item, "execution"), "server", StringComparison.Ordinal):
                    toolSearchOutputs.Add(callId);
                    break;
            }
        }

        for (var i = input.Count - 1; i >= 0; i--)
        {
            if (input[i] is not JsonObject item)
                continue;
            var type = ReadString(item, "type");
            var callId = ReadString(item, "call_id");
            if (string.IsNullOrWhiteSpace(callId))
                continue;

            if (string.Equals(type, "function_call_output", StringComparison.Ordinal)
                && !functionCalls.Contains(callId))
            {
                input.RemoveAt(i);
                continue;
            }
            if (string.Equals(type, "tool_search_output", StringComparison.Ordinal)
                && !string.Equals(ReadString(item, "execution"), "server", StringComparison.Ordinal)
                && !toolSearchCalls.Contains(callId))
            {
                input.RemoveAt(i);
                continue;
            }

            if (string.Equals(type, "function_call", StringComparison.Ordinal)
                && !functionOutputs.Contains(callId))
            {
                input.Insert(i + 1, new JsonObject
                {
                    ["type"] = "function_call_output",
                    ["id"] = CreateSyntheticOutputId("fco", ReadString(item, "id"), callId),
                    ["call_id"] = callId,
                    ["output"] = "aborted"
                });
            }
            else if (string.Equals(type, "tool_search_call", StringComparison.Ordinal)
                     && !string.Equals(ReadString(item, "execution"), "server", StringComparison.Ordinal)
                     && !toolSearchOutputs.Contains(callId))
            {
                input.Insert(i + 1, new JsonObject
                {
                    ["type"] = "tool_search_output",
                    ["id"] = CreateSyntheticOutputId("tso", ReadString(item, "id"), callId),
                    ["execution"] = "client",
                    ["call_id"] = callId,
                    ["status"] = "completed",
                    ["tools"] = new JsonArray()
                });
            }
        }
    }

    private static string CreateSyntheticOutputId(string prefix, string? sourceId, string callId)
    {
        var bytes = Encoding.UTF8.GetBytes($"{prefix}\n{sourceId}\n{callId}");
        return prefix + "_" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..32];
    }

    private static string? ReadString(JsonObject item, string propertyName) =>
        item[propertyName] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;

    private static List<ProviderHistoryEntry> CreateEntries(
        JsonArray input,
        string source,
        string? attemptId)
    {
        var entries = new List<ProviderHistoryEntry>(input.Count);
        for (var i = 0; i < input.Count; i++)
        {
            if (input[i] is not JsonObject item)
                continue;
            var raw = item.ToJsonString();
            using var document = JsonDocument.Parse(raw);
            entries.Add(new ProviderHistoryEntry
            {
                EntryId = CreateEntryId(source, attemptId, i, sequenceNumber: i, raw),
                Item = document.RootElement.Clone()
            });
        }
        return entries;
    }

    private static string CreateEntryId(
        string source,
        string? attemptId,
        int ordinal,
        long sequenceNumber,
        string raw)
    {
        var bytes = Encoding.UTF8.GetBytes(
            $"{source}\n{attemptId}\n{ordinal}\n{sequenceNumber}\n{raw}");
        return "phe_" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..32];
    }

    private static string NewAttemptId() => "attempt_" + Guid.NewGuid().ToString("N");

    private sealed record RuntimeEntry(ProviderHistoryEntry Entry, string? AttemptId);
}

internal sealed record CanonicalResponsesInput(
    JsonArray Input,
    OpenAIResponsesItemIdentityDiagnostics ItemIdentity);

internal static class OpenAIResponsesProviderHistoryRuntimeScope
{
    private static readonly AsyncLocal<OpenAIResponsesProviderHistoryContext?> CurrentContext = new();

    public static OpenAIResponsesProviderHistoryContext? Current => CurrentContext.Value;

    public static IDisposable Set(
        OpenAIResponsesProviderHistoryContext context,
        Action<OpenAIResponsesProviderHistoryContext>? onDispose = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new Scope(previous, context, onDispose);
    }

    private sealed class Scope(
        OpenAIResponsesProviderHistoryContext? previous,
        OpenAIResponsesProviderHistoryContext current,
        Action<OpenAIResponsesProviderHistoryContext>? onDispose) : IDisposable
    {
        public void Dispose()
        {
            onDispose?.Invoke(current);
            CurrentContext.Value = previous;
        }
    }
}
