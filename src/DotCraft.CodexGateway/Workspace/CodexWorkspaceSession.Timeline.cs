using System.Text.Json;

namespace DotCraft.CodexGateway.Workspace;

public sealed partial class CodexWorkspaceSession
{
    private async Task<object> TimelineAsync(JsonElement p, CancellationToken ct)
    {
        var thread = ThreadId(p);
        var requested = p.Get("limit");
        var limit = requested.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? 100
            : requested.ValueKind == JsonValueKind.Number && requested.TryGetUInt32(out var count)
                ? (int)Math.Clamp(count, 1u, 500u) : throw new ArgumentException("'limit' must be a nonnegative integer.");
        var cursor = DecodeCursor(p.Text("cursor"), "timeline", thread);
        var page = await RequestAsync("thread/timeline/list", new
        {
            threadId = thread, cursor = cursor?.Source, limit
        }, ct);
        var rows = page.Get("data").Rows().ToArray();
        await HydrateOperationsAsync(thread, rows.Where(row => row.Text("kind") == "item"), ct);
        var data = new List<object>();
        foreach (var row in rows)
        {
            var turn = row.Required("turnId");
            var turnId = _routes.External("turn", thread, turn);
            var position = row.Get("position").GetInt64();
            object entry;
            switch (row.Required("kind"))
            {
                case "item":
                    var source = row.Get("item");
                    var item = _projector.Item(thread, turn, source);
                    if (item is null || !_projector.IsInvocation(thread, turn, source)) continue;
                    entry = new { type = "item", position, turnId, item };
                    break;
                case "turnStarted":
                    entry = new { type = "turnStarted", position, turnId, startedAt = row.Seconds("startedAt") };
                    break;
                case "turnCompleted":
                    entry = new
                    {
                        type = "turnCompleted", position, turnId,
                        status = WorkspaceProjector.TurnStatus(row.Text("status")), error = WorkspaceProjector.TurnError(row),
                        startedAt = row.Seconds("startedAt"), completedAt = row.Seconds("completedAt"),
                        durationMs = row.Milliseconds("completedAt") - row.Milliseconds("startedAt")
                    };
                    break;
                default:
                    throw new InvalidDataException("Unsupported source timeline entry kind.");
            }
            data.Add(entry);
        }
        return new
        {
            data, nextCursor = WrapCursor("timeline", thread, null, "desc", page.Text("nextCursor")),
            activeRealtimeSessionAtPageStart = (string?)null
        };
    }
}
