using System.Text.Json;
using DotCraft.Protocol;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;

namespace DotCraft.Agents;

/// <summary>
/// Replaces non-text content (images, binary data) in tool-result messages with
/// text descriptions before forwarding to the LLM API.
/// Many OpenAI protocol endpoints reject non-text content in tool-role messages (HTTP 400).
/// Image bytes from the current tool round are re-attached as a synthetic user message so vision models can see them.
/// </summary>
public sealed class ImageContentSanitizingChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetResponseAsync(SanitizeMessages(chatMessages), options, cancellationToken);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetStreamingResponseAsync(SanitizeMessages(chatMessages), options, cancellationToken);
    }

    private static List<ChatMessage> SanitizeMessages(IEnumerable<ChatMessage> messages)
    {
        var list = messages is IList<ChatMessage> il
            ? new List<ChatMessage>(il)
            : new List<ChatMessage>(messages);

        // Tool messages strictly after the last non-tool message belong to the current invocation round.
        var lastNonToolIndex = FindLastNonToolIndex(list);

        var promotedImages = new List<DataContent>();
        var result = new List<ChatMessage>(list.Count + 1);

        for (var i = 0; i < list.Count; i++)
        {
            var msg = list[i];
            var isCurrentRoundTool = msg.Role == ChatRole.Tool && i > lastNonToolIndex;

            var needsSanitization = false;
            foreach (var content in msg.Contents)
            {
                if (content is FunctionResultContent frc && HasNonTextContent(frc.Result))
                {
                    needsSanitization = true;
                    break;
                }
            }

            if (!needsSanitization)
            {
                result.Add(msg);
                continue;
            }

            var newContents = new List<AIContent>(msg.Contents.Count);
            foreach (var content in msg.Contents)
            {
                if (content is FunctionResultContent frc && HasNonTextContent(frc.Result))
                {
                    if (isCurrentRoundTool && TryGetResultContentItems(frc.Result, out var items))
                    {
                        var placeholderTexts = new List<string>();
                        foreach (var item in items)
                        {
                            if (item is DataContent dc &&
                                ModelImageInputPreparer.IsImageMediaType(dc.MediaType))
                            {
                                var prepared = ModelImageInputPreparer.Prepare(dc);
                                if (prepared.Content != null)
                                    promotedImages.Add(prepared.Content);
                                else if (!string.IsNullOrWhiteSpace(prepared.PlaceholderText))
                                    placeholderTexts.Add(prepared.PlaceholderText);
                            }
                        }

                        newContents.Add(new FunctionResultContent(
                            frc.CallId,
                            AppendPlaceholders(DescribeResult(frc.Result), placeholderTexts)));
                        continue;
                    }

                    newContents.Add(new FunctionResultContent(frc.CallId, DescribeResult(frc.Result)));
                }
                else
                {
                    newContents.Add(content);
                }
            }

            result.Add(new ChatMessage(msg.Role, (IList<AIContent>)newContents));
        }

        if (promotedImages.Count > 0)
        {
            var parts = new List<AIContent>(promotedImages.Count + 1)
            {
                new TextContent("[Image content from tool results — attached for vision analysis.]")
            };
            parts.AddRange(promotedImages);
            result.Add(new ChatMessage(ChatRole.User, (IList<AIContent>)parts));
        }

        return result;
    }

    private static string AppendPlaceholders(string text, IReadOnlyList<string> placeholderTexts)
    {
        if (placeholderTexts.Count == 0)
            return text;

        var parts = new List<string> { text };
        foreach (var placeholder in placeholderTexts.Distinct(StringComparer.Ordinal))
            parts.Add(placeholder);
        return string.Join("\n", parts);
    }

    internal static bool HasNonTextContent(object? result)
    {
        if (TryGetResultContentItems(result, out var items))
        {
            foreach (var item in items)
            {
                if (item is not TextContent)
                    return true;
            }
        }

        return false;
    }

    public static string DescribeResult(object? result)
    {
        if (result is NativeToolSearchOutput toolSearchOutput)
            return NativeToolSearchTool.FormatOutputForDisplay(toolSearchOutput);

        if (!TryGetResultContentItems(result, out var items))
            return result?.ToString() ?? "(no output)";

        var parts = new List<string>();
        foreach (var item in items)
        {
            switch (item)
            {
                case TextContent tc:
                    if (!string.IsNullOrEmpty(tc.Text))
                        parts.Add(tc.Text);
                    break;
                case DataContent dc:
                {
                    var mediaType = dc.MediaType;
                    var size = dc.Data.Length;
                    parts.Add(mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                        ? $"[Image ({mediaType}), {size:N0} bytes]"
                        : $"[Binary data ({mediaType}), {size:N0} bytes]");
                    break;
                }
                default:
                    var text = item.ToString();
                    if (!string.IsNullOrEmpty(text))
                        parts.Add(text);
                    break;
            }
        }

        return parts.Count > 0 ? string.Join("\n", parts) : "(no output)";
    }

    internal static IReadOnlyList<ChatMessage> ReplaceToolImagesWithDescriptions(
        IReadOnlyList<ChatMessage> messages)
        => ReplaceToolImagesWithDescriptions(messages, preserveCurrentRoundImages: false);

    internal static IReadOnlyList<ChatMessage> ReplaceHistoricalToolImagesWithDescriptions(
        IReadOnlyList<ChatMessage> messages)
        => ReplaceToolImagesWithDescriptions(messages, preserveCurrentRoundImages: true);

    private static IReadOnlyList<ChatMessage> ReplaceToolImagesWithDescriptions(
        IReadOnlyList<ChatMessage> messages,
        bool preserveCurrentRoundImages)
    {
        var lastNonToolIndex = preserveCurrentRoundImages
            ? FindLastNonToolIndex(messages)
            : int.MaxValue;
        var result = new List<ChatMessage>(messages.Count);
        for (var messageIndex = 0; messageIndex < messages.Count; messageIndex++)
        {
            var message = messages[messageIndex];
            if (preserveCurrentRoundImages
                && message.Role == ChatRole.Tool
                && messageIndex > lastNonToolIndex)
            {
                result.Add(message);
                continue;
            }

            List<AIContent>? contents = null;
            for (var i = 0; i < message.Contents.Count; i++)
            {
                if (message.Contents[i] is not FunctionResultContent frc || !HasNonTextContent(frc.Result))
                    continue;

                contents ??= new List<AIContent>(message.Contents);
                contents[i] = new FunctionResultContent(frc.CallId, DescribeResult(frc.Result));
            }

            if (contents is null)
            {
                result.Add(message);
                continue;
            }

            result.Add(new ChatMessage(message.Role, contents)
            {
                AuthorName = message.AuthorName,
                MessageId = message.MessageId
            });
        }

        return result;
    }

    private static int FindLastNonToolIndex(IReadOnlyList<ChatMessage> messages)
    {
        var lastNonToolIndex = -1;
        for (var i = 0; i < messages.Count; i++)
        {
            if (messages[i].Role != ChatRole.Tool)
                lastNonToolIndex = i;
        }

        return lastNonToolIndex;
    }

    internal static bool TryGetResultContentItems(
        object? result,
        out IReadOnlyList<AIContent> items)
    {
        if (result is IReadOnlyList<AIContent> readOnlyItems)
        {
            items = readOnlyItems;
            return true;
        }

        if (result is IEnumerable<AIContent> enumerableItems)
        {
            items = enumerableItems.ToList();
            return true;
        }

        if (result is JsonElement { ValueKind: JsonValueKind.Array } json)
        {
            try
            {
                items = json.Deserialize<List<AIContent>>(SessionPersistenceJsonOptions.Default) ?? [];
                return true;
            }
            catch (JsonException)
            {
                // Preserve the provider-compatible scalar fallback below.
            }
            catch (NotSupportedException)
            {
                // Preserve the provider-compatible scalar fallback below.
            }
        }

        items = [];
        return false;
    }
}
