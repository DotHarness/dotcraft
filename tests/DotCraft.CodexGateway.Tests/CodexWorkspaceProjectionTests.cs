using System.Text.Json;
using System.Threading.Channels;
using Xunit;
using Fixture = DotCraft.CodexGateway.Tests.CodexWorkspaceSessionTests.WorkspaceFixture;

namespace DotCraft.CodexGateway.Tests;

public sealed class CodexWorkspaceProjectionTests
{
    [Fact]
    public async Task LogicalToolsStayAtInvocationAcrossRawPagesAndTargetedReads()
    {
        using var fixture = new Fixture();
        await using var session = await fixture.ConnectAsync();
        var thread = await ThreadAsync(session);
        var call = Item("call", "toolCall", new { callId = "code", toolName = "CodeMode", arguments = new { code = "text(1)" } });
        var result = Item("result", "toolResult", new { callId = "code", toolName = "CodeMode", result = "1", success = true });
        fixture.Transport.ItemsOverride = [Entry(call), .. Enumerable.Range(0, 100).Select(index =>
            Entry(Item("reason-" + index, "reasoningContent", new { text = index.ToString() }))), Entry(result)];
        fixture.Transport.TurnOverride = Turn("completed", [call, result]);

        var first = Json(await session.HandleAsync("thread/items/list", Json(new { threadId = thread, limit = 1 }), default));
        var entry = Assert.Single(first.GetProperty("data").EnumerateArray());
        var operation = entry.GetProperty("item");
        Assert.Equal("dynamicToolCall", operation.GetProperty("type").GetString());
        Assert.Equal("CodeMode", operation.GetProperty("tool").GetString());
        Assert.Equal("text(1)", operation.GetProperty("arguments").GetProperty("code").GetString());
        Assert.Equal("1", operation.GetProperty("contentItems")[0].GetProperty("text").GetString());
        Assert.True(operation.GetProperty("success").GetBoolean());

        var continuation = Json(await session.HandleAsync("thread/items/list", Json(new
        {
            threadId = thread, limit = 100, cursor = first.GetProperty("nextCursor").GetString()
        }), default));
        var remaining = continuation.GetProperty("data").EnumerateArray().Select(item => item.Clone()).ToList();
        var cursor = continuation.GetProperty("nextCursor").GetString();
        while (cursor is not null)
        {
            var tail = Json(await session.HandleAsync("thread/items/list", Json(new { threadId = thread, cursor }), default));
            remaining.AddRange(tail.GetProperty("data").EnumerateArray().Select(item => item.Clone()));
            cursor = tail.GetProperty("nextCursor").GetString();
        }
        Assert.Equal(100, remaining.Count);
        Assert.Equal(100, remaining.Select(row => row.GetProperty("item").GetProperty("id").GetString()).Distinct().Count());
        Assert.DoesNotContain(remaining, row => row.GetProperty("item").GetProperty("type").GetString() == "dynamicToolCall");
        var detail = Json(await session.HandleAsync("thread/items/read", Json(new
        {
            threadId = thread, turnId = entry.GetProperty("turnId").GetString(),
            itemIds = new[] { operation.GetProperty("id").GetString() }
        }), default));
        Assert.Equal(operation.GetRawText(), detail.GetProperty("data")[0].GetProperty("item").GetRawText());
        Assert.Contains(fixture.Transport.Requests, request => request.Method == "thread/items/read"
            && request.Parameters.TryGetProperty("callIds", out _));

        var descending = Json(await session.HandleAsync("thread/items/list",
            Json(new { threadId = thread, sortDirection = "desc", limit = 100 }), default));
        Assert.DoesNotContain(descending.GetProperty("data").EnumerateArray(),
            item => item.GetProperty("item").GetProperty("type").GetString() == "dynamicToolCall");
        var last = Json(await session.HandleAsync("thread/items/list", Json(new
        {
            threadId = thread, sortDirection = "desc", cursor = descending.GetProperty("nextCursor").GetString(), limit = 100
        }), default));
        Assert.Single(last.GetProperty("data").EnumerateArray(),
            item => item.GetProperty("item").GetProperty("type").GetString() == "dynamicToolCall");
    }

    [Fact]
    public async Task FailedNotificationDeliveryStillDisposesTheSourceConnection()
    {
        using var fixture = new Fixture();
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = await fixture.ConnectAsync(message =>
        {
            if (message.GetProperty("method").GetString() != "item/started") return;
            failed.TrySetResult();
            throw new IOException("Remote stream is closed.");
        });
        try
        {
            var thread = await ThreadAsync(session);
            await session.HandleAsync("thread/resume", Json(new { threadId = thread, excludeTurns = true }), default);
            fixture.Transport.Notify("item/started", new
            {
                threadId = "thread-source", turnId = "turn_001",
                item = Item("active", "agentMessage", new { text = "" }, "streaming")
            });
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await failed.Task.WaitAsync(deadline.Token);
        }
        finally
        {
            await Assert.ThrowsAsync<IOException>(() => session.DisposeAsync().AsTask());
        }
        Assert.True(fixture.Transport.Disposed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LiveToolResultRetainsFinalizedArgumentsOnItsOriginalInvocation(bool initialCallIdKnown)
    {
        using var fixture = new Fixture();
        var notifications = Channel.CreateUnbounded<JsonElement>();
        await using var session = await fixture.ConnectAsync(message => notifications.Writer.TryWrite(message));
        var thread = await ThreadAsync(session);
        await session.HandleAsync("thread/resume", Json(new { threadId = thread, excludeTurns = true }), default);
        fixture.Transport.Notify("item/started", new
        {
            threadId = "thread-source", turnId = "turn_001",
            item = Item("live-call", "toolCall", new { callId = initialCallIdKnown ? "live" : "", toolName = "CodeMode", arguments = new { } }, "started")
        });
        fixture.Transport.Notify("item/completed", new
        {
            threadId = "thread-source", turnId = "turn_001",
            item = Item("live-call", "toolCall", new { callId = "live", toolName = "CodeMode", arguments = new { code = "text(42)" } })
        });
        fixture.Transport.Notify("item/completed", new
        {
            threadId = "thread-source", turnId = "turn_001",
            item = Item("live-result", "toolResult", new { callId = "live", toolName = "CodeMode", result = "42", success = true })
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = new List<JsonElement>();
        do { received.Add(await notifications.Reader.ReadAsync(deadline.Token)); }
        while (received[^1].GetProperty("method").GetString() != "item/completed");
        var started = Assert.Single(received, message => message.GetProperty("method").GetString() == "item/started")
            .GetProperty("params").GetProperty("item");
        var completed = received[^1].GetProperty("params").GetProperty("item");
        Assert.Equal(started.GetProperty("id").GetString(), completed.GetProperty("id").GetString());
        Assert.Equal("text(42)", completed.GetProperty("arguments").GetProperty("code").GetString());
        Assert.Equal("42", completed.GetProperty("contentItems")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task ItemContinuationKeepsItsSourceBoundaryWhenEarlierRowsAreRemoved()
    {
        using var fixture = new Fixture();
        await using var session = await fixture.ConnectAsync();
        var thread = await ThreadAsync(session);
        fixture.Transport.ItemsOverride = [
            Entry(Item("first", "agentMessage", new { text = "first" })),
            Entry(Item("second", "agentMessage", new { text = "second" })),
            Entry(Item("third", "agentMessage", new { text = "third" }))];
        var first = Json(await session.HandleAsync("thread/items/list", Json(new { threadId = thread, limit = 1 }), default));
        fixture.Transport.ItemsOverride.RemoveAt(0);
        var continued = Json(await session.HandleAsync("thread/items/list", Json(new
        {
            threadId = thread, limit = 1, cursor = first.GetProperty("nextCursor").GetString()
        }), default));
        Assert.Equal("second", Assert.Single(continued.GetProperty("data").EnumerateArray())
            .GetProperty("item").GetProperty("text").GetString());
    }

    [Fact]
    public async Task SummaryAndLiveMessagesPreserveSourcePhaseAndDeliveryWithoutTerminalReplay()
    {
        using var fixture = new Fixture();
        var notifications = Channel.CreateUnbounded<JsonElement>();
        await using var session = await fixture.ConnectAsync(message => notifications.Writer.TryWrite(message));
        var thread = await ThreadAsync(session);
        var rows = new[]
        {
            Item("hidden", "userMessage", new { text = "mailbox", deliveryMode = "subagentMailbox" }),
            Item("user", "userMessage", new { text = "question" }),
            Item("first", "agentMessage", new { text = "working", phase = "commentary" }),
            Item("tool", "dynamicToolCall", new { callId = "call", toolName = "lookup", status = "completed", success = true }),
            Item("last", "agentMessage", new { text = "answer", phase = "final_answer" }),
            Item("notice", "agentMessage", new { text = "running notice", phase = "final_answer", deliveryMode = "async" })
        };
        fixture.Transport.TurnOverride = Turn("completed", rows);
        fixture.Transport.ItemsOverride = rows.Select(Entry).ToList();
        var summary = Json(await session.HandleAsync("thread/turns/list", Json(new { threadId = thread }), default));
        var items = summary.GetProperty("data")[0].GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("question", items[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("answer", items[1].GetProperty("text").GetString());
        Assert.Equal("final_answer", items[1].GetProperty("phase").GetString());
        var finalId = items[1].GetProperty("id").GetString();

        await session.HandleAsync("thread/resume", Json(new { threadId = thread, excludeTurns = true }), default);
        fixture.Transport.Notify("item/started", new { threadId = "thread-source", turnId = "turn_001", item = rows[4] });
        fixture.Transport.Notify("item/completed", new { threadId = "thread-source", turnId = "turn_001", item = rows[4] });
        fixture.Transport.Notify("item/started", new { threadId = "thread-source", turnId = "turn_001", item = rows[5] });
        fixture.Transport.Notify("item/completed", new { threadId = "thread-source", turnId = "turn_001", item = rows[5] });
        fixture.Transport.Notify("turn/completed", new { turn = fixture.Transport.TurnOverride });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = new List<JsonElement>();
        do { received.Add(await notifications.Reader.ReadAsync(timeout.Token)); }
        while (received[^1].GetProperty("method").GetString() != "turn/completed");
        var completion = Assert.Single(received, n => n.GetProperty("method").GetString() == "item/completed"
            && n.GetProperty("params").GetProperty("item").GetProperty("delivery").ValueKind == JsonValueKind.Null);
        var asyncCompletion = Assert.Single(received, n => n.GetProperty("method").GetString() == "item/completed"
            && n.GetProperty("params").GetProperty("item").GetProperty("delivery").GetString() == "async");
        Assert.Equal("final_answer", asyncCompletion.GetProperty("params").GetProperty("item").GetProperty("phase").GetString());
        Assert.Equal(finalId, completion.GetProperty("params").GetProperty("item").GetProperty("id").GetString());
        Assert.Equal("final_answer", completion.GetProperty("params").GetProperty("item").GetProperty("phase").GetString());

        rows[4] = Item("last", "agentMessage", new { text = "answer", phase = "partial_answer" });
        fixture.Transport.TurnOverride = Turn("failed", rows);
        var failed = Json(await session.HandleAsync("thread/turns/list", Json(new { threadId = thread }), default));
        Assert.Equal("partial_answer", failed.GetProperty("data")[0].GetProperty("items")[1].GetProperty("phase").GetString());
    }

    [Fact]
    public async Task ResumeUsesPreparedSummaryOrMetadataAndRestoresBaselineBeforeContinuation()
    {
        using var fixture = new Fixture();
        var notifications = Channel.CreateUnbounded<JsonElement>();
        await using var session = await fixture.ConnectAsync(message => notifications.Writer.TryWrite(message));
        var thread = await ThreadAsync(session);
        var active = Item("active", "agentMessage", new { text = "prefix" }, "streaming");
        fixture.Transport.CurrentState = new { turnId = "turn_001", activeItems = new[] { active },
            pendingRequests = new[] { new { requestId = "pending-question", itemId = "question", method = "item/userInput/request" } } };
        fixture.Transport.OnResume = () => fixture.Transport.Notify("item/agentMessage/delta", new
        {
            threadId = "thread-source", turnId = "turn_001", itemId = "active", delta = "suffix"
        });
        var restored = Json(await session.HandleAsync("thread/resume", Json(new { threadId = thread, excludeTurns = true }), default));
        Assert.Equal(JsonValueKind.Null, restored.GetProperty("initialTurnsPage").ValueKind);
        Assert.DoesNotContain(fixture.Transport.Requests, request => request.Method is "thread/turns/list" or "thread/items/list");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = new List<JsonElement>();
        do { received.Add(await notifications.Reader.ReadAsync(timeout.Token)); }
        while (received[^1].GetProperty("method").GetString() != "item/agentMessage/delta");
        var baseline = Assert.Single(received, n => n.GetProperty("method").GetString() == "item/started").GetProperty("params");
        Assert.Equal("prefix", baseline.GetProperty("item").GetProperty("text").GetString());
        Assert.Equal(baseline.GetProperty("item").GetProperty("id").GetString(),
            received[^1].GetProperty("params").GetProperty("itemId").GetString());
        Assert.Equal("suffix", received[^1].GetProperty("params").GetProperty("delta").GetString());
        Assert.DoesNotContain(received, n => n.GetProperty("method").GetString() == "item/completed");
        fixture.Transport.OnResume = null;
        await session.HandleAsync("thread/resume", Json(new { threadId = thread, initialTurnsPage = new { itemsView = "summary" } }), default);
        var resume = fixture.Transport.Requests.Last(request => request.Method == "thread/resume").Parameters;
        Assert.Equal("summary", resume.GetProperty("initialTurnsPage").GetProperty("itemsView").GetString());
        Assert.DoesNotContain(fixture.Transport.Requests, request => request.Method is "thread/turns/list" or "thread/items/list");
    }

    [Theory]
    [InlineData("summary", 2)]
    [InlineData("notLoaded", 0)]
    [InlineData("full", 2)]
    public async Task AscendingResumeAttachesMetadataThenReadsOneRequestedPage(string view, int itemCount)
    {
        using var fixture = new Fixture();
        await using var session = await fixture.ConnectAsync();
        var thread = await ThreadAsync(session);
        var restored = Json(await session.HandleAsync("thread/resume", Json(new
        {
            threadId = thread, initialTurnsPage = new { sortDirection = "asc", itemsView = view, limit = 3 }
        }), default));
        var resume = Assert.Single(fixture.Transport.Requests, request => request.Method == "thread/resume");
        Assert.Equal(JsonValueKind.Null, resume.Parameters.GetProperty("initialTurnsPage").ValueKind);
        var history = Assert.Single(fixture.Transport.Requests, request => request.Method == "thread/turns/list");
        Assert.Equal("ascending", history.Parameters.GetProperty("sortDirection").GetString());
        Assert.Equal(3, history.Parameters.GetProperty("limit").GetInt32());
        Assert.Equal(view == "summary" ? "summary" : "none", history.Parameters.GetProperty("itemsView").GetString());
        var turn = Assert.Single(restored.GetProperty("initialTurnsPage").GetProperty("data").EnumerateArray());
        Assert.Equal(view, turn.GetProperty("itemsView").GetString());
        Assert.Equal(itemCount, turn.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task DeferralOnlyOmitsSuccessfulCommandAndPlainMcpOutputAndReadRestoresBoth()
    {
        using var fixture = new Fixture();
        await using var session = await fixture.ConnectAsync();
        var thread = await ThreadAsync(session);
        var rows = new[]
        {
            Item("command", "commandExecution", new { callId = "exec", command = "echo ok", status = "completed", exitCode = 0, aggregatedOutput = "ok" }),
            Item("mcp", "mcpToolCall", new { callId = "mcp", server = "fixture", toolName = "lookup", status = "completed",
                modelContentItems = new[] { new { type = "text", text = "mcp output" } } }),
            Item("failed", "commandExecution", new { callId = "bad", command = "exit 1", status = "failed", exitCode = 1, aggregatedOutput = "failure" }),
            Item("generic", "dynamicToolCall", new { callId = "ordinary", toolName = "ReadFile", status = "completed", success = true,
                contentItems = new[] { new { type = "text", text = "file content" } } })
        };
        fixture.Transport.TurnOverride = Turn("completed", rows);
        fixture.Transport.ItemsOverride = rows.Select(Entry).ToList();
        var turns = Json(await session.HandleAsync("thread/turns/list", Json(new { threadId = thread, itemsView = "notLoaded" }), default));
        var turn = turns.GetProperty("data")[0].GetProperty("id").GetString();
        var page = Json(await session.HandleAsync("thread/items/list", Json(new
        {
            threadId = thread, turnId = turn, deferOutput = new { commandExecution = true, mcpToolCall = true }
        }), default));
        var entries = page.GetProperty("data").EnumerateArray().ToArray();
        Assert.True(entries[0].GetProperty("outputDeferred").GetBoolean());
        Assert.True(entries[1].GetProperty("outputDeferred").GetBoolean());
        Assert.Equal(JsonValueKind.Null, entries[0].GetProperty("item").GetProperty("aggregatedOutput").ValueKind);
        Assert.False(entries[2].GetProperty("outputDeferred").GetBoolean());
        Assert.Equal("failure", entries[2].GetProperty("item").GetProperty("aggregatedOutput").GetString());
        Assert.False(entries[3].GetProperty("outputDeferred").GetBoolean());
        var read = Json(await session.HandleAsync("thread/items/read", Json(new
        {
            threadId = thread, turnId = turn, itemIds = entries.Take(2).Select(entry => entry.GetProperty("item").GetProperty("id").GetString()).ToArray()
        }), default));
        Assert.Contains("mcp output", read.GetRawText());
        Assert.Equal("ok", read.GetProperty("data")[0].GetProperty("item").GetProperty("aggregatedOutput").GetString());
    }

    private static object Item(string id, string type, object payload, string status = "completed") => new
    {
        id, turnId = "turn_001", type, payload, status,
        createdAt = "2026-11-13T00:00:00Z", completedAt = status == "completed" ? "2026-11-13T00:00:01Z" : null
    };

    private static object Entry(object item) => new { turnId = "turn_001", item };
    private static object Turn(string status, object[] items) => new
    {
        id = "turn_001", threadId = "thread-source", status, items, startedAt = "2026-11-13T00:00:00Z",
        completedAt = status == "completed" ? "2026-11-13T00:00:01Z" : null
    };
    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value);
    private static async Task<string> ThreadAsync(DotCraft.CodexGateway.Workspace.CodexWorkspaceSession session) =>
        Json(await session.HandleAsync("thread/list", Json(new { }), default)).GetProperty("data")[0].GetProperty("id").GetString()!;
}
