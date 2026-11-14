using System.Text.Json;
using System.Threading.Channels;
using DotCraft.CodexGateway.Workspace;
using DotCraft.Sdk;
using DotCraft.Sdk.Wire;
using Xunit;

namespace DotCraft.CodexGateway.Tests;

public sealed class CodexWorkspaceSessionTests
{
    [Fact]
    public async Task WorkspaceDiscoveryProjectsRealBindingsAndKeepsScopedHistoryIdsAcrossSessions()
    {
        using var fixture = new WorkspaceFixture();
        string externalThread;
        string externalItem;
        await using (var session = await fixture.ConnectAsync())
        {
            var models = Json(await session.HandleAsync("model/list", Json(new { }), default));
            var model = models.GetProperty("data")[0];
            Assert.Equal("dotcraft/provider/real-model", model.GetProperty("id").GetString());
            Assert.True(model.GetProperty("isDefault").GetBoolean());
            var config = Json(await session.HandleAsync("config/read", Json(new { includeLayers = true }), default));
            Assert.Equal(model.GetProperty("id").GetString(), config.GetProperty("config").GetProperty("model").GetString());
            await session.HandleAsync("config/batchWrite", Json(new
            {
                edits = new[]
                {
                    new { keyPath = "model", value = "dotcraft/provider/real-model", mergeStrategy = "replace" },
                    new { keyPath = "model_reasoning_effort", value = "high", mergeStrategy = "replace" }
                }
            }), default);
            var edit = fixture.Transport.Requests.Single(r => r.Method == "config/batchWrite").Parameters.GetProperty("edits");
            Assert.Contains(edit.EnumerateArray(), entry => entry.GetProperty("keyPath").GetString() == "ProviderPreferences.provider.model");
            Assert.DoesNotContain("dotcraft/", edit.GetRawText());

            var listed = Json(await session.HandleAsync("thread/list",
                Json(new { modelProviders = Array.Empty<string>(), sortKey = "updated_at", sortDirection = "desc" }), default));
            externalThread = listed.GetProperty("data")[0].GetProperty("id").GetString()!;
            Assert.Equal("workspace", fixture.Transport.Requests.Single(r => r.Method == "thread/list").Parameters.GetProperty("scope").GetString());

            var items = Json(await session.HandleAsync("thread/items/list", Json(new { threadId = externalThread, limit = 1 }), default));
            var userContent = items.GetProperty("data")[0].GetProperty("item").GetProperty("content");
            Assert.Equal("hello", userContent[0].GetProperty("text").GetString());
            Assert.Equal("image", userContent[1].GetProperty("type").GetString());
            externalItem = items.GetProperty("data")[0].GetProperty("item").GetProperty("id").GetString()!;
            var next = items.GetProperty("nextCursor").GetString();
            Assert.NotNull(next);
            var continued = Json(await session.HandleAsync("thread/items/list", Json(new { threadId = externalThread, limit = 1, cursor = next }), default));
            Assert.Equal("reply", continued.GetProperty("data")[0].GetProperty("item").GetProperty("text").GetString());
            Assert.NotEqual(externalItem, continued.GetProperty("data")[0].GetProperty("item").GetProperty("id").GetString());
            var turn = items.GetProperty("data")[0].GetProperty("turnId").GetString();
            var anchored = Json(await session.HandleAsync("thread/items/list",
                Json(new { threadId = externalThread, turnId = turn, cursor = new { type = "item", itemId = externalItem } }), default));
            Assert.Single(anchored.GetProperty("data").EnumerateArray());
        }
        await using (var session = await fixture.ConnectAsync())
        {
            var listed = Json(await session.HandleAsync("thread/list", Json(new { }), default));
            Assert.Equal(externalThread, listed.GetProperty("data")[0].GetProperty("id").GetString());
            var items = Json(await session.HandleAsync("thread/items/list", Json(new { threadId = externalThread }), default));
            Assert.Equal(externalItem, items.GetProperty("data")[0].GetProperty("item").GetProperty("id").GetString());
        }
    }

    [Fact]
    public async Task AtomicInputAndStreamReuseTheSameProjectedTurnAndItemsWithoutSyntheticStarted()
    {
        using var fixture = new WorkspaceFixture();
        var notifications = Channel.CreateUnbounded<JsonElement>();
        await using var session = await fixture.ConnectAsync(message => notifications.Writer.TryWrite(message));
        var listed = Json(await session.HandleAsync("thread/list", Json(new { }), default));
        var thread = listed.GetProperty("data")[0].GetProperty("id").GetString()!;
        fixture.Transport.OnResume = () =>
            fixture.Transport.Notify("item/started", new { threadId = "thread-source", turnId = "turn_001", item = WorkspaceFixture.Item("item_002", "agentMessage", "reply") });
        fixture.Transport.OnStart = () =>
        {
            fixture.Transport.Notify("item/agentMessage/delta", new { threadId = "thread-source", turnId = "turn_001", itemId = "item_002", delta = "reply", deltaKind = "agentMessage" });
            fixture.Transport.Notify("turn/completed", new { turn = WorkspaceFixture.Turn("completed") });
        };
        var submitted = Json(await session.HandleAsync("turn/start", Json(new
        {
            threadId = thread, model = "not-a-model", effort = "low",
            collaborationMode = new { mode = "plan", settings = new { model = "dotcraft/provider/real-model", reasoning_effort = "high", developer_instructions = (string?)null } },
            cwd = Path.GetTempPath(), runtimeWorkspaceRoots = new[] { Path.GetTempPath() }, serviceTier = "fast",
            input = new[] { new { type = "text", text = "guidance" } }
        }), default));
        var turnId = submitted.GetProperty("turn").GetProperty("id").GetString();
        var outbound = fixture.Transport.Requests.Single(r => r.Method == "turn/start").Parameters;
        Assert.Equal("provider", outbound.GetProperty("providerId").GetString());
        Assert.Equal("real-model", outbound.GetProperty("model").GetString());
        Assert.Equal("high", outbound.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.True(outbound.GetProperty("reasoning").GetProperty("enabled").GetBoolean());
        Assert.Equal("plan", outbound.GetProperty("mode").GetString());
        Assert.Equal("fast", outbound.GetProperty("speed").GetString());
        Assert.DoesNotContain(fixture.Transport.Requests, request => request.Method == "thread/config/update");

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = new List<JsonElement>();
        while (true)
        {
            var notification = await notifications.Reader.ReadAsync(deadline.Token);
            received.Add(notification);
            if (notification.GetProperty("method").GetString() == "turn/completed") break;
        }
        Assert.DoesNotContain(received, n => n.GetProperty("method").GetString() == "turn/started");
        var delta = received.Single(n => n.GetProperty("method").GetString() == "item/agentMessage/delta").GetProperty("params");
        Assert.Equal(turnId, delta.GetProperty("turnId").GetString());
        var history = Json(await session.HandleAsync("thread/items/list", Json(new { threadId = thread }), default));
        Assert.Equal(delta.GetProperty("itemId").GetString(), history.GetProperty("data")[1].GetProperty("item").GetProperty("id").GetString());
        fixture.Transport.IncludeToolAndInternalMessages = true;
        var richHistory = Json(await session.HandleAsync("thread/items/list", Json(new { threadId = thread }), default));
        Assert.Contains(richHistory.GetProperty("data").EnumerateArray(),
            item => item.GetProperty("item").GetProperty("type").GetString() == "dynamicToolCall" &&
                item.GetProperty("item").GetProperty("contentItems")[0].GetProperty("text").GetString() == "file contents");
        Assert.Contains(richHistory.GetProperty("data").EnumerateArray(),
            item => item.GetProperty("item").GetProperty("type").GetString() == "mcpToolCall");
        Assert.Contains("MCP result", richHistory.GetRawText());
        Assert.DoesNotContain("hidden mailbox", richHistory.GetRawText());
        Assert.DoesNotContain("private-payload", richHistory.GetRawText());
        var command = Assert.Single(richHistory.GetProperty("data").EnumerateArray(),
            item => item.GetProperty("item").GetProperty("type").GetString() == "commandExecution");
        var file = Assert.Single(richHistory.GetProperty("data").EnumerateArray(),
            item => item.GetProperty("item").GetProperty("type").GetString() == "fileChange");
        Assert.Equal("@@ -1 +1 @@\n-old\n+new", file.GetProperty("item").GetProperty("changes")[0].GetProperty("diff").GetString());
        var reread = Json(await session.HandleAsync("thread/items/read", Json(new
        {
            threadId = thread, turnId,
            itemIds = new[] { command.GetProperty("item").GetProperty("id").GetString(), file.GetProperty("item").GetProperty("id").GetString() }
        }), default));
        Assert.Equal(2, reread.GetProperty("data").GetArrayLength());
        await session.HandleAsync("thread/archive", Json(new { threadId = thread }), default);
        var restored = Json(await session.HandleAsync("thread/unarchive", Json(new { threadId = thread }), default));
        Assert.Equal(thread, restored.GetProperty("thread").GetProperty("id").GetString());
        await session.HandleAsync("turn/interrupt", Json(new { threadId = thread, turnId }), default);
        Assert.Equal("turn_001", fixture.Transport.Requests.Last().Parameters.GetProperty("turnId").GetString());
        await Assert.ThrowsAsync<ArgumentException>(() => session.HandleAsync("turn/start",
            Json(new { threadId = thread, input = new[] { new { type = "localImage", path = "/not-authorized" } } }), default));
        Assert.Single(fixture.Transport.Requests, r => r.Method == "turn/start");
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value);

    internal sealed class WorkspaceFixture : IDisposable
    {
        private readonly string _state = Path.Combine(Path.GetTempPath(), "gateway-workspace-" + Guid.NewGuid());
        public FakeTransport Transport { get; private set; } = null!;

        public async Task<CodexWorkspaceSession> ConnectAsync(Action<JsonElement>? notification = null)
        {
            Transport = new FakeTransport();
            var client = await DotCraftClient.ConnectAsync(Transport);
            return new CodexWorkspaceSession(client, Path.GetTempPath(), _state, (message, _) =>
            {
                notification?.Invoke(message);
                return Task.CompletedTask;
            });
        }

        public static object Item(string id, string type, string text, object? payload = null) => new
        {
            id, turnId = "turn_001", type, status = "completed",
            createdAt = "2026-11-13T00:00:00Z", completedAt = "2026-11-13T00:00:01Z",
            payload = payload ?? (type == "userMessage" ? (object)new
            {
                text = "display-only", clientUserMessageId = "client-input",
                nativeInputParts = new object[] { new { type = "text", text }, new { type = "image", url = "data:image/png;base64,AA==" } }
            } : new { text, phase = type == "agentMessage" ? "final_answer" : null })
        };

        public static object Turn(string status = "completed") => new
        {
            id = "turn_001", threadId = "thread-source", status,
            startedAt = "2026-11-13T00:00:00Z", completedAt = status == "completed" ? "2026-11-13T00:00:01Z" : null,
            items = new[] { Item("item_001", "userMessage", "hello"), Item("item_002", "agentMessage", "reply") }
        };

        public void Dispose()
        {
            if (Directory.Exists(_state)) Directory.Delete(_state, true);
        }
    }

    internal sealed class FakeTransport : IJsonRpcTransport
    {
        private readonly Channel<JsonDocument> _incoming = Channel.CreateUnbounded<JsonDocument>();
        public List<(string Method, JsonElement Parameters)> Requests { get; } = [];
        public bool Disposed { get; private set; }
        public Action? OnStart { get; set; }
        public Action? OnResume { get; set; }
        public bool IncludeToolAndInternalMessages { get; set; }
        public List<object>? ItemsOverride { get; set; }
        public object? TurnOverride { get; set; }
        public object? CurrentState { get; set; }

        public Task WriteAsync(object message, CancellationToken cancellationToken = default)
        {
            var json = Json(message);
            var method = json.GetProperty("method").GetString()!;
            if (!json.TryGetProperty("id", out var id)) return Task.CompletedTask;
            var p = json.TryGetProperty("params", out var parameters) ? parameters.Clone() : Json(new { });
            Requests.Add((method, p));
            object result = method switch
            {
                "initialize" => new { serverInfo = new { name = "fixture", version = "1", protocolVersion = "1" }, capabilities = new { threadManagement = true } },
                "config/read" => new { config = new { ProviderId = "provider", ProviderPreferences = new { provider = new { model = "real-model", reasoning = new { effort = "high" } } } } },
                "provider/list" => new { providers = new[] { new { id = "provider" } } },
                "model/list" => new { success = true, models = new[] { new { id = "real-model", displayName = "Real model", inputModalities = new[] { "text" }, speed = new { supportedModes = new[] { "standard", "fast" } }, reasoning = new { defaultEffort = "high", supportedEfforts = new[] { new { effort = "high", description = "High" } } } } } },
                "thread/list" => new { data = new[] { new
                {
                    id = "thread-source", displayName = "Existing desktop conversation", workspacePath = Path.GetTempPath(),
                    createdAt = "2026-11-13T00:00:00Z", lastActiveAt = "2026-11-13T00:00:01Z", status = "active"
                } }, nextCursor = (string?)null },
                "thread/read" => new { thread = Thread() },
                "thread/resume" => new { thread = Thread(), instructionSources = Array.Empty<string>(), currentState = CurrentState,
                    turnsBackwardsCursor = "source-turn-head", itemsBackwardsCursor = "source-item-head",
                    initialTurnsPage = p.TryGetProperty("initialTurnsPage", out var initial) && initial.ValueKind == JsonValueKind.Object
                        ? (object)new { data = new[] { TurnOverride ?? WorkspaceFixture.Turn() } } : null },
                "thread/turns/list" => new { data = new[] { TurnOverride ?? WorkspaceFixture.Turn() } },
                "thread/items/list" => ItemPage(p),
                "thread/items/read" => ReadItems(p),
                "config/batchWrite" => new { status = "ok", version = "v2", filePath = Path.Combine(Path.GetTempPath(), ".craft", "config.json") },
                "thread/archive" or "thread/unarchive" or "thread/config/update" => new { },
                "turn/start" => new { turn = WorkspaceFixture.Turn("running") },
                "turn/interrupt" => new { },
                _ => throw new InvalidOperationException("Unexpected fixture method " + method)
            };
            if (method == "turn/start") OnStart?.Invoke();
            if (method == "thread/resume") OnResume?.Invoke();
            _incoming.Writer.TryWrite(JsonDocument.Parse(Json(new { id, result }).GetRawText()));
            return Task.CompletedTask;
        }

        private List<object> SourceItems()
        {
            if (ItemsOverride is not null) return ItemsOverride;
            var items = new List<object> {
                new { turnId = "turn_001", item = WorkspaceFixture.Item("item_001", "userMessage", "hello") },
                new { turnId = "turn_001", item = WorkspaceFixture.Item("item_002", "agentMessage", "reply") } };
            if (IncludeToolAndInternalMessages)
            {
                items.Add(new { turnId = "turn_001", item = WorkspaceFixture.Item("item_003", "userMessage", "", new { text = "hidden mailbox", deliveryMode = "subagentMailbox" }) });
                items.Add(new { turnId = "turn_001", item = WorkspaceFixture.Item("item_004", "toolResult", "", new { toolName = "ReadFile", result = "file contents", structuredContent = "private-payload", _meta = "private-payload" }) });
                items.Add(new { turnId = "turn_001", item = WorkspaceFixture.Item("item_005", "mcpToolCall", "", new { callId = "mcp-call", server = "fixture-mcp", toolName = "lookup", status = "completed", modelContentItems = new[] { new { type = "text", text = "MCP result" } }, structuredContent = "private-payload", _meta = "private-payload" }) });
                var shell = new { kind = "CoreNative", sourceId = "core-native", sourceToolId = "Exec" };
                items.Add(new { turnId = "turn_001", item = WorkspaceFixture.Item("item_006", "toolCall", "", new { callId = "exec-call", source = shell, toolName = "Exec", arguments = new { command = "git status" } }) });
                items.Add(new { turnId = "turn_001", item = WorkspaceFixture.Item("item_007", "commandExecution", "", new { callId = "exec-call", command = "git status", workingDirectory = Path.GetTempPath(), status = "completed", aggregatedOutput = "clean" }) });
                items.Add(new { turnId = "turn_001", item = WorkspaceFixture.Item("item_008", "toolResult", "", new { callId = "exec-call", source = shell, toolName = "Exec", result = "clean" }) });
                items.Add(new { turnId = "turn_001", item = WorkspaceFixture.Item("item_009", "toolResult", "", new
                {
                    callId = "edit-call", toolName = "EditFile", result = "edited", success = true,
                    source = new { kind = "CoreNative", sourceId = "core-native", sourceToolId = "EditFile" },
                    structuredContent = new { kind = "fileChange", writeState = "applied", changes = new[] { new { path = "a.txt", kind = "update", diff = "@@ -1 +1 @@\n-old\n+new" } } }
                }) });
            }
            return items;
        }

        private object ReadItems(JsonElement p)
        {
            var byCall = p.TryGetProperty("callIds", out var calls);
            var ids = byCall ? calls : p.GetProperty("itemIds");
            return new { data = SourceItems().Select(Json).Where(entry =>
            {
                var item = entry.GetProperty("item");
                var key = byCall && item.GetProperty("payload").TryGetProperty("callId", out var call)
                    ? call.GetString() : byCall ? null : item.GetProperty("id").GetString();
                return ids.EnumerateArray().Any(id => id.GetString() == key);
            }).ToArray() };
        }

        private readonly Dictionary<string, int> _itemOrdinals = new();

        private object ItemPage(JsonElement p)
        {
            var items = SourceItems().Select(Json).Select(entry =>
            {
                var key = entry.GetProperty("turnId").GetString() + "\n" + entry.GetProperty("item").GetProperty("id").GetString();
                if (!_itemOrdinals.TryGetValue(key, out var ordinal))
                    _itemOrdinals[key] = ordinal = _itemOrdinals.Count + 1;
                return (Entry: entry, Ordinal: ordinal);
            }).ToArray();
            var descending = p.GetProperty("sortDirection").GetString() == "descending";
            var after = p.TryGetProperty("cursor", out var cursor) && cursor.ValueKind == JsonValueKind.String
                ? int.Parse(cursor.GetString()!) : (int?)null;
            var selected = items.Where(item => after is null || (descending ? item.Ordinal < after : item.Ordinal > after));
            selected = descending ? selected.OrderByDescending(item => item.Ordinal) : selected.OrderBy(item => item.Ordinal);
            var limit = p.GetProperty("limit").GetInt32();
            var page = selected.Take(limit + 1).ToArray();
            var data = page.Take(limit).Select(item => item.Entry).ToArray();
            return new { data, nextCursor = page.Length > limit ? page[limit - 1].Ordinal.ToString() : null };
        }

        private static object Thread() => new
        {
            id = "thread-source", sessionId = "thread-source", workspacePath = Path.GetTempPath(),
            cwd = Path.GetTempPath(), createdAt = "2026-11-13T00:00:00Z", lastActiveAt = "2026-11-13T00:00:01Z",
            status = "active", displayName = "Existing desktop conversation", runtime = new { running = true },
            configuration = new { providerId = "provider", model = "real-model", reasoning = new { effort = "high" } }
        };

        public void Notify(string method, object parameters) =>
            _incoming.Writer.TryWrite(JsonDocument.Parse(Json(new { method, @params = parameters }).GetRawText()));

        public async Task<JsonDocument?> ReadAsync(CancellationToken cancellationToken = default) =>
            await _incoming.Reader.ReadAsync(cancellationToken);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _incoming.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
