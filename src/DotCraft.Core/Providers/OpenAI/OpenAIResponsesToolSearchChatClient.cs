using System.Runtime.CompilerServices;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001, MAAI001, MEAI001

namespace DotCraft.Agents;

internal sealed class OpenAIResponsesToolSearchChatClient : IChatClient
{
    private const string OpenAIResponsesProtocolName = "openai-responses";
    private static readonly AsyncLocal<TraceCollector?> CurrentTraceCollectorLocal = new();

    private readonly ResponsesClient _responsesClient;
    private readonly string _model;
    private readonly IChatClient _innerClient;
    private readonly IResponsesToolSearchTransport _toolSearchTransport;
    private readonly TraceCollector? _traceCollector;

    public OpenAIResponsesToolSearchChatClient(
        ResponsesClient responsesClient,
        string model,
        TraceCollector? traceCollector = null)
        : this(
            responsesClient,
            NormalizeRequiredModel(model),
            CreateInnerClient(responsesClient, model),
            CreateTransport(responsesClient),
            traceCollector)
    {
    }

    internal OpenAIResponsesToolSearchChatClient(
        ResponsesClient responsesClient,
        string model,
        IChatClient innerClient,
        IResponsesToolSearchTransport toolSearchTransport,
        TraceCollector? traceCollector = null)
    {
        _responsesClient = responsesClient ?? throw new ArgumentNullException(nameof(responsesClient));
        _model = string.IsNullOrWhiteSpace(model)
            ? throw new ArgumentException("Model must be configured.", nameof(model))
            : model.Trim();
        _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
        _toolSearchTransport = toolSearchTransport ?? throw new ArgumentNullException(nameof(toolSearchTransport));
        _traceCollector = traceCollector;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await GetStreamingResponseAsync(chatMessages, options, cancellationToken)
            .ToChatResponseAsync(cancellationToken)
            .ConfigureAwait(false);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var routingIdentityScope = OpenAIResponsesRoutingIdentityScope.Set(
            OpenAIResponsesCodexMetadata.ResolveRoutingIdentity());
        // All openai-responses traffic flows through _toolSearchTransport.CreateResponseStreamingAsync
        // so the SDK-built CreateResponseOptions reaches the wire verbatim with all caller patches
        // (prompt_cache_key, client_metadata, tool_choice, reasoning) intact.
        var preparedOptions = ResponsesToolSearchMapper.PreparePromptCacheOptions(options);
        var messages = chatMessages as IReadOnlyList<ChatMessage> ?? chatMessages.ToList();
        var providerHistory = OpenAIResponsesProviderHistoryRuntimeScope.Current;
        var canonicalInput = providerHistory == null
            ? null
            : await providerHistory.PrepareInputAsync(messages, preparedOptions, cancellationToken)
                .ConfigureAwait(false);
        var responseRequest = ResponsesToolSearchMapper.CreateResponseRequest(
            _model,
            messages,
            preparedOptions,
            canonicalInput: canonicalInput?.Input,
            canonicalItemIdentity: canonicalInput?.ItemIdentity,
            rawRepresentationClient: this);
        var responseOptions = responseRequest.Options;
        var traceCollector = _traceCollector ?? CurrentTraceCollectorLocal.Value;
        var sdkUpdates = _toolSearchTransport.CreateResponseStreamingAsync(responseOptions, cancellationToken);
        if (traceCollector != null)
            sdkUpdates = RecordProviderResponseDiagnostics(sdkUpdates, traceCollector, cancellationToken);
        var providerItemIdentities = new ProviderResponseItemIdentityTracker();
        sdkUpdates = providerItemIdentities.TrackAsync(sdkUpdates, cancellationToken);
        if (providerHistory != null)
            sdkUpdates = CaptureProviderHistoryAsync(sdkUpdates, providerHistory, cancellationToken);
        var functionCallNamespaces = new Dictionary<string, string>(StringComparer.Ordinal);
        var normalizedUpdates = ResponsesToolSearchMapper.NormalizeToolSearchCalls(
            sdkUpdates,
            functionCallNamespaces,
            cancellationToken);

        await foreach (var update in normalizedUpdates
                           .AsChatResponseUpdatesAsync(responseOptions, cancellationToken)
                           .ConfigureAwait(false))
        {
            providerItemIdentities.Apply(update);
            ResponsesToolSearchMapper.ApplyRecordedFunctionCallNamespaces(update, functionCallNamespaces);
            yield return SuppressProviderContinuation(update);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this :
        serviceType == typeof(IProviderConversationHistoryBridge)
            ? OpenAIResponsesProviderHistoryBridge.Instance :
        serviceType.IsInstanceOfType(_responsesClient) ? _responsesClient :
        serviceType.IsInstanceOfType(_innerClient) ? _innerClient :
        _innerClient.GetService(serviceType, serviceKey);

    public void Dispose() => _innerClient.Dispose();

    internal static IDisposable UseTraceCollector(TraceCollector? traceCollector)
    {
        var previous = CurrentTraceCollectorLocal.Value;
        CurrentTraceCollectorLocal.Value = traceCollector;
        return new RestoreTraceCollectorScope(previous);
    }

    private static IChatClient CreateInnerClient(ResponsesClient responsesClient, string model)
    {
        ArgumentNullException.ThrowIfNull(responsesClient);
        return responsesClient
            .AsIChatClient(NormalizeRequiredModel(model))
            .AsBuilder()
            .ConfigureOptions(options =>
                options.RawRepresentationFactory = _ =>
                    new CreateResponseOptions
                    {
                        StoredOutputEnabled = false,
                        IncludedProperties =
                        {
                            IncludedResponseProperty.ReasoningEncryptedContent
                        }
                    })
            .Build();
    }

    private static IResponsesToolSearchTransport CreateTransport(ResponsesClient responsesClient)
    {
        ArgumentNullException.ThrowIfNull(responsesClient);
        return new SdkResponsesToolSearchTransport(responsesClient);
    }

    private static string NormalizeRequiredModel(string? model) =>
        string.IsNullOrWhiteSpace(model)
            ? throw new ArgumentException("Model must be configured.", nameof(model))
            : model.Trim();

    private static ChatResponseUpdate SuppressProviderContinuation(ChatResponseUpdate update)
    {
        update.ConversationId = null;
        update.ContinuationToken = null;
        return update;
    }

    private static async IAsyncEnumerable<StreamingResponseUpdate> CaptureProviderHistoryAsync(
        IAsyncEnumerable<StreamingResponseUpdate> updates,
        OpenAIResponsesProviderHistoryContext providerHistory,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (update is StreamingResponseOutputItemDoneUpdate { Item: { } item } done)
            {
                await providerHistory.AppendProviderOutputAsync(
                        item,
                        done.OutputIndex,
                        done.SequenceNumber,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            yield return update;
        }
    }

    private static async IAsyncEnumerable<StreamingResponseUpdate> RecordProviderResponseDiagnostics(
        IAsyncEnumerable<StreamingResponseUpdate> updates,
        TraceCollector traceCollector,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            RecordProviderResponseDiagnostic(update, traceCollector);
            yield return update;
        }
    }

    private static void RecordProviderResponseDiagnostic(
        StreamingResponseUpdate update,
        TraceCollector traceCollector)
    {
        var sessionKey = TracingChatClient.CurrentSessionKey ?? TracingChatClient.GetActiveSessionKey();
        if (string.IsNullOrWhiteSpace(sessionKey))
            return;

        switch (update)
        {
            case StreamingResponseCompletedUpdate completed:
                RecordResponseResultDiagnostic(
                    traceCollector,
                    sessionKey,
                    "response.completed",
                    completed.Response,
                    metadataExtractionFailed: completed.Response == null);
                break;
            case StreamingResponseIncompleteUpdate incomplete:
                RecordResponseResultDiagnostic(
                    traceCollector,
                    sessionKey,
                    "response.incomplete",
                    incomplete.Response,
                    metadataExtractionFailed: incomplete.Response == null);
                break;
            case StreamingResponseFailedUpdate failed:
                RecordResponseResultDiagnostic(
                    traceCollector,
                    sessionKey,
                    "response.failed",
                    failed.Response,
                    metadataExtractionFailed: failed.Response == null);
                if (failed.Response?.Error != null)
                {
                    traceCollector.RecordProviderError(
                        sessionKey,
                        failed.Response.Error.Code.ToString(),
                        failed.Response.Error.Message,
                        "StreamingResponseFailedUpdate",
                        PromptCacheRequestShapeTraceScope.RequestIndex,
                        failed.Response.Id,
                        failed.Response.Model);
                }
                break;
            case StreamingResponseErrorUpdate error:
                traceCollector.RecordProviderError(
                    sessionKey,
                    error.Code,
                    error.Message,
                    "StreamingResponseErrorUpdate",
                    PromptCacheRequestShapeTraceScope.RequestIndex);
                break;
        }
    }

    private static void RecordResponseResultDiagnostic(
        TraceCollector traceCollector,
        string sessionKey,
        string eventType,
        ResponseResult? response,
        bool metadataExtractionFailed)
    {
        var incompleteReason = response?.IncompleteStatusDetails?.Reason?.ToString();
        var status = response?.Status?.ToString();
        traceCollector.RecordProviderResponseDiagnostic(
            sessionKey,
            OpenAIResponsesProtocolName,
            eventType,
            response?.Id,
            response?.Model,
            status,
            incompleteReason,
            incompleteReason,
            response?.Usage != null,
            PromptCacheRequestShapeTraceScope.RequestIndex,
            metadataExtractionFailed);
    }

    private sealed class RestoreTraceCollectorScope(TraceCollector? previous) : IDisposable
    {
        public void Dispose() => CurrentTraceCollectorLocal.Value = previous;
    }

    private sealed class ProviderResponseItemIdentityTracker
    {
        private readonly Dictionary<int, string> _itemIdsByOutputIndex = [];

        public async IAsyncEnumerable<StreamingResponseUpdate> TrackAsync(
            IAsyncEnumerable<StreamingResponseUpdate> updates,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                Observe(update);
                yield return update;
            }
        }

        public void Apply(ChatResponseUpdate update)
        {
            if (!TryGetReasoningEvent(
                    update.RawRepresentation,
                    out var outputIndex,
                    out var eventItemId))
                return;

            // MEAI does not currently assign a role to Responses reasoning updates.
            // Give every event in the reasoning item an Assistant boundary so its
            // summary/protected content cannot be aggregated into a preceding Tool
            // message. The provider item ID remains content metadata, not MessageId.
            update.Role = ChatRole.Assistant;

            if (!TryResolveItemId(outputIndex, eventItemId, out var providerItemId))
                return;

            foreach (var reasoning in update.Contents.OfType<TextReasoningContent>())
                OpenAIResponsesItemIdentity.PreserveProviderItemId(reasoning, providerItemId);
        }

        private void Observe(StreamingResponseUpdate update)
        {
            switch (update)
            {
                case StreamingResponseOutputItemAddedUpdate added:
                    RecordItemId(added.OutputIndex, added.Item?.Id);
                    break;
                case StreamingResponseOutputItemDoneUpdate done:
                    if (RecordItemId(done.OutputIndex, done.Item?.Id))
                        break;
                    if (done.Item != null
                        && _itemIdsByOutputIndex.TryGetValue(done.OutputIndex, out var providerItemId))
                    {
                        done.Item.Id = providerItemId;
                    }
                    break;
                case StreamingResponseReasoningSummaryPartAddedUpdate reasoning:
                    RecordItemId(reasoning.OutputIndex, reasoning.ItemId);
                    break;
                case StreamingResponseReasoningSummaryPartDoneUpdate reasoning:
                    RecordItemId(reasoning.OutputIndex, reasoning.ItemId);
                    break;
                case StreamingResponseReasoningSummaryTextDeltaUpdate reasoning:
                    RecordItemId(reasoning.OutputIndex, reasoning.ItemId);
                    break;
                case StreamingResponseReasoningSummaryTextDoneUpdate reasoning:
                    RecordItemId(reasoning.OutputIndex, reasoning.ItemId);
                    break;
                case StreamingResponseReasoningTextDeltaUpdate reasoning:
                    RecordItemId(reasoning.OutputIndex, reasoning.ItemId);
                    break;
                case StreamingResponseReasoningTextDoneUpdate reasoning:
                    RecordItemId(reasoning.OutputIndex, reasoning.ItemId);
                    break;
            }
        }

        private static bool TryGetReasoningEvent(
            object? rawRepresentation,
            out int outputIndex,
            out string? providerItemId)
        {
            switch (rawRepresentation)
            {
                case StreamingResponseOutputItemAddedUpdate
                {
                    Item: ReasoningResponseItem item
                } added:
                    outputIndex = added.OutputIndex;
                    providerItemId = item.Id;
                    return true;
                case StreamingResponseOutputItemDoneUpdate
                {
                    Item: ReasoningResponseItem item
                } done:
                    outputIndex = done.OutputIndex;
                    providerItemId = item.Id;
                    return true;
                case StreamingResponseReasoningSummaryPartAddedUpdate reasoning:
                    outputIndex = reasoning.OutputIndex;
                    providerItemId = reasoning.ItemId;
                    return true;
                case StreamingResponseReasoningSummaryPartDoneUpdate reasoning:
                    outputIndex = reasoning.OutputIndex;
                    providerItemId = reasoning.ItemId;
                    return true;
                case StreamingResponseReasoningSummaryTextDeltaUpdate reasoning:
                    outputIndex = reasoning.OutputIndex;
                    providerItemId = reasoning.ItemId;
                    return true;
                case StreamingResponseReasoningSummaryTextDoneUpdate reasoning:
                    outputIndex = reasoning.OutputIndex;
                    providerItemId = reasoning.ItemId;
                    return true;
                case StreamingResponseReasoningTextDeltaUpdate reasoning:
                    outputIndex = reasoning.OutputIndex;
                    providerItemId = reasoning.ItemId;
                    return true;
                case StreamingResponseReasoningTextDoneUpdate reasoning:
                    outputIndex = reasoning.OutputIndex;
                    providerItemId = reasoning.ItemId;
                    return true;
                default:
                    outputIndex = default;
                    providerItemId = null;
                    return false;
            }
        }

        private bool RecordItemId(int outputIndex, string? providerItemId)
        {
            if (!OpenAIResponsesItemIdentity.IsValid(providerItemId))
                return false;

            _itemIdsByOutputIndex[outputIndex] = providerItemId!.Trim();
            return true;
        }

        private bool TryResolveItemId(
            int outputIndex,
            string? providerItemId,
            out string resolvedItemId)
        {
            if (OpenAIResponsesItemIdentity.IsValid(providerItemId))
            {
                resolvedItemId = providerItemId!.Trim();
                return true;
            }

            return _itemIdsByOutputIndex.TryGetValue(outputIndex, out resolvedItemId!);
        }
    }
}
