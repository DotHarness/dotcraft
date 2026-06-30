using DotCraft.Tools;
using Microsoft.Extensions.AI;

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
        var lastNonToolIndex = -1;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Role != ChatRole.Tool)
                lastNonToolIndex = i;
        }

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
                    if (isCurrentRoundTool && frc.Result is IEnumerable<AIContent> items)
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

    private static bool HasNonTextContent(object? result)
    {
        if (result is IEnumerable<AIContent> items)
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

        if (result is not IEnumerable<AIContent> items)
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
}
