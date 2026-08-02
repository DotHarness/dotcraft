using System.ClientModel.Primitives;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Context.Compaction;
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

    void MarkProjectionCovered(IReadOnlyList<ChatMessage> samplingMessages);

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

    public void MarkProjectionCovered(IReadOnlyList<ChatMessage> samplingMessages) =>
        OpenAIResponsesProviderHistoryRuntimeScope.Current?.MarkProjectionCovered(samplingMessages);

    public string? BeginAttempt() =>
        OpenAIResponsesProviderHistoryRuntimeScope.Current?.BeginAttempt();

    public ValueTask AbortAttemptAsync(string? attemptId, CancellationToken cancellationToken) =>
        OpenAIResponsesProviderHistoryRuntimeScope.Current is { } context
            ? context.AbortAttemptAsync(attemptId, cancellationToken)
            : ValueTask.CompletedTask;

    public void EndAttempt(string? attemptId) =>
        OpenAIResponsesProviderHistoryRuntimeScope.Current?.EndAttempt(attemptId);
}

internal sealed class OpenAIResponsesProviderHistoryContext : IProviderHistoryCompactionBridge
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ThreadConversationIdentity _identity;
    private readonly Func<ProviderHistoryItemsAppendedPayload, CancellationToken, Task>? _appendAsync;
    private readonly Func<ProviderHistoryReplacedPayload, CancellationToken, Task>? _replaceAsync;
    private readonly Func<ProviderHistoryAttemptAbortedPayload, CancellationToken, Task>? _abortAsync;
    private readonly Func<string, string, CancellationToken, Task>? _reconcileContextWindowAsync;
    private readonly List<RuntimeEntry> _entries;
    private int _coveredSamplingMessageCount;
    private string _generationId;
    private string _contextWindowId;
    private string? _coveredThroughTurnId;
    private bool _isNativeCompacted;
    private string? _currentAttemptId;

    public OpenAIResponsesProviderHistoryContext(
        ThreadConversationIdentity identity,
        ProviderHistorySnapshot snapshot,
        IReadOnlyList<ChatMessage> coveredMessages,
        Func<ProviderHistoryItemsAppendedPayload, CancellationToken, Task>? appendAsync,
        Func<ProviderHistoryReplacedPayload, CancellationToken, Task>? replaceAsync,
        Func<ProviderHistoryAttemptAbortedPayload, CancellationToken, Task>? abortAsync,
        Func<string, string, CancellationToken, Task>? reconcileContextWindowAsync = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        ArgumentNullException.ThrowIfNull(snapshot);
        _generationId = snapshot.GenerationId;
        _contextWindowId = snapshot.ContextWindowId;
        _coveredThroughTurnId = snapshot.CoveredThroughTurnId;
        _isNativeCompacted = snapshot.IsNativeCompacted;
        _coveredSamplingMessageCount = GetSamplingProjection(coveredMessages).Count;
        _appendAsync = appendAsync;
        _replaceAsync = replaceAsync;
        _abortAsync = abortAsync;
        _reconcileContextWindowAsync = reconcileContextWindowAsync;
        _entries = snapshot.Entries
            .Select(entry => new RuntimeEntry(ProviderHistoryReplayer.CloneEntry(entry), AttemptId: null))
            .ToList();
    }

    public async ValueTask<CanonicalResponsesInput> PrepareInputAsync(
        IReadOnlyList<ChatMessage> samplingMessages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_coveredSamplingMessageCount > samplingMessages.Count)
            {
                throw new InvalidDataException(
                    "responses_provider_history_corrupt: MEAI sampling history is shorter than its canonical coverage.");
            }

            var tail = samplingMessages.Skip(_coveredSamplingMessageCount).ToList();
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

            _coveredSamplingMessageCount = samplingMessages.Count;
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

    public async ValueTask<ProviderCompactionInput> CaptureCompactionInputAsync(
        CompactionPhase phase,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var samplingMessages = GetSamplingProjection(messages);
            if (_coveredSamplingMessageCount > samplingMessages.Count)
            {
                throw new InvalidDataException(
                    "responses_provider_history_corrupt: MEAI sampling history is shorter than its canonical coverage.");
            }

            var input = BuildInputArray();
            var coveredMessageCount = _coveredSamplingMessageCount;
            var coveredThroughTurnId = _coveredThroughTurnId;
            if (phase is not CompactionPhase.PreTurn)
            {
                var tail = samplingMessages.Skip(_coveredSamplingMessageCount).ToList();
                if (tail.Count > 0)
                {
                    var mapped = ResponsesToolSearchMapper.BuildInputItems(
                        tail,
                        options,
                        BuildCallCorrelationIndex(),
                        itemOrdinalOffset: _entries.Count);
                    foreach (var node in mapped.Input)
                        input.Add(node?.DeepClone());
                    NormalizeCallOutputs(input);
                }

                coveredMessageCount = samplingMessages.Count;
                coveredThroughTurnId = _identity.TurnId ?? _coveredThroughTurnId;
            }

            var items = new List<JsonElement>(input.Count);
            foreach (var node in input)
            {
                if (node is not JsonObject item)
                    continue;
                using var document = JsonDocument.Parse(item.ToJsonString());
                items.Add(document.RootElement.Clone());
            }

            return new ProviderCompactionInput(
                items,
                coveredMessageCount,
                coveredThroughTurnId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask ReplaceNativeAsync(
        CompactionReplacement.ProviderNative replacement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (!string.Equals(
                replacement.Protocol,
                ProviderHistorySchema.OpenAIResponsesProtocol,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("provider_compaction_invalid_response: Protocol mismatch.");
        }
        if (replacement.Items.Count == 0
            || replacement.Items.Any(item => item.ValueKind != JsonValueKind.Object))
        {
            throw new InvalidDataException(
                "provider_compaction_invalid_response: Native replacement must contain object items.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previousWindowId = _contextWindowId;
            var nextWindowId = CodexContextWindowRecord.CreateWindowId();
            var entries = CreateEntries(
                replacement.Items,
                ProviderHistoryReasons.RemoteCompaction,
                attemptId: null);
            var payload = new ProviderHistoryReplacedPayload
            {
                SchemaVersion = ProviderHistorySchema.CurrentSchemaVersion,
                ThreadId = _identity.CurrentThreadId,
                Protocol = ProviderHistorySchema.OpenAIResponsesProtocol,
                GenerationId = nextWindowId,
                ContextWindowId = nextWindowId,
                CoveredThroughTurnId = replacement.CoveredThroughTurnId,
                Reason = ProviderHistoryReasons.RemoteCompaction,
                Entries = entries
            };

            if (_replaceAsync != null)
                await _replaceAsync(payload, cancellationToken).ConfigureAwait(false);

            _entries.Clear();
            _entries.AddRange(entries.Select(entry => new RuntimeEntry(entry, AttemptId: null)));
            _generationId = nextWindowId;
            _contextWindowId = nextWindowId;
            _coveredThroughTurnId = replacement.CoveredThroughTurnId;
            _coveredSamplingMessageCount = Math.Max(0, replacement.CoveredMessageCount);
            _currentAttemptId = null;
            _isNativeCompacted = true;

            OpenAIResponsesCodexRuntimeScope.Current?.AdvanceContextWindow(nextWindowId);
            if (_reconcileContextWindowAsync != null)
            {
                try
                {
                    await _reconcileContextWindowAsync(
                            previousWindowId,
                            nextWindowId,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // The rollout replacement is already committed and live. The state-store
                    // window is a projection and will be reconciled again on cold recovery.
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public long EstimateNativeContextTokens(
        ProviderNativeSnapshot snapshot,
        IReadOnlyList<ChatMessage> pendingTail,
        ChatOptions? options) =>
        OpenAIResponsesNativeTokenEstimator.Estimate(snapshot.Items, pendingTail, options);

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
        var normalizedItem = ResponsesToolSearchMapper.NormalizeProviderHistoryItem(document.RootElement);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var attemptId = _currentAttemptId ??= NewAttemptId();
            var entryId = CreateEntryId(
                ProviderHistorySources.ProviderOutput,
                attemptId,
                outputIndex,
                sequenceNumber,
                normalizedItem.GetRawText());
            if (_entries.Any(entry => string.Equals(entry.Entry.EntryId, entryId, StringComparison.Ordinal)))
                return;

            var entry = new ProviderHistoryEntry
            {
                EntryId = entryId,
                Item = normalizedItem
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
            _coveredSamplingMessageCount = GetSamplingProjection(messages).Count;
            _currentAttemptId = null;
            _isNativeCompacted = false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void MarkProjectionCovered(IReadOnlyList<ChatMessage> samplingMessages)
    {
        ArgumentNullException.ThrowIfNull(samplingMessages);
        _coveredSamplingMessageCount = samplingMessages.Count;
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
            _coveredThroughTurnId,
            _isNativeCompacted);

    public bool TryEstimateActiveNativeContextTokens(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        out long tokens)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var samplingMessages = GetSamplingProjection(messages);
        if (!_isNativeCompacted || _coveredSamplingMessageCount > samplingMessages.Count)
        {
            tokens = 0;
            return false;
        }

        var snapshot = CaptureSnapshot();
        tokens = EstimateNativeContextTokens(
            new ProviderNativeSnapshot(
                snapshot.Entries.Select(entry => entry.Item).ToArray(),
                _coveredSamplingMessageCount,
                snapshot.CoveredThroughTurnId),
            samplingMessages.Skip(_coveredSamplingMessageCount).ToArray(),
            options);
        return true;
    }

    private static IReadOnlyList<ChatMessage> GetSamplingProjection(
        IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return ModelRequestHistorySanitizer.Sanitize(messages);
    }

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
        {
            var normalizedItem =
                ResponsesToolSearchMapper.NormalizeProviderHistoryItem(entry.Entry.Item);
            input.Add(JsonNode.Parse(normalizedItem.GetRawText()));
        }
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

    private static List<ProviderHistoryEntry> CreateEntries(
        IReadOnlyList<JsonElement> items,
        string source,
        string? attemptId)
    {
        var entries = new List<ProviderHistoryEntry>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var raw = item.GetRawText();
            entries.Add(new ProviderHistoryEntry
            {
                EntryId = CreateEntryId(source, attemptId, i, sequenceNumber: i, raw),
                Item = item.Clone()
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
