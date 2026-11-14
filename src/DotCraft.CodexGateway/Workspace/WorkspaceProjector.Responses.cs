using System.Text.Json;

namespace DotCraft.CodexGateway.Workspace;

internal sealed partial class WorkspaceProjector
{
    internal static bool VisibleUser(JsonElement source)
    {
        var p = source.Get("payload");
        return source.Text("type") == "userMessage" && p.Text("deliveryMode") != "subagentMailbox"
            && p.Text("triggerKind") is not ("subagentMailbox" or "terminal" or "subagent" or "workflow");
    }

    internal static bool RealResponse(JsonElement source) => source.Text("type") == "agentMessage"
        && source.Get("payload").Text("deliveryMode") is null
        && !string.IsNullOrWhiteSpace(source.Get("payload").Text("text"));

    internal object[] ProjectItems(string thread, JsonElement turn, string? view = null)
    {
        if (view is "none" or "notLoaded") return [];
        var rows = turn.Get("items").Rows().ToArray();
        if (view == "summary")
        {
            var user = rows.FirstOrDefault(VisibleUser);
            var response = rows.LastOrDefault(RealResponse);
            return new[] { user, response }.Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => (object)Item(thread, turn.Required("id"), item)!).ToArray();
        }
        return ProjectLogicalItems(thread, turn.Required("id"), rows);
    }

    internal object[] ProjectLogicalItems(string thread, string turn, IEnumerable<JsonElement> items)
    {
        var order = new List<string>();
        var projected = new Dictionary<string, Dictionary<string, object?>>();
        foreach (var source in items)
        {
            if (Item(thread, turn, source) is not { } item) continue;
            var id = (string)item["id"]!;
            if (!projected.ContainsKey(id)) order.Add(id);
            projected[id] = item;
        }
        return order.Select(id => (object)LatestOperation(projected[id])).ToArray();
    }
}
