using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;
/// <summary>
/// Adds provider-specific prompt-cache markers to Claude requests.
/// </summary>
public sealed class PromptCachingChatClient : DelegatingChatClient
{
    internal const string CacheControlKey = "cache_control";
    private const int MaxCacheBreakpoints = 4;
    private const string DefaultSessionKey = "__default__";
    private static readonly AsyncLocal<PromptCacheStateOverride?> CacheStateOverrideLocal = new();

    private readonly AppConfig.PromptCachingConfig _config;
    private readonly string _model;
    private readonly TraceCollector? _traceCollector;
    private readonly PromptCacheMarkerStrategy _markerStrategy;
    private readonly IPromptCacheDialect _dialect;
    private readonly ConcurrentDictionary<string, CachePointState> _cachePointStates = new();
    private readonly Func<string?> _sessionKeyAccessor;

    public PromptCachingChatClient(
        IChatClient innerClient,
        AppConfig.PromptCachingConfig config,
        string model,
        TraceCollector? traceCollector = null,
        Func<string?>? sessionKeyAccessor = null,
        IPromptCacheDialect? dialect = null)
        : this(
            innerClient,
            config,
            model,
            dialect ?? AdditionalPropertiesPromptCacheDialect.Instance,
            traceCollector,
            sessionKeyAccessor)
    {
    }

    internal PromptCachingChatClient(
        IChatClient innerClient,
        AppConfig.PromptCachingConfig config,
        string model,
        IPromptCacheDialect dialect,
        TraceCollector? traceCollector = null,
        Func<string?>? sessionKeyAccessor = null)
        : base(innerClient)
    {
        _config = config;
        _model = model;
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        _markerStrategy = dialect.GroupToolResults
            ? PromptCacheMarkerStrategy.AnthropicNative
            : PromptCacheMarkerStrategy.OpenAICompatible;
        _traceCollector = traceCollector;
        _sessionKeyAccessor = sessionKeyAccessor ?? TracingChatClient.GetActiveSessionKey;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var prepared = Prepare(chatMessages, options);
        RecordCachePoints(prepared);
        var response = await base.GetResponseAsync(prepared.Messages, prepared.Options, cancellationToken);
        CommitCachePoints(prepared);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var prepared = Prepare(chatMessages, options);
        RecordCachePoints(prepared);
        await foreach (var update in base.GetStreamingResponseAsync(prepared.Messages, prepared.Options, cancellationToken))
        {
            yield return update;
        }

        CommitCachePoints(prepared);
    }

    internal static IDisposable UseCacheStateKey(
        string cacheStateKey,
        string? traceSessionKey = null,
        PromptCacheMaintenanceScope? maintenanceScope = null)
    {
        if (string.IsNullOrWhiteSpace(cacheStateKey))
            throw new ArgumentException("Prompt cache state key must not be empty.", nameof(cacheStateKey));

        var previous = CacheStateOverrideLocal.Value;
        CacheStateOverrideLocal.Value = new PromptCacheStateOverride(
            cacheStateKey.Trim(),
            string.IsNullOrWhiteSpace(traceSessionKey) ? null : traceSessionKey.Trim(),
            maintenanceScope);
        return new RestorePromptCacheStateOverrideScope(previous);
    }

    internal (
        IReadOnlyList<ChatMessage> Messages,
        ChatOptions? Options,
        IReadOnlyList<PendingCachePoint> PendingCachePoints,
        string? TraceSessionKey,
        string? CacheStateKey,
        int? LlmCallIndex,
        PromptCacheRequestDiagnosticSnapshot? PromptCacheDiagnostic) Prepare(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options)
    {
        var messages = chatMessages as IReadOnlyList<ChatMessage> ?? chatMessages.ToList();
        if (!_config.ShouldApply(_model))
            return (messages, options, [], null, null, null, null);

        var preparedMessages = new List<ChatMessage>(messages.Count + 1);
        var preparedOptions = options;
        var cacheControl = CreateCacheControl();
        var keys = ResolveCacheKeys();
        var state = _cachePointStates.GetOrAdd(keys.CacheStateKey, static _ => new CachePointState());
        var insertedSystemMessage = false;

        if (!string.IsNullOrWhiteSpace(options?.Instructions))
        {
            preparedOptions = options.Clone();
            preparedOptions.Instructions = null;
            preparedMessages.Add(new ChatMessage(
                ChatRole.System,
                (IList<AIContent>)[new TextContent(options.Instructions!)]));
            insertedSystemMessage = true;
        }

        foreach (var message in messages)
            preparedMessages.Add(message);

        var candidates = BuildCachePointCandidates(preparedMessages);
        var selected = SelectCachePoints(state, candidates, keys.MaintenanceScope, insertedSystemMessage);
        ApplyCacheControl(preparedMessages, selected, cacheControl, _markerStrategy);
        var commitCachePoints = keys.MaintenanceScope?.CacheWriteMode != PromptCacheMaintenanceWriteMode.ReadOnlyPrefix;
        var llmCallIndex = selected.Count == 0
            ? (int?)null
            : state.NextLlmCallIndex();
        var promptCacheDiagnostic = llmCallIndex.HasValue
            ? CreatePromptCacheDiagnosticSnapshot(
                preparedMessages,
                preparedOptions,
                candidates,
                selected,
                llmCallIndex.Value)
            : null;

        return (preparedMessages, preparedOptions, selected.Select(point => new PendingCachePoint(
            point.Candidate.Hash,
            new PromptCachePointTraceEntry(
                _model,
                point.Candidate.Role.Value,
                point.Candidate.MessageIndex,
                point.Candidate.ContentIndex,
                point.Candidate.Sequence,
                point.Candidate.Hash[..Math.Min(12, point.Candidate.Hash.Length)],
                point.Remembered,
                point.Latest,
                point.Candidate.ContentKind))).ToArray(), keys.TraceSessionKey, commitCachePoints ? keys.CacheStateKey : null, llmCallIndex, promptCacheDiagnostic);
    }

    private object CreateCacheControl()
    {
        var ttl = string.IsNullOrWhiteSpace(_config.Ttl)
            ? null
            : _config.Ttl.Trim();
        return _dialect.CreateMarker(ttl);
    }

    private (string TraceSessionKey, string CacheStateKey, PromptCacheMaintenanceScope? MaintenanceScope) ResolveCacheKeys()
    {
        var sessionKey = _sessionKeyAccessor();
        var fallback = string.IsNullOrWhiteSpace(sessionKey)
            ? DefaultSessionKey
            : sessionKey.Trim();
        var promptCacheOverride = CacheStateOverrideLocal.Value;
        if (promptCacheOverride == null)
            return (fallback, fallback, null);

        return (
            string.IsNullOrWhiteSpace(promptCacheOverride.TraceSessionKey)
                ? fallback
                : promptCacheOverride.TraceSessionKey!,
            promptCacheOverride.CacheStateKey,
            promptCacheOverride.MaintenanceScope);
    }

    private List<SelectedCachePoint> SelectCachePoints(
        CachePointState state,
        IReadOnlyList<CachePointCandidate> candidates,
        PromptCacheMaintenanceScope? maintenanceScope,
        bool insertedSystemMessage)
    {
        var remembered = state.GetHashes();
        var maintenance = maintenanceScope is null
            ? null
            : new PromptCacheMaintenanceSelection(
                maintenanceScope.SnapshotMessageCount,
                maintenanceScope.CacheWriteMode == PromptCacheMaintenanceWriteMode.ReadOnlyPrefix,
                insertedSystemMessage);
        return PromptCachePointSelector.Select(
                candidates,
                remembered,
                _markerStrategy == PromptCacheMarkerStrategy.OpenAICompatible,
                maintenance)
            .ToList();
    }

    private void RecordCachePoints(
        (IReadOnlyList<ChatMessage> Messages,
            ChatOptions? Options,
            IReadOnlyList<PendingCachePoint> PendingCachePoints,
            string? TraceSessionKey,
            string? CacheStateKey,
            int? LlmCallIndex,
            PromptCacheRequestDiagnosticSnapshot? PromptCacheDiagnostic) prepared)
    {
        if (_traceCollector == null ||
            prepared.TraceSessionKey == null ||
            prepared.PendingCachePoints.Count == 0)
        {
            return;
        }

        _traceCollector.RecordPromptCachePoints(
            prepared.TraceSessionKey,
            _model,
            prepared.PendingCachePoints.Select(static point => point.Trace).ToArray(),
            prepared.LlmCallIndex);

        if (prepared.PromptCacheDiagnostic != null)
            _traceCollector.RecordPromptCacheRequestSnapshot(prepared.TraceSessionKey, prepared.PromptCacheDiagnostic);
    }

    private void CommitCachePoints(
        (IReadOnlyList<ChatMessage> Messages,
            ChatOptions? Options,
            IReadOnlyList<PendingCachePoint> PendingCachePoints,
            string? TraceSessionKey,
            string? CacheStateKey,
            int? LlmCallIndex,
            PromptCacheRequestDiagnosticSnapshot? PromptCacheDiagnostic) prepared)
    {
        if (prepared.CacheStateKey == null || prepared.PendingCachePoints.Count == 0)
            return;

        var state = _cachePointStates.GetOrAdd(prepared.CacheStateKey, static _ => new CachePointState());
        state.Replace(prepared.PendingCachePoints.Select(static point => point.Hash));
    }

    private PromptCacheRequestDiagnosticSnapshot CreatePromptCacheDiagnosticSnapshot(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        IReadOnlyList<CachePointCandidate> candidates,
        IReadOnlyList<SelectedCachePoint> selected,
        int llmCallIndex)
    {
        var ttl = string.IsNullOrWhiteSpace(_config.Ttl)
            ? null
            : _config.Ttl.Trim();
        var selectedPoints = selected
            .OrderBy(static point => point.Candidate.Sequence)
            .Select(static point => new PromptCacheSelectedPointDiagnostic(
                point.Candidate.Role.Value,
                point.Candidate.ContentKind,
                point.Candidate.MessageIndex,
                point.Candidate.ContentIndex,
                point.Candidate.Sequence,
                point.Candidate.Hash[..Math.Min(12, point.Candidate.Hash.Length)],
                point.Remembered,
                point.Latest))
            .ToArray();
        var candidateCounts = candidates
            .GroupBy(static candidate => (Role: candidate.Role.Value, Kind: candidate.ContentKind))
            .OrderBy(static group => group.Key.Role, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.Kind, StringComparer.Ordinal)
            .Select(static group => new PromptCacheCandidateCountDiagnostic(
                group.Key.Role,
                group.Key.Kind,
                group.Count()))
            .ToArray();

        return new PromptCacheRequestDiagnosticSnapshot(
            _model,
            _markerStrategy.ToString(),
            ttl,
            llmCallIndex,
            selected.Count,
            candidates.Count,
            selected.Count(static point => !point.Remembered),
            selected.Count(static point => point.Remembered),
            selected.Any(static point => point.Latest && !point.Remembered),
            ComputeSystemHash(messages),
            ComputeToolSchemaHash(options),
            ComputeReasoningHash(options),
            options?.Tools?.Count ?? 0,
            selectedPoints,
            candidateCounts);
    }

    private static string? ComputeSystemHash(IReadOnlyList<ChatMessage> messages)
    {
        var canonical = new StringBuilder();
        foreach (var message in messages)
        {
            if (message.Role != ChatRole.System)
                continue;

            AppendMessageBoundary(canonical, message);
            foreach (var content in message.Contents)
                AppendContent(canonical, content);
        }

        return canonical.Length == 0
            ? null
            : ComputeHash(canonical);
    }

    private static string? ComputeToolSchemaHash(ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 } tools)
            return null;

        return PromptRequestFingerprints.ComputeToolFingerprint(tools);
    }

    private static string? ComputeReasoningHash(ChatOptions? options)
    {
        if (options?.Reasoning is not { } reasoning)
            return null;

        var canonical = JsonSerializer.Serialize(new
        {
            Effort = reasoning.Effort?.ToString(),
            Output = reasoning.Output.ToString()
        });
        return ComputeHash(new StringBuilder(canonical));
    }

    private void ApplyCacheControl(
        List<ChatMessage> messages,
        IReadOnlyList<SelectedCachePoint> cachePoints,
        object cacheControl,
        PromptCacheMarkerStrategy markerStrategy)
    {
        var replacements = new Dictionary<int, IReadOnlyList<ChatMessage>>();
        foreach (var group in cachePoints.GroupBy(static point => point.Candidate.MessageIndex))
        {
            var message = messages[group.Key];
            var targetIndexes = group.Select(static point => point.Candidate.ContentIndex).ToHashSet();
            if (message.Role == ChatRole.Tool &&
                markerStrategy == PromptCacheMarkerStrategy.AnthropicNative &&
                TryCreateCachedGroupedToolMessage(message, targetIndexes, cacheControl, out var groupedToolMessage))
            {
                replacements[group.Key] = [groupedToolMessage];
            }
            else if (message.Role == ChatRole.Tool &&
                TryCreateCachedToolMessages(message, targetIndexes, cacheControl, out var toolMessages))
            {
                replacements[group.Key] = toolMessages;
            }
            else if (TryCreateCachedTextMessage(message, targetIndexes, cacheControl, out var cachedMessage))
            {
                replacements[group.Key] = [cachedMessage];
            }
        }

        if (replacements.Count == 0)
            return;

        var rewritten = new List<ChatMessage>(messages.Count + replacements.Values.Sum(static value => value.Count) - replacements.Count);
        for (var i = 0; i < messages.Count; i++)
        {
            if (replacements.TryGetValue(i, out var replacement))
                rewritten.AddRange(replacement);
            else
                rewritten.Add(messages[i]);
        }

        messages.Clear();
        messages.AddRange(rewritten);
    }

    private static IReadOnlyList<CachePointCandidate> BuildCachePointCandidates(
        IReadOnlyList<ChatMessage> messages)
    {
        var candidates = new List<CachePointCandidate>();
        var canonical = new StringBuilder();
        var sequence = 0;

        for (var messageIndex = 0; messageIndex < messages.Count; messageIndex++)
        {
            var message = messages[messageIndex];
            var cacheableRole = IsCacheableRole(message);
            AppendMessageBoundary(canonical, message);

            for (var contentIndex = 0; contentIndex < message.Contents.Count; contentIndex++)
            {
                var content = message.Contents[contentIndex];
                AppendContent(canonical, content);

                if (cacheableRole && TryGetCachePointContentKind(content, out var contentKind))
                {
                    candidates.Add(new CachePointCandidate(
                        messageIndex,
                        contentIndex,
                        message.Role,
                        sequence++,
                        ComputeHash(canonical),
                        contentKind));
                }
            }
        }

        return candidates;
    }

    private static bool IsCacheableRole(ChatMessage message)
    {
        if (message.Role == ChatRole.User || message.Role == ChatRole.System)
            return true;

        if (message.Role == ChatRole.Assistant)
            return message.Contents.Any(static content => content is TextContent { Text.Length: > 0 });

        return message.Role == ChatRole.Tool;
    }

    private static bool TryGetCachePointContentKind(AIContent content, out string contentKind)
    {
        if (content is TextContent { Text.Length: > 0 })
        {
            contentKind = "text";
            return true;
        }

        if (content is FunctionResultContent result &&
            TryGetToolResultWireText(result, out var text) &&
            !string.IsNullOrEmpty(text))
        {
            contentKind = "function_result";
            return true;
        }

        contentKind = string.Empty;
        return false;
    }

    private static void AppendMessageBoundary(StringBuilder builder, ChatMessage message)
    {
        builder.Append("\nmessage:");
        builder.Append(message.Role.Value);
        builder.Append(':');
        builder.Append(message.AuthorName ?? string.Empty);
    }

    private static void AppendContent(StringBuilder builder, AIContent content)
    {
        builder.Append("\ncontent:");
        builder.Append(content.GetType().FullName);
        builder.Append(':');

        switch (content)
        {
            case TextContent text:
                AppendString(builder, text.Text);
                break;
            case FunctionCallContent call:
                AppendString(builder, call.CallId);
                AppendString(builder, call.Name);
                AppendCanonicalObject(builder, call.Arguments);
                break;
            case FunctionResultContent result:
                AppendString(builder, result.CallId);
                if (TryGetToolResultWireText(result, out var toolResultText))
                    AppendString(builder, toolResultText);
                else
                    AppendCanonicalObject(builder, result.Result);
                if (result.Exception != null)
                    AppendString(builder, result.Exception.GetType().FullName + ":" + result.Exception.Message);
                break;
            case DataContent data:
                AppendString(builder, data.MediaType);
                builder.Append(data.Data.Length);
                break;
            default:
                AppendString(builder, content.ToString());
                break;
        }
    }

    private static void AppendString(StringBuilder builder, string? value)
    {
        value ??= string.Empty;
        builder.Append(value.Length);
        builder.Append(':');
        builder.Append(value);
        builder.Append(';');
    }

    private static void AppendCanonicalObject(StringBuilder builder, object? value)
    {
        if (value == null)
        {
            builder.Append("null;");
            return;
        }

        if (value is string text)
        {
            AppendString(builder, text);
            return;
        }

        try
        {
            builder.Append(JsonSerializer.Serialize(value));
            builder.Append(';');
        }
        catch (NotSupportedException)
        {
            AppendString(builder, value.ToString());
        }
    }

    private static string ComputeHash(StringBuilder canonical)
    {
        var bytes = Encoding.UTF8.GetBytes(canonical.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private bool TryCreateCachedTextMessage(
        ChatMessage message,
        HashSet<int> targetIndexes,
        object cacheControl,
        out ChatMessage cachedMessage)
    {
        cachedMessage = message;
        var contents = new List<AIContent>(message.Contents.Count);
        var markedAny = false;

        for (var i = 0; i < message.Contents.Count; i++)
        {
            var content = message.Contents[i];
            if (targetIndexes.Contains(i) && content is TextContent text)
            {
                contents.Add(_dialect.MarkText(text, cacheControl));
                markedAny = true;
            }
            else
            {
                contents.Add(content);
            }
        }

        if (!markedAny)
            return false;

        cachedMessage = new ChatMessage(message.Role, contents)
        {
            AdditionalProperties = message.AdditionalProperties,
            AuthorName = message.AuthorName,
            CreatedAt = message.CreatedAt,
            MessageId = message.MessageId,
            RawRepresentation = message.RawRepresentation
        };

        cachedMessage.RawRepresentation = _dialect.CreateMessageRawRepresentation(
            message,
            cachedMessage,
            targetIndexes,
            cacheControl) ?? cachedMessage.RawRepresentation;

        return true;
    }

    private bool TryCreateCachedToolMessages(
        ChatMessage message,
        HashSet<int> targetIndexes,
        object cacheControl,
        out IReadOnlyList<ChatMessage> cachedMessages)
    {
        cachedMessages = [];
        var messages = new List<ChatMessage>(message.Contents.Count);
        var markedAny = false;

        for (var i = 0; i < message.Contents.Count; i++)
        {
            if (message.Contents[i] is not FunctionResultContent result)
                return false;

            AIContent toolContent = result;
            if (targetIndexes.Contains(i))
            {
                if (!TryCreateCachedFunctionResultContent(result, cacheControl, out var cachedResult))
                    return false;

                toolContent = cachedResult;
                markedAny = true;
            }

            messages.Add(new ChatMessage(ChatRole.Tool, (IList<AIContent>)[toolContent])
            {
                AdditionalProperties = message.AdditionalProperties,
                AuthorName = message.AuthorName,
                CreatedAt = message.CreatedAt,
                MessageId = message.MessageId,
                RawRepresentation = toolContent.RawRepresentation
            });
        }

        if (!markedAny)
            return false;

        cachedMessages = messages;
        return true;
    }

    private bool TryCreateCachedGroupedToolMessage(
        ChatMessage message,
        HashSet<int> targetIndexes,
        object cacheControl,
        out ChatMessage cachedMessage)
    {
        cachedMessage = message;
        var contents = new List<AIContent>(message.Contents.Count);
        var markedAny = false;

        for (var i = 0; i < message.Contents.Count; i++)
        {
            if (message.Contents[i] is not FunctionResultContent result)
                return false;

            AIContent toolContent = result;
            if (targetIndexes.Contains(i))
            {
                if (!TryCreateCachedFunctionResultContent(result, cacheControl, out var cachedResult))
                    return false;

                toolContent = cachedResult;
                markedAny = true;
            }

            contents.Add(toolContent);
        }

        if (!markedAny)
            return false;

        cachedMessage = new ChatMessage(ChatRole.Tool, contents)
        {
            AdditionalProperties = message.AdditionalProperties,
            AuthorName = message.AuthorName,
            CreatedAt = message.CreatedAt,
            MessageId = message.MessageId
        };
        return true;
    }

    private bool TryCreateCachedFunctionResultContent(
        FunctionResultContent result,
        object cacheControl,
        out FunctionResultContent cachedResult)
    {
        cachedResult = result;
        if (!TryGetToolResultWireText(result, out var text))
            return false;

        cachedResult = _dialect.MarkFunctionResult(result, text, cacheControl);

        return true;
    }

    private static bool TryGetToolResultWireText(FunctionResultContent result, out string text)
    {
        if (result.Result is string value)
        {
            text = value;
            return true;
        }

        if (result.Result is JsonElement element)
        {
            return TryGetJsonWireText(element, out text);
        }

        if (result.Result is JsonDocument document)
        {
            return TryGetJsonWireText(document.RootElement, out text);
        }

        if (result.Result is JsonNode node)
        {
            return TryGetJsonWireText(node, out text);
        }

        if (result.Result is IEnumerable<AIContent> contents)
        {
            var textContents = new List<AIContent>();

            foreach (var content in contents)
            {
                if (content is not TextContent)
                {
                    text = string.Empty;
                    return false;
                }

                textContents.Add(content);
            }

            text = textContents.Count == 0
                ? string.Empty
                : JsonSerializer.Serialize(textContents, AIJsonUtilities.DefaultOptions);
            return text.Length > 0;
        }

        text = string.Empty;
        return false;
    }

    private static bool TryGetJsonWireText(JsonElement element, out string text)
    {
        if (element.ValueKind == JsonValueKind.Undefined)
        {
            text = string.Empty;
            return false;
        }

        text = element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : JsonSerializer.Serialize(element, AIJsonUtilities.DefaultOptions);
        return true;
    }

    private static bool TryGetJsonWireText(JsonNode node, out string text)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var stringValue))
        {
            text = stringValue ?? string.Empty;
            return true;
        }

        text = JsonSerializer.Serialize(node, AIJsonUtilities.DefaultOptions);
        return text.Length > 0;
    }

    internal sealed record PendingCachePoint(string Hash, PromptCachePointTraceEntry Trace);

    private sealed record PromptCacheStateOverride(
        string CacheStateKey,
        string? TraceSessionKey,
        PromptCacheMaintenanceScope? MaintenanceScope);

    private sealed class RestorePromptCacheStateOverrideScope(PromptCacheStateOverride? previous) : IDisposable
    {
        public void Dispose()
        {
            CacheStateOverrideLocal.Value = previous;
        }
    }

    private sealed class CachePointState
    {
        private readonly Lock _gate = new();
        private HashSet<string> _hashes = new(StringComparer.Ordinal);

        public HashSet<string> GetHashes()
        {
            lock (_gate)
                return new HashSet<string>(_hashes, StringComparer.Ordinal);
        }

        public void Replace(IEnumerable<string> hashes)
        {
            lock (_gate)
                _hashes = hashes.Take(MaxCacheBreakpoints).ToHashSet(StringComparer.Ordinal);
        }

        public int NextLlmCallIndex()
        {
            lock (_gate)
                return ++_llmCallIndex;
        }

        private int _llmCallIndex;
    }
}
