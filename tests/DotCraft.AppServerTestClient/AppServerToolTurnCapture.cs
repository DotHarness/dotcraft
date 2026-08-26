using System.Text.Json;

namespace DotCraft.AppServerTestClient;

internal sealed record AppServerToolCall(
    string CallId,
    string? Namespace,
    string ToolName,
    string ProviderFlatName,
    string? PluginId,
    JsonElement Arguments);

internal sealed record AppServerToolResult(
    bool Success,
    string Result);

internal sealed class AppServerToolTurnCapture
{
    public List<AppServerToolCall> Calls { get; } = [];

    public Dictionary<string, AppServerToolResult> Results { get; } =
        new(StringComparer.Ordinal);
}

internal static class AppServerToolTurnCaptureReader
{
    public static void ReadCompletedItem(
        JsonElement notification,
        string turnId,
        AppServerToolTurnCapture capture)
    {
        var parameters = notification.GetProperty("params");
        if (parameters.GetProperty("turnId").GetString() != turnId)
            return;
        ReadItem(parameters.GetProperty("item"), capture);
    }

    public static void ReadCompletedTurn(
        JsonElement notification,
        AppServerToolTurnCapture capture)
    {
        capture.Calls.Clear();
        capture.Results.Clear();
        var items = notification.GetProperty("params").GetProperty("turn").GetProperty("items");
        foreach (var item in items.EnumerateArray())
            ReadItem(item, capture);
    }

    private static void ReadItem(JsonElement item, AppServerToolTurnCapture capture)
    {
        var type = item.GetProperty("type").GetString();
        var payload = item.GetProperty("payload");
        if (type == "toolCall")
        {
            var source = payload.TryGetProperty("source", out var sourceElement)
                ? sourceElement
                : default;
            capture.Calls.Add(new AppServerToolCall(
                payload.GetProperty("callId").GetString()!,
                ReadOptionalString(payload, "namespace"),
                payload.GetProperty("toolName").GetString()!,
                payload.GetProperty("providerFlatName").GetString()!,
                source.ValueKind == JsonValueKind.Object
                    ? ReadOptionalString(source, "pluginId")
                    : null,
                payload.TryGetProperty("arguments", out var arguments)
                    ? arguments.Clone()
                    : JsonSerializer.SerializeToElement(new { })));
            return;
        }

        if (type != "toolResult")
            return;
        var callId = payload.GetProperty("callId").GetString()!;
        capture.Results[callId] = new AppServerToolResult(
            payload.GetProperty("success").GetBoolean(),
            payload.GetProperty("result").GetString()!);
    }

    private static string? ReadOptionalString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }
        return value.GetString();
    }
}
