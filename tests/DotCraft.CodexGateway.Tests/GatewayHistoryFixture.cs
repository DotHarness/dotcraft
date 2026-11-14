using System.Text.Json;
using System.Threading.Channels;
using DotCraft.CodexGateway.Workspace;
using DotCraft.Sdk;
using DotCraft.Sdk.Wire;

namespace DotCraft.CodexGateway.Tests;

internal sealed class GatewayHistoryFixture : IDisposable
{
    private readonly string _state = Path.Combine(Path.GetTempPath(), "gateway-history-" + Guid.NewGuid());
    private readonly List<HistoryRecord> _records = [];
    private readonly Dictionary<string, JsonElement> _turns = [];
    private readonly Dictionary<string, SourceCursor> _cursors = [];
    private long _position;
    public HistoryTransport Transport { get; private set; } = null!;
    public bool EmptyNextItemPage { get; set; }
    public int SourcePageSize { get; set; } = 100;

    private sealed record SourceCursor(string Kind, string? Turn, string Direction, long Upper, long? Boundary);
    private sealed record HistoryRecord(string Kind, long Position, string TurnId, JsonElement Item);

    public GatewayHistoryFixture(int turns = 2, int itemsPerTurn = 4)
    {
        for (var i = 1; i <= turns; i++) AddTurn("turn-" + i, itemsPerTurn);
    }

    public void AddTurn(string id, int count)
    {
        _turns[id] = Json(new
        {
            id, threadId = "history-source", status = "completed",
            startedAt = "2026-11-14T00:00:00Z", completedAt = "2026-11-14T00:00:01Z",
            items = Array.Empty<object>(), itemsView = "none"
        });
        _records.Add(new("turnStarted", ++_position, id, default));
        for (var i = 0; i < count; i++)
        {
            var user = i == 0;
            var item = Json(new
            {
                id = id + "-item-" + i, turnId = id, type = user ? "userMessage" : "agentMessage",
                status = "completed", createdAt = "2026-11-14T00:00:00Z", completedAt = "2026-11-14T00:00:01Z",
                payload = new { text = id + " content " + i, phase = user ? null : i == count - 1 ? "final_answer" : "commentary" }
            });
            _records.Add(new("item", ++_position, id, item));
        }
        _records.Add(new("turnCompleted", ++_position, id, default));
    }

    public void RemoveItem(string id) =>
        _records.RemoveAll(row => row.Kind == "item" && row.Item.GetProperty("id").GetString() == id);

    public void AppendLateCommand(string turn, string output)
    {
        var item = Json(new
        {
            id = turn + "-late-command", turnId = turn, type = "commandExecution", status = "completed",
            createdAt = "2026-11-14T00:00:02Z", completedAt = "2026-11-14T00:00:03Z",
            payload = new
            {
                callId = "late-call", command = "fixture command", workingDirectory = Path.GetTempPath(),
                status = "completed", aggregatedOutput = output, exitCode = 0
            }
        });
        _records.Add(new("item", ++_position, turn, item));
    }

    public async Task<CodexWorkspaceSession> ConnectAsync(Action<JsonElement>? notification = null)
    {
        Transport = new HistoryTransport(Respond);
        var client = await DotCraftClient.ConnectAsync(Transport, new DotCraftClientOptions { AutoReconnect = true });
        return new(client, Path.GetTempPath(), _state, (message, _) =>
        {
            notification?.Invoke(message);
            return Task.CompletedTask;
        });
    }

    public static async Task<JsonElement> CallAsync(CodexWorkspaceSession session, string method, object parameters) =>
        Json(await session.HandleAsync(method, Json(parameters), default));

    public static async Task<string> DiscoverAsync(CodexWorkspaceSession session)
    {
        var listed = await CallAsync(session, "thread/list", new { });
        return listed.GetProperty("data")[0].GetProperty("id").GetString()!;
    }

    private object Respond(string method, JsonElement p) => method switch
    {
        "initialize" => new { serverInfo = new { name = "history-fixture", version = "1", protocolVersion = "1" }, capabilities = new { threadManagement = true } },
        "thread/list" => new { data = new[] { Thread() }, nextCursor = (string?)null },
        "thread/read" => new { thread = Thread() },
        "thread/resume" => new
        {
            thread = Thread(), instructionSources = Array.Empty<string>(),
            currentState = (object?)null,
            turnsBackwardsCursor = _turns.Count == 0 ? null : Save(new("turnBootstrap", null, "descending", _position, null)),
            itemsBackwardsCursor = !_records.Any(row => row.Kind == "item") ? null : Save(new("itemBootstrap", null, "descending", _position, null)),
            initialTurnsPage = p.TryGetProperty("initialTurnsPage", out var initial) && initial.ValueKind == JsonValueKind.Object
                ? TurnPage(initial) : null
        },
        "thread/turns/list" => TurnPage(p),
        "thread/items/list" => ItemPage(p),
        "thread/items/read" => ReadItems(p),
        "thread/timeline/list" => TimelinePage(p),
        "config/read" => new { config = new { Permissions = new { DefaultApprovalPolicy = "Prompt" } } },
        _ => throw new InvalidOperationException("Unexpected history fixture method " + method)
    };

    private object Thread() => new
    {
        id = "history-source", sessionId = "history-source", displayName = "Fixture history", workspacePath = Path.GetTempPath(),
        cwd = Path.GetTempPath(), createdAt = "2026-11-14T00:00:00Z", lastActiveAt = "2026-11-14T00:00:01Z",
        runtime = new { running = false }, configuration = new { providerId = "fixture", model = "fixture", approvalPolicy = "prompt" }
    };

    private SourceCursor Position(JsonElement p, string kind, string? turn = null)
    {
        var direction = Text(p, "sortDirection") ?? "descending";
        if (Text(p, "cursor") is not { } token) return new(kind, turn, direction, _position, null);
        var cursor = _cursors[token];
        var bootstrap = cursor.Kind == (kind == "items" ? "itemBootstrap" : "turnBootstrap");
        if (cursor.Direction != direction || (bootstrap ? cursor.Direction != "descending" : cursor.Kind != kind || cursor.Turn != turn))
            throw new InvalidOperationException("Source continuation scope mismatch.");
        return cursor with { Kind = kind, Turn = turn };
    }

    private object TurnPage(JsonElement p)
    {
        var cursor = Position(p, "turns");
        var limit = Limit(p);
        var selected = Select(_records.Where(row => row.Kind == "turnStarted"), cursor).Take(limit + 1).ToArray();
        var view = Text(p, "itemsView") ?? "summary";
        var data = selected.Take(limit).Select(row =>
        {
            var turn = _turns[row.TurnId].EnumerateObject().ToDictionary(field => field.Name, field => (object?)field.Value.Clone());
            var items = _records.Where(item => item.Kind == "item" && item.TurnId == row.TurnId && item.Position <= cursor.Upper).ToArray();
            turn["items"] = view == "none" ? Array.Empty<JsonElement>() : items.Where((_, i) => i == 0 || i == items.Length - 1).Select(item => item.Item).ToArray();
            turn["itemsView"] = view;
            return turn;
        }).ToArray();
        return new { data, nextCursor = selected.Length > limit ? Save(cursor with { Boundary = selected[limit - 1].Position }) : null };
    }

    private object ItemPage(JsonElement p)
    {
        var turn = Text(p, "turnId");
        var cursor = Position(p, "items", turn);
        if (EmptyNextItemPage)
        {
            EmptyNextItemPage = false;
            return new { data = Array.Empty<object>(), nextCursor = Save(cursor) };
        }
        var limit = Limit(p);
        var selected = Select(_records.Where(row => row.Kind == "item" && (turn is null || row.TurnId == turn)), cursor).Take(limit + 1).ToArray();
        return new
        {
            data = selected.Take(limit).Select(row => new { turnId = row.TurnId, item = row.Item }).ToArray(),
            nextCursor = selected.Length > limit ? Save(cursor with { Boundary = selected[limit - 1].Position }) : null
        };
    }

    private object ReadItems(JsonElement p)
    {
        var turn = p.GetProperty("turnId").GetString();
        var byCall = p.TryGetProperty("callIds", out var ids);
        if (!byCall) ids = p.GetProperty("itemIds");
        var requested = ids.EnumerateArray().Select(id => id.GetString()).ToHashSet();
        return new
        {
            data = _records.Where(row => row.Kind == "item" && row.TurnId == turn &&
                requested.Contains(byCall ? Text(row.Item.GetProperty("payload"), "callId") : Text(row.Item, "id")))
                .Select(row => new { turnId = row.TurnId, item = row.Item }).ToArray()
        };
    }

    private object TimelinePage(JsonElement p)
    {
        var cursor = Position(p, "timeline");
        var limit = Limit(p);
        var selected = Select(_records, cursor).Take(limit + 1).ToArray();
        var data = selected.Take(limit).Reverse().Select(row =>
        {
            var entry = new Dictionary<string, object?>
            {
                ["kind"] = row.Kind, ["position"] = row.Position, ["turnId"] = row.TurnId
            };
            if (row.Kind == "item") entry["item"] = row.Item;
            else
            {
                var turn = _turns[row.TurnId];
                entry["startedAt"] = turn.GetProperty("startedAt");
                if (row.Kind == "turnCompleted")
                {
                    entry["completedAt"] = turn.GetProperty("completedAt");
                    entry["status"] = turn.GetProperty("status");
                }
            }
            return entry;
        }).ToArray();
        return new { data, nextCursor = selected.Length > limit ? Save(cursor with { Boundary = selected[limit - 1].Position }) : null };
    }

    private static IEnumerable<HistoryRecord> Select(IEnumerable<HistoryRecord> rows, SourceCursor cursor)
    {
        rows = rows.Where(row => row.Position <= cursor.Upper &&
            (cursor.Boundary is null || (cursor.Direction == "ascending" ? row.Position > cursor.Boundary : row.Position < cursor.Boundary)));
        return cursor.Direction == "ascending" ? rows.OrderBy(row => row.Position) : rows.OrderByDescending(row => row.Position);
    }

    private int Limit(JsonElement p) => Math.Min(p.TryGetProperty("limit", out var limit) ? limit.GetInt32() : 20, SourcePageSize);
    private string Save(SourceCursor cursor)
    {
        var id = Guid.NewGuid().ToString("N");
        _cursors[id] = cursor;
        return id;
    }

    private static string? Text(JsonElement p, string key) =>
        p.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    internal static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value);

    public void Dispose()
    {
        if (Directory.Exists(_state)) Directory.Delete(_state, true);
    }

    internal sealed class HistoryTransport(Func<string, JsonElement, object> respond) : IReconnectableJsonRpcTransport
    {
        private readonly Channel<JsonDocument?> _incoming = Channel.CreateUnbounded<JsonDocument?>();
        public List<(string Method, JsonElement Parameters)> Requests { get; } = [];
        public int ReconnectCount { get; private set; }

        public Task WriteAsync(object message, CancellationToken cancellationToken = default)
        {
            var json = Json(message);
            if (!json.TryGetProperty("id", out var id)) return Task.CompletedTask;
            var method = json.GetProperty("method").GetString()!;
            var parameters = json.TryGetProperty("params", out var p) ? p.Clone() : Json(new { });
            Requests.Add((method, parameters));
            var result = respond(method, parameters);
            _incoming.Writer.TryWrite(JsonDocument.Parse(Json(new { id, result }).GetRawText()));
            return Task.CompletedTask;
        }

        public async Task<JsonDocument?> ReadAsync(CancellationToken cancellationToken = default) =>
            await _incoming.Reader.ReadAsync(cancellationToken);

        public void Disconnect() => _incoming.Writer.TryWrite(null);
        public Task ReconnectAsync(CancellationToken cancellationToken = default)
        {
            ReconnectCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _incoming.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
