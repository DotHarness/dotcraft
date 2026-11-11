using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Channels;
using DotCraft.Tools;

namespace DotCraft.ExternalChannel.Messaging;

internal static class ReadChannelHistoryErrorCodes
{
    public const string InvalidArguments = "invalidArguments";
    public const string Unsupported = "unsupported";
    public const string ChannelUnavailable = "channelUnavailable";
    public const string HistoryUnavailable = "historyUnavailable";
}

internal sealed class ReadChannelHistoryRuntime(
    ChatBindingResolver resolver,
    string threadId,
    string? originChannel,
    string? channelContext) : IToolRuntime
{
    internal const int DefaultLimit = 20;
    internal const int MaxLimit = 50;

    public async ValueTask<ToolExecutionResult> InvokeAsync(
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        if (!TryParse(arguments, out var limit, out var before, out var parseError))
            return Fail(ReadChannelHistoryErrorCodes.InvalidArguments, parseError);

        var bound = resolver.Resolve(threadId, originChannel, channelContext);
        if (bound == null)
            return Fail(ReadChannelHistoryErrorCodes.ChannelUnavailable, "This conversation is no longer connected to a chat.");
        if (!bound.CanDeliver
            || !resolver.Registry.TryGet(bound.ChannelName, out var runtime)
            || runtime is not { IsReady: true }
            || runtime.GetDeliveryCapabilities() is not { } capabilities)
        {
            return Fail(ReadChannelHistoryErrorCodes.ChannelUnavailable, $"The connection to {bound.Address} is not active.");
        }

        if (capabilities.History is not { } history)
            return Fail(ReadChannelHistoryErrorCodes.Unsupported, $"{runtime.DisplayName} does not let DotCraft read chat history.");

        var request = new ChannelHistoryRequest(bound.Target, Math.Min(limit, Math.Max(1, history.MaxMessages)), before);
        var result = await runtime.ReadHistoryAsync(request, cancellationToken);
        if (!result.Succeeded)
            return Fail(MapError(result.ErrorCode), result.ErrorMessage ?? $"{runtime.DisplayName} could not read the chat history.");

        return ToolExecutionResult.Succeeded(
            Render(bound, result),
            JsonSerializer.SerializeToElement(Structured(bound, result)));
    }

    private static bool TryParse(JsonObject arguments, out int limit, out string? before, out string error)
    {
        limit = DefaultLimit;
        before = null;
        error = string.Empty;
        if (arguments["limit"] is { } limitNode)
        {
            if (limitNode is not JsonValue value || !value.TryGetValue<int>(out limit) || limit is < 1 or > MaxLimit)
            {
                error = $"'limit' must be an integer from 1 to {MaxLimit}.";
                return false;
            }
        }

        if (arguments["before"] is { } beforeNode)
        {
            if (beforeNode is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
            {
                error = "'before' must be the cursor string from a previous result.";
                return false;
            }

            before = text.Trim();
        }

        return true;
    }

    private static string MapError(string? adapterCode) => adapterCode switch
    {
        "UnsupportedTarget" => ReadChannelHistoryErrorCodes.Unsupported,
        "InvalidCursor" => ReadChannelHistoryErrorCodes.InvalidArguments,
        "AdapterDisconnected" => ReadChannelHistoryErrorCodes.ChannelUnavailable,
        _ => ReadChannelHistoryErrorCodes.HistoryUnavailable
    };

    private static string Render(ChatBinding bound, ChannelHistoryResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(result.Messages.Count == 0
            ? $"No messages in {bound.Address}."
            : $"Messages in {bound.Address}, oldest first:");
        foreach (var message in result.Messages)
        {
            var time = message.SentAt is { Length: > 0 } sentAt ? $"[{sentAt}] " : string.Empty;
            var sender = message.FromBot == true
                ? "You"
                : message.SenderName is { Length: > 0 } name
                    ? message.SenderId is { Length: > 0 } id && id != name ? $"{name} ({id})" : name
                    : message.SenderId ?? "Unknown";
            sb.Append(time).Append(sender).Append(": ")
                .AppendLine(message.Text.Replace("\n", "\n  ", StringComparison.Ordinal));
        }

        if (result.Before is { Length: > 0 } cursor)
            sb.AppendLine($"Older messages: before={JsonSerializer.Serialize(cursor)}");
        return sb.ToString().TrimEnd();
    }

    private static JsonObject Structured(ChatBinding bound, ChannelHistoryResult result)
    {
        var messages = new JsonArray();
        foreach (var message in result.Messages)
        {
            var value = new JsonObject { ["text"] = message.Text };
            if (message.MessageId != null) value["messageId"] = message.MessageId;
            if (message.SenderId != null) value["senderId"] = message.SenderId;
            if (message.SenderName != null) value["senderName"] = message.SenderName;
            if (message.SentAt != null) value["sentAt"] = message.SentAt;
            if (message.FromBot != null) value["fromBot"] = message.FromBot;
            messages.Add(value);
        }

        var structured = new JsonObject { ["address"] = bound.Address, ["messages"] = messages };
        if (result.Before != null)
            structured["before"] = result.Before;
        return structured;
    }

    private static ToolExecutionResult Fail(string code, string message) =>
        new(false, $"{code}: {message}", null, error: new ToolError(code, message));
}
