using OpenAI.Responses;
using OpenAiStreamingUpdate = OpenAI.Chat.StreamingChatCompletionUpdate;

#pragma warning disable OPENAI001

namespace DotCraft.Agents;

internal sealed class OpenAIToolCallArgumentsDeltaExtractor : IToolCallArgumentsDeltaExtractor
{
    public static OpenAIToolCallArgumentsDeltaExtractor Instance { get; } = new();

    public IEnumerable<ProviderToolCallArgumentsDelta> Extract(object? rawRepresentation)
    {
        if (rawRepresentation is OpenAiStreamingUpdate chatUpdate)
        {
            foreach (var update in chatUpdate.ToolCallUpdates)
            {
                yield return new ProviderToolCallArgumentsDelta(
                    update.Index,
                    update.FunctionName,
                    update.ToolCallId,
                    update.FunctionArgumentsUpdate?.ToString() ?? string.Empty);
            }
            yield break;
        }
        if (rawRepresentation is StreamingResponseOutputItemAddedUpdate
            { Item: FunctionCallResponseItem call } added)
        {
            yield return new ProviderToolCallArgumentsDelta(
                added.OutputIndex,
                call.FunctionName,
                call.CallId,
                string.Empty);
            yield break;
        }
        if (rawRepresentation is StreamingResponseFunctionCallArgumentsDeltaUpdate delta)
        {
            yield return new ProviderToolCallArgumentsDelta(
                delta.OutputIndex,
                null,
                null,
                delta.Delta.ToString());
        }
    }
}
