using System.Text.Json;

namespace DotCraft.CodexGateway.Workspace;

public sealed partial class CodexWorkspaceSession
{
    private async Task HydrateOperationsAsync(string thread, IEnumerable<JsonElement> entries, CancellationToken ct)
    {
        foreach (var group in entries.Where(entry => entry.Get("item").Get("payload").Text("callId") is { Length: > 0 })
                     .GroupBy(entry => entry.Required("turnId")))
        {
            var calls = group.Select(entry => entry.Get("item").Get("payload").Required("callId")).Distinct().ToArray();
            foreach (var batch in calls.Chunk(100))
            {
                var result = await RequestAsync("thread/items/read", new { threadId = thread, turnId = group.Key, callIds = batch }, ct);
                _projector.ProjectLogicalItems(thread, group.Key, result.Get("data").Rows().Select(entry => entry.Get("item")));
            }
        }
    }

    private static bool DeferOutput(JsonElement options, Dictionary<string, object?> item, JsonElement source)
    {
        if (item.GetValueOrDefault("status") is not "completed") return false;
        if (options.Flag("commandExecution") && item.GetValueOrDefault("type") is "commandExecution"
            && item.GetValueOrDefault("exitCode") is JsonElement { ValueKind: JsonValueKind.Number } exit
            && exit.GetInt32() == 0 && item.GetValueOrDefault("aggregatedOutput") is string { Length: > 0 })
        {
            item["aggregatedOutput"] = null;
            return true;
        }
        if (options.Flag("mcpToolCall") && item.GetValueOrDefault("type") is "mcpToolCall"
            && item.GetValueOrDefault("server") is not "codex_app"
            && source.Get("payload").Text("mcpAppResourceUri") is null
            && source.Get("payload").Get("structuredContent").Value() is null
            && source.Get("payload").Get("_meta").Value() is null
            && item.GetValueOrDefault("error") is null && item.GetValueOrDefault("result") is { } value)
        {
            var result = WorkspaceJson.Element(value);
            var content = result.Get("content").Rows().ToArray();
            if (content.Length > 0 && content.All(part => part.Text("type") == "text")
                && content.Any(part => part.Text("text") is { Length: > 0 }))
            {
                item["result"] = new { content = Array.Empty<object>(), structuredContent = (object?)null, _meta = (object?)null };
                return true;
            }
        }
        return false;
    }
}
