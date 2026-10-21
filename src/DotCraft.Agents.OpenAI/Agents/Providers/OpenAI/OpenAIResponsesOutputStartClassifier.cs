using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace DotCraft.Agents;

/// <summary>Responses acknowledges a request before prefill, so those lifecycle events are not output.</summary>
internal sealed class OpenAIResponsesOutputStartClassifier : IModelStreamOutputStartClassifier
{
    public static OpenAIResponsesOutputStartClassifier Instance { get; } = new();

    public bool IsOutputStart(ChatResponseUpdate update) =>
        update.RawRepresentation is not (StreamingResponseCreatedUpdate
            or StreamingResponseQueuedUpdate
            or StreamingResponseInProgressUpdate);
}
