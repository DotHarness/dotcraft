using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Agents;

/// <summary>
/// Moves OpenAI protocol prompt-cache markers from message root extensions into text content blocks.
/// </summary>
internal sealed class PromptCacheControlPipelinePolicy : PipelinePolicy
{
    private const string CacheControlKey = PromptCachingChatClient.CacheControlKey;
    private const string ChatCompletionsPathSuffix = "/chat/completions";

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        if (ShouldRewrite(message))
            RewriteRequestContent(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        if (ShouldRewrite(message))
            await RewriteRequestContentAsync(message).ConfigureAwait(false);
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }

    internal static string? RewriteJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json) ||
            !HasMessageRootCacheControl(json))
        {
            return null;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (root is not JsonObject rootObject ||
            rootObject["messages"] is not JsonArray messages)
        {
            return null;
        }

        var changed = false;
        foreach (var messageNode in messages)
        {
            if (messageNode is JsonObject messageObject)
                changed |= MoveMessageRootCacheControl(messageObject);
        }

        return changed ? rootObject.ToJsonString() : null;
    }

    private static bool ShouldRewrite(PipelineMessage message)
    {
        var uri = message.Request.Uri;
        if (uri is null)
            return false;

        return uri.AbsolutePath.EndsWith(ChatCompletionsPathSuffix, StringComparison.Ordinal);
    }

    private static bool HasMessageRootCacheControl(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, "messages", StringComparison.Ordinal) ||
                    property.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var message in property.Value.EnumerateArray())
                {
                    if (message.ValueKind != JsonValueKind.Object)
                        continue;

                    foreach (var messageProperty in message.EnumerateObject())
                    {
                        if (string.Equals(messageProperty.Name, CacheControlKey, StringComparison.Ordinal))
                            return true;
                    }
                }

                return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static bool MoveMessageRootCacheControl(JsonObject message)
    {
        if (!message.TryGetPropertyValue(CacheControlKey, out var cacheControl) ||
            cacheControl == null)
        {
            return false;
        }

        var moved = false;
        if (message["content"] is JsonValue contentValue &&
            contentValue.TryGetValue<string>(out var text))
        {
            message["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text,
                    [CacheControlKey] = cacheControl.DeepClone()
                }
            };
            moved = true;
        }
        else if (message["content"] is JsonArray contentArray)
        {
            moved = MoveCacheControlToLastTextBlock(contentArray, cacheControl);
        }

        if (!moved)
            return false;

        message.Remove(CacheControlKey);
        return true;
    }

    private static bool MoveCacheControlToLastTextBlock(JsonArray contentArray, JsonNode cacheControl)
    {
        for (var i = contentArray.Count - 1; i >= 0; i--)
        {
            if (contentArray[i] is not JsonObject block)
                continue;

            if (block["type"] is JsonValue typeValue &&
                typeValue.TryGetValue<string>(out var type) &&
                string.Equals(type, "text", StringComparison.Ordinal))
            {
                block[CacheControlKey] = cacheControl.DeepClone();
                return true;
            }
        }

        return false;
    }

    private static void RewriteRequestContent(PipelineMessage message)
    {
        if (message.Request.Content == null)
            return;

        using var stream = new MemoryStream();
        message.Request.Content.WriteTo(stream, message.CancellationToken);
        RewriteRequestContent(message, stream);
    }

    private static async ValueTask RewriteRequestContentAsync(PipelineMessage message)
    {
        if (message.Request.Content == null)
            return;

        await using var stream = new MemoryStream();
        await message.Request.Content.WriteToAsync(stream, message.CancellationToken).ConfigureAwait(false);
        RewriteRequestContent(message, stream);
    }

    private static void RewriteRequestContent(PipelineMessage message, MemoryStream stream)
    {
        var original = BinaryData.FromBytes(stream.ToArray()).ToString();
        var rewritten = RewriteJson(original);
        if (rewritten == null)
            return;

        message.Request.Content = BinaryContent.Create(BinaryData.FromString(rewritten));
    }
}
