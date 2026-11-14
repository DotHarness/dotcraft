using System.Text.Json;

namespace DotCraft.CodexGateway.Workspace;

public sealed partial class CodexWorkspaceSession
{
    private sealed record ThreadAnchor(long Timestamp, string Id);

    private async Task<object> ListThreadsAsync(JsonElement p, CancellationToken ct)
    {
        p.Reject("parentThreadId", "ancestorThreadId", "attachedToThreadId");
        if (p.Flag("currentAccountOnly"))
            throw new ArgumentException("Gateway workspace history is local, not filtered by ChatGPT account.");
        ValidateWorkspace(p);
        var sort = p.Text("sortKey") ?? "created_at";
        if (sort is not ("created_at" or "updated_at" or "recency_at"))
            throw new ArgumentException("Unsupported thread sort key.");
        var direction = Direction(p, "desc");
        var providers = p.Get("modelProviders").Rows().Select(v => v.GetString()).ToHashSet(StringComparer.Ordinal);
        var sources = p.Get("sourceKinds").Rows().Select(v => v.GetString()).ToHashSet(StringComparer.Ordinal);
        var excluded = p.Get("excludedThreadIds").Rows().Select(v => v.GetString()).ToHashSet(StringComparer.Ordinal);
        var scope = "threads:" + JsonSerializer.Serialize(new
        {
            sort, archived = p.Flag("archived"), query = p.Text("searchTerm"),
            providers = providers.Order(), sources = sources.Order(), excluded = excluded.Order()
        });
        var cursor = DecodeCursor(p.Text("cursor"), scope, null);
        if (cursor is not null && cursor.Direction != direction)
            throw new ArgumentException("Thread cursor direction cannot change.");
        var anchor = cursor?.Source is { } token ? JsonSerializer.Deserialize<ThreadAnchor>(token) : null;
        var result = await ListMetadataAsync(p, ct);
        var ordered = result.Get("data").Rows()
            .Where(t => !p.Flag("archived") || t.Text("status") == "archived")
            .Where(t => sources.Count == 0 || sources.Contains("appServer"))
            .Where(t => excluded.Count == 0 || !excluded.Contains(_routes.External("thread", t.Required("id"))))
            .Select(t => (Thread: t, Anchor: new ThreadAnchor(
                DateTimeOffset.Parse(t.Required(sort == "created_at" ? "createdAt" : "lastActiveAt")).UtcTicks, t.Required("id"))));
        ordered = direction == "asc"
            ? ordered.OrderBy(t => t.Anchor.Timestamp).ThenBy(t => t.Anchor.Id, StringComparer.Ordinal)
            : ordered.OrderByDescending(t => t.Anchor.Timestamp).ThenByDescending(t => t.Anchor.Id, StringComparer.Ordinal);
        if (anchor is not null)
            ordered = ordered.Where(t =>
            {
                var compare = t.Anchor.Timestamp.CompareTo(anchor.Timestamp);
                if (compare == 0) compare = StringComparer.Ordinal.Compare(t.Anchor.Id, anchor.Id);
                return direction == "asc" ? compare > 0 : compare < 0;
            });
        var limit = p.Limit();
        var rows = new List<object>();
        ThreadAnchor? last = null;
        var more = false;
        foreach (var row in ordered)
        {
            var read = await RequestAsync("thread/read", new { threadId = row.Thread.Required("id") }, ct);
            var source = read.Get("thread");
            if (providers.Count != 0 && !providers.Contains(source.Get("configuration").Text("providerId"))) continue;
            if (rows.Count == limit) { more = true; break; }
            rows.Add(_projector.Thread(source));
            last = row.Anchor;
        }
        return new
        {
            data = rows,
            nextCursor = more ? EncodeCursor(new(scope, null, null, direction, JsonSerializer.Serialize(last))) : null,
            backwardsCursor = (string?)null, readStates = (object?)null
        };
    }
}
