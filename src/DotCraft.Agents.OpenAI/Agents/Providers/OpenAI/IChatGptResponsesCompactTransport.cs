using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace DotCraft.Agents;

internal interface IChatGptResponsesCompactTransport
{
    Task<ChatGptResponsesCompactResponse> CompactAsync(
        ChatGptResponsesCompactRequest requestBody,
        CancellationToken cancellationToken);
}

internal sealed class SdkChatGptResponsesCompactTransport(ResponsesClient responsesClient)
    : IChatGptResponsesCompactTransport
{
    public async Task<ChatGptResponsesCompactResponse> CompactAsync(
        ChatGptResponsesCompactRequest requestBody,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestBody);
        var requestOptions = new RequestOptions
        {
            CancellationToken = cancellationToken
        };
        var requestBytes = JsonSerializer.SerializeToUtf8Bytes(
            requestBody,
            ChatGptResponsesCompactJson.Options);
        var result = await responsesClient.CompactResponseAsync(
                BinaryContent.Create(BinaryData.FromBytes(requestBytes)),
                "application/json",
                requestOptions)
            .ConfigureAwait(false);
        var rawResponse = result.GetRawResponse();
        try
        {
            return JsonSerializer.Deserialize<ChatGptResponsesCompactResponse>(
                       rawResponse.Content.ToMemory().Span,
                       ChatGptResponsesCompactJson.Options)
                   ?? throw new JsonException("Compact response body was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "provider_compaction_invalid_response: Compact response body must match the expected JSON envelope.",
                ex);
        }
    }
}
