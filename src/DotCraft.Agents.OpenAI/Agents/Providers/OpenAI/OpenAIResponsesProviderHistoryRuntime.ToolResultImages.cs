using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal sealed partial class OpenAIResponsesProviderHistoryContext
{
    // The sampling messages only re-attach the current tool round's images, but the stored
    // history keeps every round's copy; send earlier rounds as placeholders like the sampling history does.
    private static bool EndsWithToolResultImages(IReadOnlyList<ChatMessage> samplingMessages) =>
        samplingMessages.Count > 0
        && samplingMessages[^1].Role == ChatRole.User
        && samplingMessages[^1].Contents.FirstOrDefault() is TextContent { Text: ModelImageInputPreparer.ToolResultImagesMarker };

    private static void OmitEarlierToolResultImages(JsonArray input, bool keepLatest)
    {
        var messages = input.OfType<JsonObject>().Where(IsToolResultImagesMessage).ToList();
        if (keepLatest && messages.Count > 0)
            messages.RemoveAt(messages.Count - 1);

        foreach (var message in messages)
        {
            var content = (JsonArray)message["content"]!;
            for (var i = 0; i < content.Count; i++)
            {
                if (content[i] is JsonObject part && ReadString(part, "type") == "input_image")
                    content[i] = new JsonObject { ["type"] = "input_text", ["text"] = DescribeImage(ReadString(part, "image_url")) };
            }
        }
    }

    private static bool IsToolResultImagesMessage(JsonObject item) =>
        ReadString(item, "type") == "message"
        && ReadString(item, "role") == "user"
        && item["content"] is JsonArray { Count: > 0 } content
        && content[0] is JsonObject first
        && ReadString(first, "type") == "input_text"
        && ReadString(first, "text") == ModelImageInputPreparer.ToolResultImagesMarker;

    // Matches the placeholder the sampling history uses for earlier tool-round images.
    private static string DescribeImage(string? imageUrl)
    {
        const string dataPrefix = "data:";
        const string base64Marker = ";base64,";
        if (imageUrl is null || !imageUrl.StartsWith(dataPrefix, StringComparison.Ordinal))
            return "[Image]";
        var markerIndex = imageUrl.IndexOf(base64Marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return "[Image]";
        var mediaType = imageUrl[dataPrefix.Length..markerIndex];
        var base64Length = imageUrl.Length - markerIndex - base64Marker.Length;
        var padding = imageUrl.EndsWith("==", StringComparison.Ordinal) ? 2 : imageUrl.EndsWith('=') ? 1 : 0;
        var size = base64Length / 4 * 3 - padding;
        return $"[Image ({mediaType}), {size:N0} bytes]";
    }
}
