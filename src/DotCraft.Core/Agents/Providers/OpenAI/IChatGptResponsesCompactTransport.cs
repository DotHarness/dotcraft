using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace DotCraft.Agents;

internal interface IChatGptResponsesCompactTransport
{
    Task<JsonElement> CompactAsync(
        JsonElement requestBody,
        CancellationToken cancellationToken);
}

internal sealed class SdkChatGptResponsesCompactTransport(ResponsesClient responsesClient)
    : IChatGptResponsesCompactTransport
{
    public async Task<JsonElement> CompactAsync(
        JsonElement requestBody,
        CancellationToken cancellationToken)
    {
        var requestOptions = new RequestOptions
        {
            CancellationToken = cancellationToken
        };
        var result = await responsesClient.CompactResponseAsync(
                "application/json",
                BinaryContent.Create(BinaryData.FromString(requestBody.GetRawText())),
                requestOptions)
            .ConfigureAwait(false);
        var rawResponse = result.GetRawResponse();
        using var document = JsonDocument.Parse(rawResponse.Content);
        return document.RootElement.Clone();
    }
}
