using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAIAssistantChatMessage = OpenAI.Chat.AssistantChatMessage;
using OpenAIChatMessage = OpenAI.Chat.ChatMessage;
using OpenAIChatMessageContentPart = OpenAI.Chat.ChatMessageContentPart;
using OpenAISystemChatMessage = OpenAI.Chat.SystemChatMessage;
using OpenAIToolChatMessage = OpenAI.Chat.ToolChatMessage;
using OpenAIUserChatMessage = OpenAI.Chat.UserChatMessage;

namespace DotCraft.Agents;

internal sealed class OpenAIPromptCacheDialect : IPromptCacheDialect
{
    public static OpenAIPromptCacheDialect Instance { get; } = new();
    public string Name => "OpenAICompatible";
    public bool GroupToolResults => false;

    public object CreateMarker(string? ttl)
    {
        var marker = new Dictionary<string, object>(StringComparer.Ordinal) { ["type"] = "ephemeral" };
        if (!string.IsNullOrWhiteSpace(ttl))
            marker["ttl"] = ttl.Trim();
        return marker;
    }

    public TextContent MarkText(TextContent content, object marker)
    {
        var cacheControl = RequireMarker(marker);
        return new TextContent(content.Text)
        {
            AdditionalProperties = WithMarker(content.AdditionalProperties, cacheControl),
            RawRepresentation = CreateTextPart(content.Text, cacheControl)
        };
    }

    public FunctionResultContent MarkFunctionResult(
        FunctionResultContent content,
        string wireText,
        object marker)
    {
        var cacheControl = RequireMarker(marker);
        return new FunctionResultContent(content.CallId, content.Result)
        {
            AdditionalProperties = WithMarker(content.AdditionalProperties, cacheControl),
            Exception = content.Exception,
            RawRepresentation = CreateToolMessage(content.CallId, wireText, cacheControl)
        };
    }

    public object? CreateMessageRawRepresentation(
        Microsoft.Extensions.AI.ChatMessage original,
        Microsoft.Extensions.AI.ChatMessage rewritten,
        IReadOnlySet<int> markedContentIndexes,
        object marker)
    {
        var cacheControl = RequireMarker(marker);
        if (original.Role == ChatRole.Assistant
            && original.Contents.Any(static content => content is FunctionCallContent)
            && markedContentIndexes.Count == 1
            && rewritten.Contents.OfType<TextContent>().Count() == 1)
        {
            var text = rewritten.Contents.OfType<TextContent>().Single().Text;
            var message = new OpenAIAssistantChatMessage(text);
            Patch(message, cacheControl);
#pragma warning disable SCME0001
            message.Patch.Set(
                "$.tool_calls"u8,
                BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(CreateToolCalls(original))));
#pragma warning restore SCME0001
            return message;
        }

        if (rewritten.Contents.Count != 1 || rewritten.Contents[0] is not TextContent textContent)
            return original.RawRepresentation;
        OpenAIChatMessage? root = original.Role == ChatRole.User
            ? new OpenAIUserChatMessage(textContent.Text)
            : original.Role == ChatRole.Assistant
                ? new OpenAIAssistantChatMessage(textContent.Text)
                : original.Role == ChatRole.System
                    ? new OpenAISystemChatMessage(textContent.Text)
                    : null;
        if (root == null)
            return original.RawRepresentation;
        Patch(root, cacheControl);
        return root;
    }

    private static Dictionary<string, object> RequireMarker(object marker) =>
        marker as Dictionary<string, object>
        ?? throw new ArgumentException("Invalid OpenAI prompt-cache marker.", nameof(marker));

    private static AdditionalPropertiesDictionary WithMarker(
        AdditionalPropertiesDictionary? source,
        Dictionary<string, object> marker)
    {
        var properties = source == null
            ? new AdditionalPropertiesDictionary()
            : new AdditionalPropertiesDictionary(source);
        properties["cache_control"] = marker;
        return properties;
    }

    private static OpenAIChatMessageContentPart CreateTextPart(
        string text,
        Dictionary<string, object> marker)
    {
        var part = OpenAIChatMessageContentPart.CreateTextPart(text);
#pragma warning disable SCME0001
        part.Patch.Set("$.cache_control"u8, BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(marker)));
#pragma warning restore SCME0001
        return part;
    }

    private static OpenAIChatMessage CreateToolMessage(
        string callId,
        string text,
        Dictionary<string, object> marker)
    {
        var message = new OpenAIToolChatMessage(callId, text);
        Patch(message, marker);
        return message;
    }

    private static void Patch(OpenAIChatMessage message, Dictionary<string, object> marker)
    {
#pragma warning disable SCME0001
        message.Patch.Set("$.cache_control"u8, BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(marker)));
#pragma warning restore SCME0001
    }

    private static object[] CreateToolCalls(Microsoft.Extensions.AI.ChatMessage message) =>
        message.Contents.OfType<FunctionCallContent>().Select(static call => new
        {
            id = call.CallId,
            type = "function",
            function = new
            {
                name = call.Name,
                arguments = call.Arguments == null ? "{}" : JsonSerializer.Serialize(call.Arguments)
            }
        }).Cast<object>().ToArray();
}
