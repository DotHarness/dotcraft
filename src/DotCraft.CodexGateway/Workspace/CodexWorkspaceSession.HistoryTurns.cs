using System.Text.Json;

namespace DotCraft.CodexGateway.Workspace;

public sealed partial class CodexWorkspaceSession
{
    private static void ValidateItemsView(string view)
    {
        if (view is not ("notLoaded" or "summary" or "full"))
            throw new ArgumentException("Unsupported itemsView.");
    }

    private async Task<HistoryPage> TurnsAsync(JsonElement p, CancellationToken ct)
    {
        var thread = ThreadId(p);
        var direction = Direction(p, "desc");
        var cursor = DecodeCursor(p.Text("cursor"), "turns", thread);
        if (cursor is not null && cursor.Direction != "both" && cursor.Direction != direction)
            throw new ArgumentException("Use backwardsCursor to reverse pagination.");
        var view = p.Text("itemsView") ?? "summary";
        ValidateItemsView(view);
        var result = await RequestAsync("thread/turns/list", new
        {
            threadId = thread, cursor = cursor?.Source, limit = p.Limit(),
            sortDirection = direction == "asc" ? "ascending" : "descending",
            itemsView = view == "summary" ? "summary" : "none"
        }, ct);
        return await ProjectTurnPageAsync(thread, result, direction, view, ct);
    }

    private async Task<HistoryPage?> InitialPageAsync(string thread, JsonElement p, JsonElement source, CancellationToken ct)
    {
        var initial = p.Get("initialTurnsPage");
        if (initial.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return null;
        if (initial.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("initialTurnsPage must be an object.");
        var direction = Direction(initial, "desc");
        var view = initial.Text("itemsView") ?? "summary";
        ValidateItemsView(view);
        if (direction == "asc" || source.ValueKind != JsonValueKind.Object)
            return await TurnsAsync(WorkspaceJson.Element(new
            {
                threadId = p.Required("threadId"), limit = initial.Limit(20), itemsView = view, sortDirection = direction
            }), ct);
        return await ProjectTurnPageAsync(thread, source, direction, view, ct);
    }

    private async Task<HistoryPage> ProjectTurnPageAsync(string thread, JsonElement source, string direction,
        string view, CancellationToken ct)
    {
        var turns = new List<object>();
        var budget = new HistoryReadBudget();
        foreach (var turn in source.Get("data").Rows())
        {
            object[]? items = view == "full" && turn.Text("itemsView") != "full"
                ? await AllTurnItemsAsync(thread, turn.Required("id"), ct) : null;
            var projected = _projector.Turn(thread, turn, items, view);
            budget.Add(projected);
            turns.Add(projected);
        }
        return new(turns.ToArray(),
            WrapCursor("turns", thread, null, direction, source.Text("nextCursor")),
            WrapCursor("turns", thread, null, "both", source.Text("backwardsCursor")));
    }

    private async Task<object[]> ReadAllTurnsAsync(string thread, CancellationToken ct)
    {
        var turns = new List<object>();
        var budget = new HistoryReadBudget();
        var cursors = new HashSet<string>();
        string? cursor = null;
        do
        {
            var result = await RequestAsync("thread/turns/list", new
            {
                threadId = thread, cursor, limit = 100, sortDirection = "ascending", itemsView = "none"
            }, ct);
            foreach (var turn in result.Get("data").Rows())
            {
                var items = await AllTurnItemsAsync(thread, turn.Required("id"), ct);
                var projected = _projector.Turn(thread, turn, items, "full");
                budget.Add(projected);
                turns.Add(projected);
            }
            cursor = result.Text("nextCursor");
            if (cursor is not null && !cursors.Add(cursor))
                throw new InvalidDataException("The source repeated a history continuation.");
        } while (cursor is not null);
        return turns.ToArray();
    }

    private async Task<object[]> AllTurnItemsAsync(string thread, string turn, CancellationToken ct)
    {
        var order = new List<string>();
        var items = new Dictionary<string, Dictionary<string, object?>>();
        var cursors = new HashSet<string>();
        string? cursor = null;
        do
        {
            var result = await SourceItemsAsync(thread, turn, "asc", cursor, ct);
            foreach (var item in _projector.ProjectLogicalItems(thread, turn,
                         result.Get("data").Rows().Select(entry => entry.Get("item"))).Cast<Dictionary<string, object?>>())
            {
                var id = (string)item["id"]!;
                if (!items.ContainsKey(id)) order.Add(id);
                items[id] = item;
            }
            cursor = result.Text("nextCursor");
            if (cursor is not null && !cursors.Add(cursor))
                throw new InvalidDataException("The source repeated a history continuation.");
        } while (cursor is not null);
        var completed = order.Select(id => (object)_projector.LatestOperation(items[id])).ToArray();
        var budget = new HistoryReadBudget();
        foreach (var item in completed) budget.Add(item);
        return completed;
    }
}
