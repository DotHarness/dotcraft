using System.Text.Json;
using DotCraft.Protocol.AppServer;

namespace DotCraft.CLI;

internal enum OneShotNotificationKind
{
    Ignored,
    Progress,
    Completed,
    Failed,
    Cancelled
}

internal sealed record OneShotNotification(OneShotNotificationKind Kind, string? Text)
{
    private static readonly OneShotNotification Ignored = new(OneShotNotificationKind.Ignored, null);

    public static OneShotNotification From(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (!root.TryGetProperty("method", out var methodEl) || !root.TryGetProperty("params", out var @params))
            return Ignored;

        return methodEl.GetString() switch
        {
            AppServerMethodNames.TurnCompleted => new OneShotNotification(
                OneShotNotificationKind.Completed,
                ReadFinalText(@params)),
            AppServerMethodNames.TurnFailed => new OneShotNotification(
                OneShotNotificationKind.Failed,
                ReadFailure(@params)),
            AppServerMethodNames.TurnCancelled => new OneShotNotification(OneShotNotificationKind.Cancelled, "Turn cancelled."),
            AppServerMethodNames.ItemStarted => ReadItemStarted(@params),
            AppServerMethodNames.ItemCompleted => ReadItemCompleted(@params),
            AppServerMethodNames.SystemEvent => ReadSystemEvent(@params),
            _ => Ignored
        };
    }

    private static string? ReadFinalText(JsonElement @params)
    {
        if (!@params.TryGetProperty("turn", out var turn)
            || !turn.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
            return null;

        string? last = null;
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var type) || type.GetString() != "agentMessage")
                continue;
            if (!item.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                continue;
            if (ReadString(payload, "deliveryMode") == "async")
                continue;
            if (ReadString(payload, "text") is { Length: > 0 } text)
                last = text;
        }

        return last;
    }

    public static string ReadFailure(JsonElement @params)
    {
        if (ReadString(@params, "error") is { Length: > 0 } error)
            return error;
        if (@params.TryGetProperty("turn", out var turn) && turn.TryGetProperty("error", out var turnError))
        {
            var message = turnError.ValueKind == JsonValueKind.Object ? ReadString(turnError, "message") : null;
            if (turnError.ValueKind == JsonValueKind.String)
                message = turnError.GetString();
            if (!string.IsNullOrWhiteSpace(message))
                return message;
        }

        return "Turn failed.";
    }

    private static OneShotNotification ReadItemStarted(JsonElement @params)
    {
        if (!@params.TryGetProperty("item", out var item))
            return Ignored;

        var type = ReadString(item, "type");
        if (type is not ("toolCall" or "commandExecution"))
            return Ignored;

        var payload = item.TryGetProperty("payload", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
        var name = payload.ValueKind == JsonValueKind.Object
            ? ReadString(payload, "toolName") ?? ReadString(payload, "command")
            : null;

        return new OneShotNotification(
            OneShotNotificationKind.Progress,
            string.IsNullOrWhiteSpace(name) ? $"[CLI] Started {type}." : $"[CLI] Started {type}: {name}");
    }

    private static OneShotNotification ReadItemCompleted(JsonElement @params)
    {
        if (!@params.TryGetProperty("item", out var item))
            return Ignored;

        var type = ReadString(item, "type");
        if (type == "imageGeneration")
            return ReadImageGeneration(item);
        if (type is not ("toolExecution" or "toolResult" or "commandExecution"))
            return Ignored;

        return new OneShotNotification(OneShotNotificationKind.Progress, $"[CLI] Completed {type}.");
    }

    private static OneShotNotification ReadImageGeneration(JsonElement item)
    {
        if (!item.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            return Ignored;

        if (ImageGenerationFailure(payload) is { } failure)
            return new OneShotNotification(OneShotNotificationKind.Progress, $"[CLI] Image generation failed: {failure}");

        return ReadString(payload, "status") == "completed"
            ? new OneShotNotification(OneShotNotificationKind.Progress, $"[CLI] Generated image: {ReadString(payload, "savedPath")}")
            : Ignored;
    }

    public static string? ImageGenerationFailure(JsonElement payload)
    {
        var status = ReadString(payload, "status");
        if (status == "failed")
            return ReadString(payload, "errorMessage") ?? "Image generation failed.";
        if (status != "completed")
            return null;
        if (ReadString(payload, "savedPath") is { Length: > 0 } && ReadString(payload, "saveStatus") != "failed")
            return null;

        return ReadString(payload, "saveErrorCode") is { Length: > 0 } code
            ? $"The generated image could not be saved ({code})."
            : "The generated image could not be saved.";
    }

    private static OneShotNotification ReadSystemEvent(JsonElement @params) =>
        ReadString(@params, "message") is { } message && !string.IsNullOrWhiteSpace(message)
            ? new OneShotNotification(OneShotNotificationKind.Progress, $"[CLI] {message}")
            : Ignored;

    internal static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
