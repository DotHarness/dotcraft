using System.Runtime.CompilerServices;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace DotCraft.Agents;

internal interface IResponsesToolSearchTransport
{
    IAsyncEnumerable<StreamingResponseUpdate> CreateResponseStreamingAsync(
        CreateResponseOptions options,
        CancellationToken cancellationToken);
}

internal interface IResponsesLiteTransport
{
    IAsyncEnumerable<StreamingResponseUpdate> CreateResponseStreamingAsync(
        BinaryData wireBody,
        CancellationToken cancellationToken);
}

internal sealed class SdkResponsesToolSearchTransport(ResponsesClient responsesClient) : IResponsesToolSearchTransport
{
    public async IAsyncEnumerable<StreamingResponseUpdate> CreateResponseStreamingAsync(
        CreateResponseOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var update in responsesClient
                           .CreateResponseStreamingAsync(options, cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return update;
        }
    }
}
