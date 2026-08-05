using System.ClientModel;
using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001, MAAI001, MEAI001

namespace DotCraft.Agents;

/// <summary>
/// OpenAI Responses adapter that keeps DotCraft's existing request/history/update semantics but
/// reads the HTTP SSE stream through the SDK raw protocol method.
/// </summary>
internal sealed class OpenAIResponsesLiteChatClient : IChatClient
{
    private readonly OpenAIResponsesToolSearchChatClient _inner;

    public OpenAIResponsesLiteChatClient(
        ResponsesClient responsesClient,
        string model,
        string installationId,
        TraceCollector? traceCollector = null)
        : this(
            responsesClient,
            model,
            CreateInnerClient(responsesClient, model),
            new OpenAIResponsesLiteTransport(responsesClient),
            installationId,
            traceCollector)
    {
    }

    internal OpenAIResponsesLiteChatClient(
        ResponsesClient responsesClient,
        string model,
        IChatClient innerClient,
        IResponsesLiteTransport transport,
        string installationId,
        TraceCollector? traceCollector = null)
    {
        if (string.IsNullOrWhiteSpace(installationId))
            throw new ArgumentException("Installation id must be non-empty.", nameof(installationId));
        var normalizedInstallationId = installationId.Trim();
        _inner = new OpenAIResponsesToolSearchChatClient(
            responsesClient,
            model,
            innerClient,
            traceCollector,
            (requestModel, messages, options, canonicalInput, canonicalItemIdentity, rawClient,
                cancellationToken) =>
            {
                var request = OpenAIResponsesLiteRequestMapper.CreateResponseRequest(
                    requestModel,
                    messages,
                    options,
                    canonicalInput,
                    canonicalItemIdentity,
                    rawClient,
                    normalizedInstallationId);
                return new OpenAIResponsesToolSearchChatClient.PreparedResponseStream(
                    request.Options,
                    transport.CreateResponseStreamingAsync(request.WireBody, cancellationToken));
            });
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _inner.GetResponseAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _inner.GetStreamingResponseAsync(messages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this)
            ? this
            : _inner.GetService(serviceType, serviceKey);

    public void Dispose() => _inner.Dispose();

    private static IChatClient CreateInnerClient(ResponsesClient responsesClient, string model)
    {
        ArgumentNullException.ThrowIfNull(responsesClient);
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model must be configured.", nameof(model));

        return responsesClient
            .AsIChatClient(model.Trim())
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
}

/// <summary>
/// Uses the raw Responses protocol overload so the configured ClientPipeline still applies OAuth,
/// request policies and capture while DotCraft controls SSE framing and terminal detection.
/// </summary>
internal sealed class OpenAIResponsesLiteTransport(ResponsesClient responsesClient)
    : IResponsesLiteTransport
{
    private readonly ResponsesClient _responsesClient = responsesClient
        ?? throw new ArgumentNullException(nameof(responsesClient));

    public async IAsyncEnumerable<StreamingResponseUpdate> CreateResponseStreamingAsync(
        BinaryData wireBody,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wireBody);
        using var content = BinaryContent.Create(wireBody);
        var requestOptions = new RequestOptions
        {
            BufferResponse = false,
            CancellationToken = cancellationToken
        };
        var result = await _responsesClient.CreateResponseAsync(content, requestOptions)
            .ConfigureAwait(false);
        using var response = result.GetRawResponse();
        if (response.ContentStream == null)
            throw new IOException("Responses SSE response did not contain a content stream.");

        await foreach (var update in ReadStreamingResponseAsync(response.ContentStream, cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return update;
        }
    }

    internal static async IAsyncEnumerable<StreamingResponseUpdate> ReadStreamingResponseAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var terminalObserved = false;
        await foreach (var update in ParseSseUpdatesAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            terminalObserved |= update is StreamingResponseCompletedUpdate;
            yield return update;
            if (update is StreamingResponseIncompleteUpdate)
                throw new IOException("Responses stream ended with response.incomplete.");
            if (update is StreamingResponseFailedUpdate)
                throw new IOException("Responses stream ended with response.failed.");
            if (update is StreamingResponseErrorUpdate error)
                throw new IOException($"Responses stream ended with {error.Code}: {error.Message}");
        }

        if (!terminalObserved)
            throw new IOException("Responses SSE stream ended before a terminal event.");
    }

    internal static async IAsyncEnumerable<StreamingResponseUpdate> ParseSseUpdatesAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: true);
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                if (TryDeserialize(data, out var update))
                    yield return update;
                data.Clear();
                continue;
            }

            if (line[0] == ':')
                continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;
            var value = line.AsSpan(5);
            if (value.Length > 0 && value[0] == ' ')
                value = value[1..];
            if (data.Length > 0)
                data.Append('\n');
            data.Append(value);
        }

        if (TryDeserialize(data, out var finalUpdate))
            yield return finalUpdate;
    }

    private static bool TryDeserialize(StringBuilder data, out StreamingResponseUpdate update)
    {
        update = null!;
        if (data.Length == 0 || string.Equals(data.ToString(), "[DONE]", StringComparison.Ordinal))
            return false;
        update = ModelReaderWriter.Read<StreamingResponseUpdate>(
            BinaryData.FromString(data.ToString()),
            ModelReaderWriterOptions.Json)
            ?? throw new InvalidDataException("Responses SSE event could not be deserialized.");
        return true;
    }

}
