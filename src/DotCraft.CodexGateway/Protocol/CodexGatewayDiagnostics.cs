using System.Text.Json;
using DotCraft.CodexGateway.Workspace;

namespace DotCraft.CodexGateway.Protocol;

internal static class CodexGatewayDiagnostics
{
    public static void Request(Action<string>? report, string method, JsonElement parameters)
    {
        if (report is null) return;
        var initial = parameters.Get("initialTurnsPage");
        report(JsonSerializer.Serialize(new
        {
            stage = "request", method, excludeTurns = parameters.Flag("excludeTurns"),
            initialPage = initial.ValueKind == JsonValueKind.Object,
            view = Choice(initial.Text("itemsView") ?? parameters.Text("itemsView"), "summary", "full", "notLoaded"),
            direction = Choice(initial.Text("sortDirection") ?? parameters.Text("sortDirection"), "asc", "desc"),
            limit = Number(initial.Get("limit").ValueKind == JsonValueKind.Undefined ? parameters.Get("limit") : initial.Get("limit"))
        }));
    }

    public static void Response(Action<string>? report, string? method, JsonElement result, int bytes, double elapsedMs)
    {
        if (report is null) return;
        report(JsonSerializer.Serialize(new
        {
            stage = "response", method, bytes, elapsedMs = Math.Round(elapsedMs, 2),
            rows = Count(result.Get("data")), turns = Count(result.Get("thread").Get("turns")),
            initialTurns = Count(result.Get("initialTurnsPage").Get("data")),
            nextCursor = Present(result.Get("nextCursor")),
            turnsBootstrap = Present(result.Get("turnsBackwardsCursor")),
            itemsBootstrap = Present(result.Get("itemsBackwardsCursor"))
        }));
    }

    public static void Notification(Action<string>? report, JsonElement message)
    {
        if (report is null || message.Text("method") is not ("item/started" or "item/completed")) return;
        var parameters = message.Get("params");
        var item = parameters.Get("item");
        if (item.Text("type") != "agentMessage") return;
        report(JsonSerializer.Serialize(new
        {
            stage = "assistant", method = message.Text("method"),
            threadId = parameters.Text("threadId"), turnId = parameters.Text("turnId"), itemId = item.Text("id"),
            phase = Choice(item.Text("phase"), "commentary", "partial_answer", "final_answer"),
            delivery = Choice(item.Text("delivery"), "async")
        }));
    }

    private static bool Present(JsonElement value) => value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);
    private static int? Count(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.GetArrayLength() : null;
    private static int? Number(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
    private static string? Choice(string? value, params string[] allowed) => value is null ? null : allowed.Contains(value) ? value : "invalid";
}
