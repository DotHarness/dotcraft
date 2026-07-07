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
        // All openai-responses traffic flows through _toolSearchTransport.CreateResponseStreamingAsync
        // so the SDK-built CreateResponseOptions reaches the wire verbatim with all caller patches
        // (prompt_cache_key, client_metadata, tool_choice, reasoning) intact.
        var preparedOptions = ResponsesToolSearchMapper.PreparePromptCacheOptions(options);
        var messages = chatMessages as IReadOnlyList<ChatMessage> ?? chatMessages.ToList();
        var responseOptions = ResponsesToolSearchMapper.CreateResponseOptions(_model, messages, preparedOptions);
        var traceCollector = _traceCollector ?? CurrentTraceCollectorLocal.Value;
        var sdkUpdates = _toolSearchTransport.CreateResponseStreamingAsync(responseOptions, cancellationToken);
        if (traceCollector != null)
            sdkUpdates = RecordProviderResponseDiagnostics(sdkUpdates, traceCollector, cancellationToken);
        var functionCallNamespaces = new Dictionary<string, string>(StringComparer.Ordinal);
        var hostedImageGenerationContents = new Queue<HostedImageGenerationContent>();
        var normalizedUpdates = ResponsesToolSearchMapper.CaptureHostedImageGenerationCalls(
            ResponsesToolSearchMapper.NormalizeToolSearchCalls(
                sdkUpdates,
                functionCallNamespaces,
                cancellationToken),
            hostedImageGenerationContents,
            cancellationToken);

        await foreach (var update in normalizedUpdates
                           .AsChatResponseUpdatesAsync(responseOptions, cancellationToken)
                           .ConfigureAwait(false))
        {
            ResponsesToolSearchMapper.ApplyRecordedFunctionCallNamespaces(update, functionCallNamespaces);
            yield return SuppressProviderContinuation(update);
        }

        while (hostedImageGenerationContents.Count > 0)
        {
            yield return SuppressProviderContinuation(new ChatResponseUpdate(
                ChatRole.Assistant,
                [hostedImageGenerationContents.Dequeue()]));
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this :
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
        return responsesClient.AsIChatClientWithStoredOutputDisabled(
            NormalizeRequiredModel(model),
            includeReasoningEncryptedContent: true);
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
}
