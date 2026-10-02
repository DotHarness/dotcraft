using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001, MAAI001, MEAI001

namespace DotCraft.Agents;

internal sealed class OpenAIResponsesToolSearchChatClient : IChatClient
{
    private const string OpenAIResponsesProtocolName = "openai-responses";
    private static readonly AsyncLocal<IModelRuntimeDiagnostics?> CurrentDiagnosticsLocal = new();

    private readonly ResponsesClient _responsesClient;
    private readonly string _model;
    private readonly IChatClient _innerClient;
    private readonly IModelRuntimeDiagnostics? _diagnostics;
    private readonly ResponsesRequestSender _requestSender;

    internal delegate PreparedResponseStream ResponsesRequestSender(
        string model,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        JsonArray? canonicalInput,
        OpenAIResponsesItemIdentityDiagnostics? canonicalItemIdentity,
        IChatClient rawRepresentationClient,
        CancellationToken cancellationToken);

    internal sealed record PreparedResponseStream(
        CreateResponseOptions Options,
        IAsyncEnumerable<StreamingResponseUpdate> Updates,
        PromptCacheRequestShapeSnapshot? RequestShape = null);

    public OpenAIResponsesToolSearchChatClient(
        ResponsesClient responsesClient,
        string model,
        bool supportsFreeformTools,
        IModelRuntimeDiagnostics? traceCollector = null)
        : this(
            responsesClient,
            NormalizeRequiredModel(model),
            CreateInnerClient(responsesClient, model),
            CreateTransport(responsesClient),
            traceCollector,
            supportsFreeformTools)
    {
    }

    internal OpenAIResponsesToolSearchChatClient(
        ResponsesClient responsesClient,
        string model,
        IChatClient innerClient,
        IResponsesToolSearchTransport toolSearchTransport,
        IModelRuntimeDiagnostics? traceCollector = null,
        bool supportsFreeformTools = true)
        : this(
            responsesClient,
            model,
            innerClient,
            traceCollector,
            CreateStandardRequestSender(toolSearchTransport, supportsFreeformTools))
    {
    }

    internal OpenAIResponsesToolSearchChatClient(
        ResponsesClient responsesClient,
        string model,
        IChatClient innerClient,
        IModelRuntimeDiagnostics? traceCollector,
        ResponsesRequestSender requestSender)
    {
        _responsesClient = responsesClient ?? throw new ArgumentNullException(nameof(responsesClient));
        _model = string.IsNullOrWhiteSpace(model)
            ? throw new ArgumentException("Model must be configured.", nameof(model))
            : model.Trim();
        _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
        _diagnostics = traceCollector;
        _requestSender = requestSender ?? throw new ArgumentNullException(nameof(requestSender));
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await GetStreamingResponseAsync(chatMessages, options, cancellationToken)
            .ToAgentResponseAsync(cancellationToken)
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
        var requestContext = ProviderRequestContextScope.Current;
        var providerHistory = requestContext?.CurrentIdentity.RequestKind == ProviderRequestKind.Turn
            ? requestContext.History as OpenAIResponsesProviderHistoryContext
            : null;
        var canonicalInput = providerHistory == null
            ? null
            : await providerHistory.PrepareInputAsync(messages, preparedOptions, cancellationToken)
                .ConfigureAwait(false);
        var preparedResponse = _requestSender(
            _model,
            messages,
            preparedOptions,
            canonicalInput?.Input,
            canonicalInput?.ItemIdentity,
            this,
            cancellationToken);
        var responseOptions = preparedResponse.Options;
        var traceCollector = _diagnostics
            ?? requestContext?.Diagnostics
            ?? CurrentDiagnosticsLocal.Value;
        RecordPromptCacheRequestShape(traceCollector, preparedResponse.RequestShape);
        var sdkUpdates = preparedResponse.Updates;
        if (traceCollector != null)
            sdkUpdates = RecordProviderResponseDiagnostics(sdkUpdates, traceCollector, cancellationToken);
        var reasoning = new ResponsesReasoningStream();
        sdkUpdates = reasoning.TrackAsync(sdkUpdates, cancellationToken);
        if (providerHistory != null)
            sdkUpdates = CaptureProviderHistoryAsync(sdkUpdates, providerHistory, cancellationToken);
        var callState = new ResponsesToolSearchMapper.ResponsesOutputCallState(
            ResponsesToolSearchMapper.CollectFreeformParameters(preparedOptions));
        var normalizedUpdates = ResponsesToolSearchMapper.NormalizeToolSearchCalls(
            sdkUpdates,
            callState,
            cancellationToken);

        await foreach (var update in normalizedUpdates
                           .AsChatResponseUpdatesAsync(responseOptions, cancellationToken)
                           .ConfigureAwait(false))
        {
            if (update.RawRepresentation is StreamingResponseCompletedUpdate
                { Response.Usage.InputTokenDetails.CacheWriteTokenCount: { } cacheWrite })
            {
                foreach (var usage in update.Contents.OfType<UsageContent>())
                {
                    usage.Details.AdditionalCounts ??= new AdditionalPropertiesDictionary<long>();
                    usage.Details.AdditionalCounts["CacheWriteInputTokenCount"] = cacheWrite;
                }
            }
            reasoning.Apply(update);
            ResponsesToolSearchMapper.ApplyRecordedFunctionCallState(update, callState);
            yield return SuppressProviderContinuation(update);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this :
        serviceType == typeof(IToolCallArgumentsDeltaExtractor) ? OpenAIToolCallArgumentsDeltaExtractor.Instance :
        serviceType.IsInstanceOfType(_responsesClient) ? _responsesClient :
        serviceType.IsInstanceOfType(_innerClient) ? _innerClient :
        _innerClient.GetService(serviceType, serviceKey);

    public void Dispose() => _innerClient.Dispose();

    internal static IDisposable UseDiagnostics(IModelRuntimeDiagnostics? diagnostics)
    {
        var previous = CurrentDiagnosticsLocal.Value;
        CurrentDiagnosticsLocal.Value = diagnostics;
        return new RestoreDiagnosticsScope(previous);
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

    private static ResponsesRequestSender CreateStandardRequestSender(
        IResponsesToolSearchTransport transport,
        bool supportsFreeformTools)
    {
        ArgumentNullException.ThrowIfNull(transport);
        return (model, messages, options, canonicalInput, canonicalItemIdentity, rawRepresentationClient,
            cancellationToken) =>
        {
            var request = ResponsesToolSearchMapper.CreateResponseRequest(
                model,
                messages,
                options,
                canonicalInput: canonicalInput,
                canonicalItemIdentity: canonicalItemIdentity,
                rawRepresentationClient: rawRepresentationClient,
                supportsFreeformTools: supportsFreeformTools);
            return new PreparedResponseStream(
                request.Options,
                transport.CreateResponseStreamingAsync(request.Options, cancellationToken),
                request.Shape);
        };
    }

    private static void RecordPromptCacheRequestShape(
        IModelRuntimeDiagnostics? diagnostics,
        PromptCacheRequestShapeSnapshot? snapshot)
    {
        var sessionKey = ProviderRequestContextScope.Current?.CurrentIdentity.CurrentThreadId;
        if (diagnostics is null || snapshot is null || string.IsNullOrWhiteSpace(sessionKey))
            return;

        diagnostics.Record(new ModelRuntimeDiagnostic(
            "prompt_cache.request_shape",
            new Dictionary<string, object?>
            {
                ["sessionKey"] = sessionKey,
                ["snapshot"] = snapshot,
                ["requestIndex"] = PromptCacheRequestShapeTraceScope.RequestIndex,
                ["attemptNumber"] = ModelStreamAttemptRuntimeScope.Current?.AttemptNumber
            }));
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
        var pendingText = new SortedDictionary<int, SortedDictionary<int, StringBuilder>>();
        try
        {
            await foreach (var update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (update is StreamingResponseOutputTextDeltaUpdate delta)
                {
                    if (!pendingText.TryGetValue(delta.OutputIndex, out var contents))
                        pendingText[delta.OutputIndex] = contents = new();
                    if (!contents.TryGetValue(delta.ContentIndex, out var text))
                        contents[delta.ContentIndex] = text = new();
                    text.Append(delta.Delta);
                }
                else if (update is StreamingResponseOutputItemDoneUpdate { Item: { } item } done)
                {
                    await providerHistory.AppendProviderOutputAsync(
                            item, done.OutputIndex, done.SequenceNumber, CancellationToken.None)
                        .ConfigureAwait(false);
                    pendingText.Remove(done.OutputIndex);
                }
                yield return update;
            }
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested && pendingText.Count > 0)
            {
                var partial = pendingText.Values.Select(contents => new ChatMessage(
                    ChatRole.Assistant,
                    contents.Values.Select(text => (AIContent)new TextContent(text.ToString())).ToList())).ToList();
                await providerHistory.AppendLocalInputAsync(partial, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private static async IAsyncEnumerable<StreamingResponseUpdate> RecordProviderResponseDiagnostics(
        IAsyncEnumerable<StreamingResponseUpdate> updates,
        IModelRuntimeDiagnostics traceCollector,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var output = new ResponsesOutputDiagnostics();
        await foreach (var update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            output.Observe(update);
            RecordProviderResponseDiagnostic(update, traceCollector, output);
            yield return update;
        }
    }

    private static void RecordProviderResponseDiagnostic(
        StreamingResponseUpdate update,
        IModelRuntimeDiagnostics traceCollector,
        ResponsesOutputDiagnostics output)
    {
        var sessionKey = ProviderRequestContextScope.Current?.ConversationIdentity.CurrentThreadId;
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
                    metadataExtractionFailed: completed.Response == null,
                    output: output.Summarize(completed.Response));
                break;
            case StreamingResponseIncompleteUpdate incomplete:
                RecordResponseResultDiagnostic(
                    traceCollector,
                    sessionKey,
                    "response.incomplete",
                    incomplete.Response,
                    metadataExtractionFailed: incomplete.Response == null,
                    output: output.Summarize(incomplete.Response));
                break;
            case StreamingResponseFailedUpdate failed:
                RecordResponseResultDiagnostic(
                    traceCollector,
                    sessionKey,
                    "response.failed",
                    failed.Response,
                    metadataExtractionFailed: failed.Response == null,
                    output: output.Summarize(failed.Response));
                if (failed.Response?.Error != null)
                {
                    RecordProviderError(
                        traceCollector,
                        sessionKey,
                        failed.Response.Error.Code.ToString(),
                        failed.Response.Error.Message,
                        "StreamingResponseFailedUpdate",
                        failed.Response.Id,
                        failed.Response.Model);
                }
                break;
            case StreamingResponseErrorUpdate error:
                RecordProviderError(
                    traceCollector,
                    sessionKey,
                    error.Code,
                    error.Message,
                    "StreamingResponseErrorUpdate");
                break;
        }
    }

    private static void RecordResponseResultDiagnostic(
        IModelRuntimeDiagnostics traceCollector,
        string sessionKey,
        string eventType,
        ResponseResult? response,
        bool metadataExtractionFailed,
        ResponsesOutputDiagnostics.Summary? output)
    {
        var incompleteReason = response?.IncompleteStatusDetails?.Reason?.ToString();
        var status = response?.Status?.ToString();
        traceCollector.Record(new ModelRuntimeDiagnostic(
            "provider.response",
            new Dictionary<string, object?>
            {
                ["sessionKey"] = sessionKey,
                ["providerProtocol"] = OpenAIResponsesProtocolName,
                ["eventType"] = eventType,
                ["responseId"] = response?.Id,
                ["modelId"] = response?.Model,
                ["status"] = status,
                ["incompleteReason"] = incompleteReason,
                ["rawFinishReason"] = incompleteReason,
                ["usagePresent"] = response?.Usage != null,
                ["requestIndex"] = PromptCacheRequestShapeTraceScope.RequestIndex,
                ["outputTypes"] = output?.Types,
                ["outputTextLength"] = output?.TextLength,
                ["metadataExtractionFailed"] = metadataExtractionFailed || (response != null && output == null)
            }));
    }

    private static void RecordProviderError(
        IModelRuntimeDiagnostics diagnostics,
        string sessionKey,
        string? errorCode,
        string? message,
        string contentType,
        string? responseId = null,
        string? modelId = null) =>
        diagnostics.Record(new ModelRuntimeDiagnostic(
            "provider.error",
            new Dictionary<string, object?>
            {
                ["sessionKey"] = sessionKey,
                ["errorCode"] = errorCode,
                ["message"] = message,
                ["contentType"] = contentType,
                ["requestIndex"] = PromptCacheRequestShapeTraceScope.RequestIndex,
                ["responseId"] = responseId,
                ["modelId"] = modelId
            }));

    private sealed class RestoreDiagnosticsScope(IModelRuntimeDiagnostics? previous) : IDisposable
    {
        public void Dispose() => CurrentDiagnosticsLocal.Value = previous;
    }
}
