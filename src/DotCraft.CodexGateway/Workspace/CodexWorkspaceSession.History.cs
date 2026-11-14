using System.Text.Json;

namespace DotCraft.CodexGateway.Workspace;

public sealed partial class CodexWorkspaceSession
{
    private async Task<PageCursor> LocateItemAsync(string thread, string? turn, string direction, string anchor,
        bool inclusive, CancellationToken ct)
    {
        string? cursor = null;
        do
        {
            var page = await SourceItemsAsync(thread, turn, direction, cursor, ct);
            var rows = page.Get("data").Rows().ToArray();
            await HydrateOperationsAsync(thread, rows, ct);
            for (var i = 0; i < rows.Length; i++)
            {
                var source = rows[i].Get("item");
                var projected = _projector.Item(thread, rows[i].Required("turnId"), source);
                if (projected is null || !_projector.IsInvocation(thread, rows[i].Required("turnId"), source)) continue;
                if ((string)projected["id"]! == anchor)
                    return new("items", thread, turn, direction, cursor, i + (inclusive ? 0 : 1));
            }
            cursor = page.Text("nextCursor");
        } while (cursor is not null);
        throw new ArgumentException("Item anchor is not present in the requested history.");
    }

    private Task<JsonElement> SourceItemsAsync(string thread, string? turn, string direction, string? cursor,
        CancellationToken ct, int limit = 100) =>
        RequestAsync("thread/items/list", new
        {
            threadId = thread, turnId = turn, cursor, limit,
            sortDirection = direction == "asc" ? "ascending" : "descending"
        }, ct);

    private async Task<object> ItemsAsync(JsonElement p, CancellationToken ct)
    {
        var thread = ThreadId(p);
        var turn = p.Text("turnId") is { } id ? _routes.Resolve(id, "turn", thread) : null;
        var direction = Direction(p, "asc");
        var deferral = p.Get("deferOutput");
        if (deferral.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.Object))
            throw new ArgumentException("deferOutput must be an object.");
        if (deferral.ValueKind == JsonValueKind.Object)
        {
            if (turn is null) throw new ArgumentException("deferOutput requires turnId.");
            foreach (var field in deferral.EnumerateObject())
                if (field.Name is not ("commandExecution" or "mcpToolCall") || field.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new ArgumentException("Unsupported output deferral flag.");
        }
        var value = p.Get("cursor");
        PageCursor? position;
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (turn is null || value.Text("type") != "item")
                throw new ArgumentException("An item anchor requires turnId and type=item.");
            var anchor = value.Required("itemId");
            _routes.Resolve(anchor, "item", thread, turn);
            position = await LocateItemAsync(thread, turn, direction, anchor, false, ct);
        }
        else
        {
            position = ItemCursor(p.Text("cursor"), thread, turn, direction);
            if (position?.Anchor is { } anchor)
                position = await LocateItemAsync(thread, turn, direction, anchor, position.Inclusive, ct);
            else if (position is not null && position.Direction != direction)
                throw new ArgumentException("Use backwardsCursor to reverse pagination.");
        }
        position ??= new("items", thread, turn, direction, null);
        var limit = p.Limit(100);
        var page = await SourceItemsAsync(thread, turn, direction, position.Source, ct);
        var rows = page.Get("data").Rows().ToArray();
        await HydrateOperationsAsync(thread, rows, ct);
        var result = new List<object>();
        string? first = null;
        var index = position.Offset;
        for (; index < rows.Length && result.Count < limit; index++)
        {
            var item = rows[index].Get("item");
            var sourceTurn = rows[index].Required("turnId");
            var projected = _projector.Item(thread, sourceTurn, item);
            if (projected is null || !_projector.IsInvocation(thread, sourceTurn, item)) continue;
            first ??= (string)projected["id"]!;
            var deferred = DeferOutput(deferral, projected, _projector.OutputSource(item, projected));
            result.Add(ItemEntry(thread, sourceTurn, item, projected, deferred));
        }
        var sourceNext = page.Text("nextCursor");
        if (index < rows.Length)
        {
            var boundary = await SourceItemsAsync(thread, turn, direction, position.Source, ct, index);
            sourceNext = boundary.Text("nextCursor");
        }
        var next = WrapCursor("items", thread, turn, direction, sourceNext);
        return new
        {
            data = result, nextCursor = next,
            backwardsCursor = first is null ? null : EncodeCursor(new("items", thread, turn, "both", null, Anchor: first, Inclusive: true))
        };
    }

    private object ItemEntry(string thread, string turn, JsonElement source, object item, bool outputDeferred = false) => new
    {
        turnId = _routes.External("turn", thread, turn), item, outputDeferred,
        startedAtMs = item is Dictionary<string, object?> values ? values.GetValueOrDefault("startedAtMs") : source.Milliseconds("createdAt"),
        completedAtMs = item is Dictionary<string, object?> completed ? completed.GetValueOrDefault("completedAtMs") : source.Milliseconds("completedAt")
    };

    private async Task<object> ReadItemsAsync(JsonElement p, CancellationToken ct)
    {
        var thread = ThreadId(p);
        var turn = _routes.Resolve(p.Required("turnId"), "turn", thread);
        var requested = p.Get("itemIds").Rows().Select(value => value.GetString()!).ToArray();
        var ids = requested.Select(value => _routes.Resolve(value, "item", thread, turn)).ToArray();
        if (ids.Length is < 1 or > 100) throw new ArgumentException("itemIds must contain 1 to 100 IDs.");
        var calls = ids.Where(id => id.StartsWith("command:", StringComparison.Ordinal) ||
            id.StartsWith("call:", StringComparison.Ordinal)).Select(id => id[(id.IndexOf(':') + 1)..]).ToArray();
        var ordinary = ids.Where(id => !id.StartsWith("command:", StringComparison.Ordinal) &&
            !id.StartsWith("call:", StringComparison.Ordinal)).ToArray();
        var rows = new List<JsonElement>();
        if (calls.Length > 0)
        {
            var result = await RequestAsync("thread/items/read", new { threadId = thread, turnId = turn, callIds = calls }, ct);
            rows.AddRange(result.Get("data").Rows());
        }
        if (ordinary.Length > 0)
        {
            var result = await RequestAsync("thread/items/read", new { threadId = thread, turnId = turn, itemIds = ordinary }, ct);
            rows.AddRange(result.Get("data").Rows());
        }
        var projectedItems = _projector.ProjectLogicalItems(thread, turn, rows.Select(row => row.Get("item")))
            .Cast<Dictionary<string, object?>>().ToDictionary(item => (string)item["id"]!);
        return new
        {
            data = requested.Where(projectedItems.ContainsKey).Select(id =>
            {
                var source = rows.Last(row => _projector.ItemId(thread, turn, row.Get("item")) == id).Get("item");
                return ItemEntry(thread, turn, source, projectedItems[id]);
            }).ToArray()
        };
    }
}
