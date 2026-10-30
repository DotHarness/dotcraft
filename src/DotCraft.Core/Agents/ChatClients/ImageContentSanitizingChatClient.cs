using System.Text.Json;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;

namespace DotCraft.Agents;

/// <summary>
/// Keeps prepared tool-result images inside the result, or for text-only tool results describes them and attaches them in one user message after each run of tool messages.
/// The projection depends only on the history, so a later request never changes what an earlier one sent.
/// </summary>
public sealed class ImageContentSanitizingChatClient(IChatClient innerClient, bool toolResultsAcceptImages = false)
    : DelegatingChatClient(innerClient)
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

    private List<ChatMessage> SanitizeMessages(IEnumerable<ChatMessage> messages)
    {
        var result = new List<ChatMessage>();
        List<AIContent>? runImages = null;
        foreach (var message in messages)
        {
            if (message.Role != ChatRole.Tool)
                FlushRunImages(result, ref runImages);

            result.Add(toolResultsAcceptImages
                ? PrepareToolImages(message)
                : DescribeToolImages(message, ref runImages));
        }

        FlushRunImages(result, ref runImages);
        return result;
    }

    private static ChatMessage PrepareToolImages(ChatMessage message)
    {
        List<AIContent>? contents = null;
        for (var i = 0; i < message.Contents.Count; i++)
        {
            if (message.Contents[i] is not FunctionResultContent frc
                || !TryGetResultContentItems(frc.Result, out var items)
                || items.All(IsTextContent))
            {
                continue;
            }

            var prepared = new List<AIContent>(items.Count);
            foreach (var item in items)
            {
                if (IsTextContent(item))
                {
                    prepared.Add(item);
                }
                else if (item is DataContent dc && ModelImageInputPreparer.IsImageMediaType(dc.MediaType))
                {
                    var image = ModelImageInputPreparer.Prepare(dc);
                    prepared.Add((AIContent?)image.Content ?? new TextContent(image.PlaceholderText));
                }
                else
                {
                    prepared.Add(new TextContent(DescribeContent(item)));
                }
            }

            contents ??= new List<AIContent>(message.Contents);
            contents[i] = new FunctionResultContent(frc.CallId, prepared);
        }

        return contents is null ? message : WithContents(message, contents);
    }

    private static ChatMessage DescribeToolImages(ChatMessage message, ref List<AIContent>? runImages)
    {
        List<AIContent>? contents = null;
        for (var i = 0; i < message.Contents.Count; i++)
        {
            if (message.Contents[i] is not FunctionResultContent frc || !HasNonTextContent(frc.Result))
                continue;

            var placeholderTexts = new List<string>();
            if (TryGetResultContentItems(frc.Result, out var items))
            {
                foreach (var item in items)
                {
                    if (item is not DataContent dc || !ModelImageInputPreparer.IsImageMediaType(dc.MediaType))
                        continue;

                    var prepared = ModelImageInputPreparer.Prepare(dc);
                    if (prepared.Content != null)
                        (runImages ??= []).Add(prepared.Content);
                    else
                        placeholderTexts.Add(prepared.PlaceholderText!);
                }
            }

            contents ??= new List<AIContent>(message.Contents);
            contents[i] = new FunctionResultContent(
                frc.CallId,
                AppendPlaceholders(DescribeResult(frc.Result), placeholderTexts));
        }

        return contents is null ? message : WithContents(message, contents);
    }

    private static void FlushRunImages(List<ChatMessage> result, ref List<AIContent>? runImages)
    {
        if (runImages is null)
            return;

        result.Add(new ChatMessage(
            ChatRole.User,
            [new TextContent(ModelImageInputPreparer.ToolResultImagesMarker), .. runImages]));
        runImages = null;
    }

    private static ChatMessage WithContents(ChatMessage message, IList<AIContent> contents) =>
        new(message.Role, contents)
        {
            AuthorName = message.AuthorName,
            MessageId = message.MessageId,
            AdditionalProperties = message.AdditionalProperties
        };

    private static string AppendPlaceholders(string text, IReadOnlyList<string> placeholderTexts) =>
        string.Join("\n", [text, .. placeholderTexts.Distinct(StringComparer.Ordinal)]);

    private static bool IsTextContent(AIContent item) => item is TextContent or DeferredToolReferenceContent;

    internal static bool HasNonTextContent(object? result) =>
        TryGetResultContentItems(result, out var items) && !items.All(IsTextContent);

    public static string DescribeResult(object? result)
    {
        if (result is NativeToolSearchOutput toolSearchOutput)
            return NativeToolSearchTool.FormatOutputForDisplay(toolSearchOutput);

        if (!TryGetResultContentItems(result, out var items))
            return result?.ToString() ?? "(no output)";

        var parts = new List<string>();
        foreach (var item in items)
        {
            if (DescribeContent(item) is { Length: > 0 } text)
                parts.Add(text);
        }

        return parts.Count > 0 ? string.Join("\n", parts) : "(no output)";
    }

    private static string DescribeContent(AIContent item) =>
        item switch
        {
            TextContent tc => tc.Text,
            DataContent dc => dc.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                ? $"[Image ({dc.MediaType}), {dc.Data.Length:N0} bytes]"
                : $"[Binary data ({dc.MediaType}), {dc.Data.Length:N0} bytes]",
            _ => item.ToString() ?? string.Empty
        };

    internal static IReadOnlyList<ChatMessage> ReplaceToolImagesWithDescriptions(
        IReadOnlyList<ChatMessage> messages)
    {
        var result = new List<ChatMessage>(messages.Count);
        foreach (var message in messages)
        {
            List<AIContent>? contents = null;
            for (var i = 0; i < message.Contents.Count; i++)
            {
                if (message.Contents[i] is not FunctionResultContent frc || !HasNonTextContent(frc.Result))
                    continue;

                contents ??= new List<AIContent>(message.Contents);
                contents[i] = new FunctionResultContent(frc.CallId, DescribeResult(frc.Result));
            }

            result.Add(contents is null ? message : WithContents(message, contents));
        }

        return result;
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
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
            }
        }

        items = [];
        return false;
    }
}
