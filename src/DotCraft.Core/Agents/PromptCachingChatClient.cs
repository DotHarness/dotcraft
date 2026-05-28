using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnthropicCacheControlEphemeral = Anthropic.Models.Messages.CacheControlEphemeral;
using AnthropicTextBlockParam = Anthropic.Models.Messages.TextBlockParam;
using DotCraft.Configuration;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using OpenAIAssistantChatMessage = OpenAI.Chat.AssistantChatMessage;
using OpenAIChatMessageContentPart = OpenAI.Chat.ChatMessageContentPart;
using OpenAIChatMessage = OpenAI.Chat.ChatMessage;
using OpenAISystemChatMessage = OpenAI.Chat.SystemChatMessage;
using OpenAIToolChatMessage = OpenAI.Chat.ToolChatMessage;
using OpenAIUserChatMessage = OpenAI.Chat.UserChatMessage;

namespace DotCraft.Agents;

/// <summary>
/// Adds provider-specific prompt-cache markers to Claude requests.
/// </summary>
public sealed class PromptCachingChatClient : DelegatingChatClient
{
    internal const string CacheControlKey = "cache_control";
    private const int MaxCacheBreakpoints = 4;
    private const string DefaultSessionKey = "__default__";

    private readonly AppConfig.PromptCachingConfig _config;
    private readonly string _model;
    private readonly TraceCollector? _traceCollector;
    private readonly PromptCacheMarkerStrategy _markerStrategy;
    private readonly ConcurrentDictionary<string, CachePointState> _cachePointStates = new();
    private readonly Func<string?> _sessionKeyAccessor;

    public PromptCachingChatClient(
        IChatClient innerClient,
        AppConfig.PromptCachingConfig config,
        string model,
        TraceCollector? traceCollector = null,
        Func<string?>? sessionKeyAccessor = null)
        : this(
            innerClient,
            config,
            model,
            PromptCacheMarkerStrategy.OpenAICompatible,
            traceCollector,
            sessionKeyAccessor)
    {
    }

    internal PromptCachingChatClient(
        IChatClient innerClient,
        AppConfig.PromptCachingConfig config,
        string model,
        PromptCacheMarkerStrategy markerStrategy,
        TraceCollector? traceCollector = null,
        Func<string?>? sessionKeyAccessor = null)
        : base(innerClient)
    {
        _config = config;
        _model = model;
        _markerStrategy = markerStrategy;
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

    internal (
        IReadOnlyList<ChatMessage> Messages,
        ChatOptions? Options,
        IReadOnlyList<PendingCachePoint> PendingCachePoints,
        string? SessionKey,
        int? LlmCallIndex,
        PromptCacheRequestDiagnosticSnapshot? PromptCacheDiagnostic) Prepare(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options)
    {
        var messages = chatMessages as IReadOnlyList<ChatMessage> ?? chatMessages.ToList();
        if (!_config.ShouldApply(_model))
            return (messages, options, [], null, null, null);

        var preparedMessages = new List<ChatMessage>(messages.Count + 1);
        var preparedOptions = options;
        var cacheControl = CreateCacheControl();
        var sessionKey = ResolveSessionKey();
        var state = _cachePointStates.GetOrAdd(sessionKey, static _ => new CachePointState());

        if (!string.IsNullOrWhiteSpace(options?.Instructions))
        {
            preparedOptions = options.Clone();
            preparedOptions.Instructions = null;
            preparedMessages.Add(new ChatMessage(
                ChatRole.System,
                (IList<AIContent>)[new TextContent(options.Instructions!)]));
        }

        foreach (var message in messages)
            preparedMessages.Add(message);

        var candidates = BuildCachePointCandidates(preparedMessages);
        var selected = SelectCachePoints(state, candidates);
        ApplyCacheControl(preparedMessages, selected, cacheControl);
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
                point.Candidate.ContentKind))).ToArray(), sessionKey, llmCallIndex, promptCacheDiagnostic);
    }

    private CacheControlMarker CreateCacheControl()
    {
        var ttl = string.IsNullOrWhiteSpace(_config.Ttl)
            ? null
            : _config.Ttl.Trim();
        return _markerStrategy switch
        {
            PromptCacheMarkerStrategy.AnthropicNative => CacheControlMarker.ForAnthropic(CreateAnthropicCacheControl(ttl)),
            _ => CacheControlMarker.ForOpenAI(CreateOpenAiCacheControl(ttl))
        };
    }

    private static Dictionary<string, object> CreateOpenAiCacheControl(string? ttl)
    {
        var cacheControl = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["type"] = "ephemeral"
        };

        if (!string.IsNullOrWhiteSpace(ttl))
            cacheControl["ttl"] = ttl;

        return cacheControl;
    }

    private static AnthropicCacheControlEphemeral CreateAnthropicCacheControl(string? ttl) =>
        string.IsNullOrWhiteSpace(ttl)
            ? new AnthropicCacheControlEphemeral()
            : new AnthropicCacheControlEphemeral { Ttl = ttl };

    private string ResolveSessionKey()
    {
        var sessionKey = _sessionKeyAccessor();
        return string.IsNullOrWhiteSpace(sessionKey)
            ? DefaultSessionKey
            : sessionKey.Trim();
    }

    private List<SelectedCachePoint> SelectCachePoints(
        CachePointState state,
        IReadOnlyList<CachePointCandidate> candidates)
    {
        if (candidates.Count == 0)
            return [];

        var remembered = state.GetHashes();
        var selected = _markerStrategy == PromptCacheMarkerStrategy.OpenAICompatible
            ? SelectOpenAICompatibleCachePoints(candidates, remembered)
            : SelectStablePrefixCachePoints(candidates, remembered);

        return selected.Values
            .OrderBy(static point => point.Candidate.Sequence)
            .ToList();
    }

    private static Dictionary<string, SelectedCachePoint> SelectOpenAICompatibleCachePoints(
        IReadOnlyList<CachePointCandidate> candidates,
        HashSet<string> remembered)
    {
        var selected = new Dictionary<string, SelectedCachePoint>(StringComparer.Ordinal);

        AddLatest(selected, candidates, remembered, ChatRole.System);
        var latestTail = FindLatestConversationTail(candidates);
        AddNearestRememberedBefore(
            selected,
            candidates,
            remembered,
            latestTail?.Sequence ?? int.MaxValue);
        if (latestTail != null)
            AddSelected(selected, latestTail, remembered.Contains(latestTail.Hash), latest: true);

        return selected;
    }

    private static Dictionary<string, SelectedCachePoint> SelectStablePrefixCachePoints(
        IReadOnlyList<CachePointCandidate> candidates,
        HashSet<string> remembered)
    {
        var selected = new Dictionary<string, SelectedCachePoint>(StringComparer.Ordinal);

        AddLatest(selected, candidates, remembered, ChatRole.System);
        AddLatestConversationTail(selected, candidates, remembered);

        foreach (var candidate in candidates
                     .Where(candidate => remembered.Contains(candidate.Hash))
                     .OrderByDescending(candidate => candidate.Sequence))
        {
            AddSelected(selected, candidate, remembered: true, latest: false);
            if (selected.Count >= MaxCacheBreakpoints)
                break;
        }

        AddLatest(selected, candidates, remembered, ChatRole.User);
        AddLatest(selected, candidates, remembered, ChatRole.Assistant);
        AddLatest(selected, candidates, remembered, ChatRole.Tool);

        return selected;
    }

    private static void AddLatestConversationTail(
        Dictionary<string, SelectedCachePoint> selected,
        IReadOnlyList<CachePointCandidate> candidates,
        HashSet<string> remembered)
    {
        var latestTail = FindLatestConversationTail(candidates);
        if (latestTail != null)
            AddSelected(selected, latestTail, remembered.Contains(latestTail.Hash), latest: true);
    }

    private static CachePointCandidate? FindLatestConversationTail(
        IReadOnlyList<CachePointCandidate> candidates)
    {
        for (var i = candidates.Count - 1; i >= 0; i--)
        {
            var candidate = candidates[i];
            if (candidate.Role == ChatRole.User ||
                candidate.Role == ChatRole.Assistant ||
                candidate.Role == ChatRole.Tool)
                return candidate;
        }

        return null;
    }

    private static void AddNearestRememberedBefore(
        Dictionary<string, SelectedCachePoint> selected,
        IReadOnlyList<CachePointCandidate> candidates,
        HashSet<string> remembered,
        int beforeSequence)
    {
        foreach (var candidate in candidates
                     .Where(candidate => candidate.Sequence < beforeSequence)
                     .Where(candidate => remembered.Contains(candidate.Hash))
                     .OrderByDescending(candidate => candidate.Sequence))
        {
            if (selected.ContainsKey(candidate.Hash))
                continue;

            AddSelected(selected, candidate, remembered: true, latest: false);
            return;
        }
    }

    private static void AddLatest(
        Dictionary<string, SelectedCachePoint> selected,
        IReadOnlyList<CachePointCandidate> candidates,
        HashSet<string> remembered,
        ChatRole role)
    {
        for (var i = candidates.Count - 1; i >= 0; i--)
        {
            var candidate = candidates[i];
            if (candidate.Role == role)
            {
                AddSelected(selected, candidate, remembered.Contains(candidate.Hash), latest: true);
                return;
            }
        }
    }

    private static void AddSelected(
        Dictionary<string, SelectedCachePoint> selected,
        CachePointCandidate candidate,
        bool remembered,
        bool latest)
    {
        if (selected.TryGetValue(candidate.Hash, out var existing))
        {
            selected[candidate.Hash] = existing with
            {
                Remembered = existing.Remembered || remembered,
                Latest = existing.Latest || latest
            };
        }
        else if (selected.Count < MaxCacheBreakpoints)
        {
            selected[candidate.Hash] = new SelectedCachePoint(candidate, remembered, latest);
        }
    }

    private void RecordCachePoints(
        (IReadOnlyList<ChatMessage> Messages,
            ChatOptions? Options,
            IReadOnlyList<PendingCachePoint> PendingCachePoints,
            string? SessionKey,
            int? LlmCallIndex,
            PromptCacheRequestDiagnosticSnapshot? PromptCacheDiagnostic) prepared)
    {
        if (_traceCollector == null ||
            prepared.SessionKey == null ||
            prepared.PendingCachePoints.Count == 0)
        {
            return;
        }

        _traceCollector.RecordPromptCachePoints(
            prepared.SessionKey,
            _model,
            prepared.PendingCachePoints.Select(static point => point.Trace).ToArray(),
            prepared.LlmCallIndex);

        if (prepared.PromptCacheDiagnostic != null)
            _traceCollector.RecordPromptCacheRequestSnapshot(prepared.SessionKey, prepared.PromptCacheDiagnostic);
    }

    private void CommitCachePoints(
        (IReadOnlyList<ChatMessage> Messages,
            ChatOptions? Options,
            IReadOnlyList<PendingCachePoint> PendingCachePoints,
            string? SessionKey,
            int? LlmCallIndex,
            PromptCacheRequestDiagnosticSnapshot? PromptCacheDiagnostic) prepared)
    {
        if (prepared.SessionKey == null || prepared.PendingCachePoints.Count == 0)
            return;

        var state = _cachePointStates.GetOrAdd(prepared.SessionKey, static _ => new CachePointState());
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

        var canonical = string.Join(
            "\n",
            tools
                .Select(static tool => tool.Name ?? string.Empty)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(static name => name, StringComparer.Ordinal));
        if (string.IsNullOrWhiteSpace(canonical))
            return null;

        return ComputeHash(new StringBuilder(canonical));
    }

    private static void ApplyCacheControl(
        List<ChatMessage> messages,
        IReadOnlyList<SelectedCachePoint> cachePoints,
        CacheControlMarker cacheControl)
    {
        var replacements = new Dictionary<int, IReadOnlyList<ChatMessage>>();
        foreach (var group in cachePoints.GroupBy(static point => point.Candidate.MessageIndex))
        {
            var message = messages[group.Key];
            var targetIndexes = group.Select(static point => point.Candidate.ContentIndex).ToHashSet();
            if (message.Role == ChatRole.Tool &&
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

    private static bool TryCreateCachedTextMessage(
        ChatMessage message,
        HashSet<int> targetIndexes,
        CacheControlMarker cacheControl,
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
                contents.Add(CreateCachedTextContent(text, cacheControl));
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

        if (cacheControl.OpenAiCacheControl != null &&
            message.Role == ChatRole.Assistant &&
            message.Contents.Any(static content => content is FunctionCallContent) &&
            targetIndexes.Count == 1 &&
            contents.Count(static content => content is TextContent) == 1 &&
            contents[targetIndexes.Single()] is TextContent assistantText)
        {
            cachedMessage.RawRepresentation = CreateCachedAssistantToolCallMessage(message, assistantText.Text, cacheControl.OpenAiCacheControl);
        }
        else if (cacheControl.OpenAiCacheControl != null &&
                 contents.Count == 1 &&
                 contents[0] is TextContent textContent)
        {
            cachedMessage.RawRepresentation = CreateCachedRootMessage(message.Role, textContent.Text, cacheControl.OpenAiCacheControl);
        }

        return true;
    }

    private static bool TryCreateCachedToolMessages(
        ChatMessage message,
        HashSet<int> targetIndexes,
        CacheControlMarker cacheControl,
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

    private static TextContent CreateCachedTextContent(
        TextContent text,
        CacheControlMarker cacheControl)
    {
        if (cacheControl.OpenAiCacheControl != null)
            return CreateOpenAiCachedTextContent(text, cacheControl.OpenAiCacheControl);

        if (cacheControl.AnthropicCacheControl != null)
            return CreateAnthropicCachedTextContent(text, cacheControl.AnthropicCacheControl);

        throw new InvalidOperationException("Prompt cache marker strategy is not configured.");
    }

    private static TextContent CreateOpenAiCachedTextContent(
        TextContent text,
        Dictionary<string, object> cacheControl) =>
        new(text.Text)
        {
            AdditionalProperties = WithOpenAiCacheControl(text.AdditionalProperties, cacheControl),
            RawRepresentation = CreateCachedTextPart(text.Text, cacheControl)
        };

    private static TextContent CreateAnthropicCachedTextContent(
        TextContent text,
        AnthropicCacheControlEphemeral cacheControl)
    {
        var cached = new TextContent(text.Text)
        {
            AdditionalProperties = CloneAdditionalProperties(text.AdditionalProperties),
            RawRepresentation = text.RawRepresentation is AnthropicTextBlockParam block
                ? block with { CacheControl = cacheControl }
                : null
        };
        cached.WithCacheControl(cacheControl);
        return cached;
    }

    private static bool TryCreateCachedFunctionResultContent(
        FunctionResultContent result,
        CacheControlMarker cacheControl,
        out FunctionResultContent cachedResult)
    {
        cachedResult = result;
        if (!TryGetToolResultWireText(result, out var text))
            return false;

        cachedResult = new FunctionResultContent(result.CallId, result.Result)
        {
            AdditionalProperties = CloneAdditionalProperties(result.AdditionalProperties),
            Exception = result.Exception,
            RawRepresentation = cacheControl.OpenAiCacheControl == null
                ? null
                : CreateCachedToolRootMessage(result.CallId, text, cacheControl.OpenAiCacheControl)
        };
        if (cacheControl.OpenAiCacheControl != null)
            cachedResult.AdditionalProperties = WithOpenAiCacheControl(cachedResult.AdditionalProperties, cacheControl.OpenAiCacheControl);
        else if (cacheControl.AnthropicCacheControl != null)
            cachedResult.WithCacheControl(cacheControl.AnthropicCacheControl);
        else
            throw new InvalidOperationException("Prompt cache marker strategy is not configured.");

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

    private static AdditionalPropertiesDictionary? CloneAdditionalProperties(
        AdditionalPropertiesDictionary? source) =>
        source == null
            ? null
            : new AdditionalPropertiesDictionary(source);

    private static AdditionalPropertiesDictionary WithOpenAiCacheControl(
        AdditionalPropertiesDictionary? source,
        Dictionary<string, object> cacheControl)
    {
        var properties = source == null
            ? new AdditionalPropertiesDictionary()
            : new AdditionalPropertiesDictionary(source);
        properties[CacheControlKey] = cacheControl;
        return properties;
    }

    private static OpenAIChatMessageContentPart CreateCachedTextPart(
        string? text,
        Dictionary<string, object> cacheControl)
    {
        var part = OpenAIChatMessageContentPart.CreateTextPart(text ?? string.Empty);
#pragma warning disable SCME0001
        part.Patch.Set(
            "$.cache_control"u8,
            BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(cacheControl)));
#pragma warning restore SCME0001
        return part;
    }

    private static OpenAIChatMessage? CreateCachedRootMessage(
        ChatRole role,
        string? text,
        Dictionary<string, object> cacheControl)
    {
        OpenAIChatMessage? message = role switch
        {
            var value when value == ChatRole.User => new OpenAIUserChatMessage(text ?? string.Empty),
            var value when value == ChatRole.Assistant => new OpenAIAssistantChatMessage(text ?? string.Empty),
            var value when value == ChatRole.System => new OpenAISystemChatMessage(text ?? string.Empty),
            _ => null
        };

        if (message == null)
            return null;

#pragma warning disable SCME0001
        message.Patch.Set(
            "$.cache_control"u8,
            BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(cacheControl)));
#pragma warning restore SCME0001
        return message;
    }

    private static OpenAIChatMessage CreateCachedToolRootMessage(
        string toolCallId,
        string text,
        Dictionary<string, object> cacheControl)
    {
        var message = new OpenAIToolChatMessage(toolCallId, text);

#pragma warning disable SCME0001
        message.Patch.Set(
            "$.cache_control"u8,
            BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(cacheControl)));
#pragma warning restore SCME0001
        return message;
    }

    private static OpenAIChatMessage CreateCachedAssistantToolCallMessage(
        ChatMessage message,
        string? text,
        Dictionary<string, object> cacheControl)
    {
        var assistantMessage = new OpenAIAssistantChatMessage(text ?? string.Empty);

#pragma warning disable SCME0001
        assistantMessage.Patch.Set(
            "$.cache_control"u8,
            BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(cacheControl)));
        assistantMessage.Patch.Set(
            "$.tool_calls"u8,
            BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(CreateOpenAIToolCalls(message))));
#pragma warning restore SCME0001

        return assistantMessage;
    }

    private static object[] CreateOpenAIToolCalls(ChatMessage message) =>
        message.Contents
            .OfType<FunctionCallContent>()
            .Select(static call => new
            {
                id = call.CallId,
                type = "function",
                function = new
                {
                    name = call.Name,
                    arguments = SerializeToolCallArguments(call.Arguments)
                }
            })
            .Cast<object>()
            .ToArray();

    private static string SerializeToolCallArguments(object? arguments) =>
        arguments == null
            ? "{}"
            : JsonSerializer.Serialize(arguments);

    private sealed record CacheControlMarker(
        Dictionary<string, object>? OpenAiCacheControl,
        AnthropicCacheControlEphemeral? AnthropicCacheControl)
    {
        public static CacheControlMarker ForOpenAI(Dictionary<string, object> cacheControl) =>
            new(cacheControl, null);

        public static CacheControlMarker ForAnthropic(AnthropicCacheControlEphemeral cacheControl) =>
            new(null, cacheControl);
    }

    internal sealed record PendingCachePoint(string Hash, PromptCachePointTraceEntry Trace);

    private sealed record SelectedCachePoint(
        CachePointCandidate Candidate,
        bool Remembered,
        bool Latest);

    private sealed record CachePointCandidate(
        int MessageIndex,
        int ContentIndex,
        ChatRole Role,
        int Sequence,
        string Hash,
        string ContentKind);

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

internal enum PromptCacheMarkerStrategy
{
    OpenAICompatible,
    AnthropicNative
}
