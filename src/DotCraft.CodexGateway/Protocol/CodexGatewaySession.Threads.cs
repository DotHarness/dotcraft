using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.CodexGateway.Workspace;
using DotCraft.Sdk;
using DotCraft.Sdk.Hub;

namespace DotCraft.CodexGateway.Protocol;

public sealed partial class CodexGatewaySession
{
    private sealed record ThreadRow(CodexWorkspace Workspace, JsonElement Summary, long Timestamp, string Key);

    private static IReadOnlyList<CodexWorkspace> SelectWorkspaces(JsonElement p, IReadOnlyList<CodexWorkspace> workspaces)
    {
        if (p.Text("projectId") is { } id) return [CodexWorkspaceCatalog.ById(workspaces, id)];
        if (p.Text("cwd") is { } path) return [CodexWorkspaceCatalog.ByPath(workspaces, path)];
        if (p.Get("cwd").ValueKind == JsonValueKind.Array)
            return p.Get("cwd").Rows().Select(v => CodexWorkspaceCatalog.ByPath(workspaces, v.GetString()!)).DistinctBy(w => w.Id).ToArray();
        return workspaces;
    }

    private async Task<object> ListThreadsAsync(JsonElement p, IReadOnlyList<CodexWorkspace> snapshot, CancellationToken ct)
    {
        p.Reject("parentThreadId", "ancestorThreadId", "attachedToThreadId");
        if (p.Flag("currentAccountOnly"))
            throw new ArgumentException("Local workspace history is not filtered by ChatGPT account.");
        var workspaces = SelectWorkspaces(p, snapshot);
        var sort = p.Text("sortKey") ?? "created_at";
        if (sort is not ("created_at" or "updated_at" or "recency_at"))
            throw new ArgumentException("Unsupported conversation sort key.");
        var direction = p.Text("sortDirection") ?? "desc";
        if (direction is not ("asc" or "desc")) throw new ArgumentException("sortDirection must be asc or desc.");
        var providers = p.Get("modelProviders").Rows().Select(v => v.GetString()).ToHashSet(StringComparer.Ordinal);
        var sources = p.Get("sourceKinds").Rows().Select(v => v.GetString()).ToHashSet(StringComparer.Ordinal);
        var excluded = p.Get("excludedThreadIds").Rows().Select(v => v.GetString()).ToHashSet(StringComparer.Ordinal);
        var scope = JsonSerializer.Serialize(new
        {
            workspaces = workspaces.Select(w => w.Id).Order(), sort, archived = p.Flag("archived"),
            query = p.Text("searchTerm"), providers = providers.Order(), sources = sources.Order(), excluded = excluded.Order()
        });
        var cursor = GatewayCursor.Read(p.Text("cursor"), scope);
        var rows = new List<ThreadRow>();
        Exception? firstFailure = null;
        var accessible = 0;
        foreach (var route in workspaces)
        {
            try
            {
                var backend = await GetWorkspaceAsync(route, ct);
                var page = await backend.ListMetadataAsync(p, ct);
                accessible++;
                foreach (var summary in page.Get("data").Rows())
                {
                    if (sources.Count != 0 && !sources.Contains("appServer")) continue;
                    if (p.Flag("archived") && summary.Text("status") != "archived") continue;
                    var sourceId = summary.Required("id");
                    if (excluded.Count != 0 && excluded.Contains(_catalog.Routes(route).External("thread", sourceId))) continue;
                    var time = DateTimeOffset.Parse(summary.Required(sort == "created_at" ? "createdAt" : "lastActiveAt")).UtcTicks;
                    rows.Add(new(route, summary.Clone(), time, route.Id + "/" + sourceId));
                }
            }
            catch (Exception exception) when (workspaces.Count > 1 &&
                exception is IOException or HttpRequestException or DotCraftException or HubClientException)
            {
                firstFailure ??= exception;
                _diagnostic?.Invoke("workspaceUnavailable:" + route.Id);
            }
        }
        if (accessible == 0 && firstFailure is not null) throw firstFailure;
        IEnumerable<ThreadRow> ordered = direction == "asc"
            ? rows.OrderBy(r => r.Timestamp).ThenBy(r => r.Key, StringComparer.Ordinal)
            : rows.OrderByDescending(r => r.Timestamp).ThenByDescending(r => r.Key, StringComparer.Ordinal);
        if (cursor?.Timestamp is { } stamp && cursor.Id is { } anchor)
            ordered = ordered.Where(r =>
            {
                var comparison = r.Timestamp.CompareTo(stamp);
                if (comparison == 0) comparison = StringComparer.Ordinal.Compare(r.Key, anchor);
                return cursor.Inclusive ? (direction == "asc" ? comparison >= 0 : comparison <= 0)
                    : (direction == "asc" ? comparison > 0 : comparison < 0);
            });
        var data = new List<JsonObject>();
        var limit = p.Limit();
        ThreadRow? first = null;
        ThreadRow? last = null;
        var more = false;
        foreach (var row in ordered)
        {
            var external = _catalog.Routes(row.Workspace).External("thread", row.Summary.Required("id"));
            var backend = await GetWorkspaceAsync(row.Workspace, ct);
            var result = await backend.HandleAsync("thread/read", WorkspaceJson.Element(new { threadId = external }), ct);
            var node = JsonSerializer.SerializeToNode(result)!["thread"]!.AsObject();
            if (providers.Count > 0 && !providers.Contains(node["modelProvider"]?.GetValue<string>())) continue;
            if (data.Count == limit) { more = true; break; }
            node["projectId"] = row.Workspace.Id;
            data.Add(node);
            first ??= row;
            last = row;
        }
        return new
        {
            data,
            nextCursor = more ? GatewayCursor.Write(new(scope, last!.Timestamp, last.Key)) : null,
            backwardsCursor = first is null ? null : GatewayCursor.Write(new(scope, first.Timestamp, first.Key, Inclusive: true)),
            readStates = (object?)null
        };
    }
}
