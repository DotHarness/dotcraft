using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001, MAAI001, MEAI001

namespace DotCraft.Agents;

internal sealed class OpenAIResponsesToolSearchChatClient : IChatClient
{
    private readonly ResponsesClient _responsesClient;
    private readonly string _model;
    private readonly IChatClient _innerClient;
    private readonly IResponsesToolSearchTransport _toolSearchTransport;

    public OpenAIResponsesToolSearchChatClient(ResponsesClient responsesClient, string model)
        : this(
            responsesClient,
            NormalizeRequiredModel(model),
            CreateInnerClient(responsesClient, model),
            CreateTransport(responsesClient))
    {
    }

    internal OpenAIResponsesToolSearchChatClient(
        ResponsesClient responsesClient,
        string model,
        IChatClient innerClient,
        IResponsesToolSearchTransport toolSearchTransport)
    {
        _responsesClient = responsesClient ?? throw new ArgumentNullException(nameof(responsesClient));
        _model = string.IsNullOrWhiteSpace(model)
            ? throw new ArgumentException("Model must be configured.", nameof(model))
            : model.Trim();
        _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
        _toolSearchTransport = toolSearchTransport ?? throw new ArgumentNullException(nameof(toolSearchTransport));
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
        var sdkUpdates = _toolSearchTransport.CreateResponseStreamingAsync(responseOptions, cancellationToken);
        var functionCallNamespaces = new Dictionary<string, string>(StringComparer.Ordinal);
        var normalizedUpdates = ResponsesToolSearchMapper.NormalizeToolSearchCalls(
            sdkUpdates,
            functionCallNamespaces,
            cancellationToken);

        await foreach (var update in normalizedUpdates
                           .AsChatResponseUpdatesAsync(responseOptions, cancellationToken)
                           .ConfigureAwait(false))
        {
            ResponsesToolSearchMapper.ApplyRecordedFunctionCallNamespaces(update, functionCallNamespaces);
            yield return SuppressProviderContinuation(update);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this :
        serviceType.IsInstanceOfType(_responsesClient) ? _responsesClient :
        serviceType.IsInstanceOfType(_innerClient) ? _innerClient :
        _innerClient.GetService(serviceType, serviceKey);

    public void Dispose() => _innerClient.Dispose();

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
}
