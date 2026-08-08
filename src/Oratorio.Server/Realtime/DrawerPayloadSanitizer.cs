using System.Text.Json;

namespace Oratorio.Server.Realtime;

public static class DrawerPayloadSanitizer
{
    public const string PayloadUnavailableCode = "payloadUnavailable";
    public const string PayloadUnavailableMessage = "The AppServer item payload could not be rendered in the Oratorio drawer.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static JsonElement SafeClonePayload(string itemType, JsonElement payload, out Exception? error)
    {
        try
        {
            error = null;
            return payload.Clone();
        }
        catch (Exception ex) when (IsPayloadException(ex))
        {
            error = ex;
            return CreateUnavailablePayload(itemType);
        }
    }

    public static JsonElement CreateUnavailablePayload(string itemType) =>
        JsonSerializer.SerializeToElement(new
        {
            type = itemType,
            serializationError = PayloadUnavailableCode,
            message = PayloadUnavailableMessage
        }, JsonOptions);

    public static JsonElement CreateDeltaPayload(string itemType, string deltaKind, string delta) =>
        JsonSerializer.SerializeToElement(new
        {
            type = itemType,
            deltaKind,
            text = TextFor(itemType, delta),
            aggregatedOutput = itemType == "commandExecution" ? delta : null,
            argumentsText = itemType == "toolCall" ? delta : null
        }, JsonOptions);

    public static JsonElement MergeDeltaPayload(string itemType, string deltaKind, JsonElement payload, string delta, out Exception? error)
    {
        try
        {
            var text = ExtractString(payload, "text");
            var aggregatedOutput = ExtractString(payload, "aggregatedOutput");
            var argumentsText = ExtractString(payload, "argumentsText");
            error = null;
            return JsonSerializer.SerializeToElement(new
            {
                type = itemType,
                deltaKind,
                text = TextFor(itemType, string.Concat(text, delta)),
                aggregatedOutput = itemType == "commandExecution" ? string.Concat(aggregatedOutput, delta) : aggregatedOutput,
                argumentsText = itemType == "toolCall" ? string.Concat(argumentsText, delta) : argumentsText
            }, JsonOptions);
        }
        catch (Exception ex) when (IsPayloadException(ex))
        {
            error = ex;
            return CreateDeltaPayload(itemType, deltaKind, delta);
        }
    }

    public static bool IsPayloadException(Exception ex) =>
        ex is ObjectDisposedException or JsonException or InvalidOperationException or NotSupportedException;

    private static string? TextFor(string itemType, string value) =>
        itemType is "agentMessage" or "reasoningContent" or "reasoning" ? value : null;

    private static string? ExtractString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
