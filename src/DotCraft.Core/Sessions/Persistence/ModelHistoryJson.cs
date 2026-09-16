using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

/// <summary>
/// The JSON form of one model-history message, shared by rollout persistence and by transports that
/// carry model history across a process boundary. Streaming tool-argument deltas are not part of it.
/// </summary>
public static class ModelHistoryJson
{
    private static readonly ModelHistoryCodec Codec = new();

    public static JsonNode Encode(ChatMessage message) =>
        JsonSerializer.SerializeToNode(Codec.Encode(message), SessionJsonOptions.Default)!;

    public static ChatMessage Decode(JsonNode json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var message = json.Deserialize<ModelHistoryMessage>(SessionJsonOptions.Default)
            ?? throw new JsonException("Model history message is missing.");
        return Codec.Decode(message);
    }
}
