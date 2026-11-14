using System.Text.Json;
using System.Threading.Channels;
using Xunit;
using Fixture = DotCraft.CodexGateway.Tests.CodexWorkspaceSessionTests.WorkspaceFixture;

namespace DotCraft.CodexGateway.Tests;

public sealed class CodexWorkspaceCompactPresentationTests
{
    [Fact]
    public async Task TrustedWrappersAndNestedContextDoNotFlattenButRealCommandsAndForeignToolsRemain()
    {
        using var fixture = new Fixture();
        await using var session = await fixture.ConnectAsync();
        var thread = await ThreadAsync(session);
        var wrapper = Source("code-mode", "CodeMode");
        var read = Source("core-native", "ReadFile");
        var shell = Source("core-native", "Exec");
        var clock = Source("user-coordination", "CurrentTime");
        var foreign = Source("sample-plugin", "ReadFile", "Plugin");
        fixture.Transport.ItemsOverride =
        [
            Entry(Call("wrapper", "code", "CodeMode", wrapper, new { code = "text(1)" }, presentation: "core.code-mode")),
            Entry(Call("read", "read-call", "ReadFile", read, new { path = "notes.txt" }, "code")),
            Entry(Result("read-result", "read-call", "ReadFile", read, "internal context", "code")),
            Entry(Call("clock", "clock-call", "CurrentTime", clock, new { }, "code")),
            Entry(Result("clock-result", "clock-call", "CurrentTime", clock, "2026-11-14T00:00:00Z", "code")),
            Entry(Call("shell", "shell-call", "Exec", shell, new { command = "Get-Content notes.txt" }, "code")),
            Entry(Item("command", "commandExecution", new
            {
                callId = "shell-call", command = "Get-Content notes.txt", status = "completed", exitCode = 0,
                aggregatedOutput = "actual command output"
            }, "code")),
            Entry(Result("shell-result", "shell-call", "Exec", shell, "actual command output", "code")),
            Entry(Call("plugin", "plugin-call", "ReadFile", foreign, new { path = "plugin.txt" }, "code")),
            Entry(Result("plugin-result", "plugin-call", "ReadFile", foreign, "independent plugin result", "code")),
            Entry(Result("wrapper-result", "code", "CodeMode", wrapper, "printed context", presentation: "core.code-mode"))
        ];
        var page = Json(await session.HandleAsync("thread/items/list", Json(new { threadId = thread }), default));
        var items = page.GetProperty("data").EnumerateArray().Select(row => row.GetProperty("item")).ToArray();
        Assert.Equal(2, items.Length);
        var command = Assert.Single(items, item => item.GetProperty("type").GetString() == "commandExecution");
        Assert.Equal("read", command.GetProperty("commandActions")[0].GetProperty("type").GetString());
        Assert.Equal("Get-Content notes.txt", command.GetProperty("commandActions")[0].GetProperty("command").GetString());
        var plugin = Assert.Single(items, item => item.GetProperty("type").GetString() == "dynamicToolCall");
        Assert.Equal("ReadFile", plugin.GetProperty("tool").GetString());
        Assert.Equal("independent plugin result", plugin.GetProperty("contentItems")[0].GetProperty("text").GetString());
        Assert.DoesNotContain("internal context", page.GetRawText());
    }

    [Fact]
    public async Task HiddenCodeModeFailureIsPromotedOnceAndNestedMediaRemainsReadable()
    {
        using var fixture = new Fixture();
        var notifications = Channel.CreateUnbounded<JsonElement>();
        await using var session = await fixture.ConnectAsync(message => notifications.Writer.TryWrite(message));
        var thread = await ThreadAsync(session);
        await session.HandleAsync("thread/resume", Json(new { threadId = thread, excludeTurns = true }), default);
        var wrapper = Source("code-mode", "CodeMode");
        var invocation = Call("wrapper", "code", "CodeMode", wrapper, new { code = "throw Error()" }, presentation: "core.code-mode");
        var failure = Item("failure", "toolResult", new
        {
            callId = "code", toolName = "CodeMode", source = wrapper,
            presentation = new { presentationId = "core.code-mode" }, result = "script failed", success = true,
            structuredContent = new { status = "failed", error = "script failed" }
        });
        fixture.Transport.Notify("item/started", new { threadId = "thread-source", turnId = "turn_001", item = invocation });
        fixture.Transport.Notify("item/completed", new { threadId = "thread-source", turnId = "turn_001", item = failure });
        fixture.Transport.Notify("turn/completed", new { turn = Fixture.Turn() });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = new List<JsonElement>();
        do { received.Add(await notifications.Reader.ReadAsync(deadline.Token)); }
        while (received[^1].GetProperty("method").GetString() != "turn/completed");
        var started = Assert.Single(received, message => message.GetProperty("method").GetString() == "item/started");
        var completed = Assert.Single(received, message => message.GetProperty("method").GetString() == "item/completed");
        var errorItem = completed.GetProperty("params").GetProperty("item");
        Assert.Equal(started.GetProperty("params").GetProperty("item").GetProperty("id").GetString(), errorItem.GetProperty("id").GetString());
        Assert.Equal("failed", errorItem.GetProperty("status").GetString());
        Assert.False(errorItem.GetProperty("success").GetBoolean());

        var read = Source("core-native", "ReadFile");
        fixture.Transport.ItemsOverride =
        [
            Entry(Call("image-call", "image", "ReadFile", read, new { path = "image.png" }, "code")),
            Entry(Item("image-result", "toolResult", new
            {
                callId = "image", toolName = "ReadFile", source = read, success = true,
                contentItems = new[] { new { type = "image", dataBase64 = "AA==", mediaType = "image/png" } }
            }, "code"))
        ];
        var page = Json(await session.HandleAsync("thread/items/list", Json(new { threadId = thread }), default));
        var image = Assert.Single(page.GetProperty("data").EnumerateArray()).GetProperty("item");
        Assert.Equal("inputImage", image.GetProperty("contentItems")[0].GetProperty("type").GetString());
        var detail = Json(await session.HandleAsync("thread/items/read", Json(new
        {
            threadId = thread, turnId = page.GetProperty("data")[0].GetProperty("turnId").GetString(),
            itemIds = new[] { image.GetProperty("id").GetString() }
        }), default));
        Assert.Equal(image.GetRawText(), detail.GetProperty("data")[0].GetProperty("item").GetRawText());
    }

    private static object Source(string id, string tool, string kind = "CoreNative") =>
        new { kind, sourceId = id, sourceToolId = tool };
    private static object Call(string id, string callId, string name, object source, object arguments,
        string? parent = null, string? presentation = null) => Item(id, "toolCall", new
        {
            callId, toolName = name, source, arguments,
            presentation = presentation is null ? null : new { presentationId = presentation }
        }, parent);
    private static object Result(string id, string callId, string name, object source, string result,
        string? parent = null, string? presentation = null) => Item(id, "toolResult", new
        {
            callId, toolName = name, source, result, success = true,
            presentation = presentation is null ? null : new { presentationId = presentation }
        }, parent);
    private static object Item(string id, string type, object payload, string? parent = null) => new
    {
        id, turnId = "turn_001", type, status = "completed",
        createdAt = "2026-11-14T00:00:00Z", completedAt = "2026-11-14T00:00:01Z",
        invocationOrigin = parent is null ? null : "codeMode", invocationSourceId = parent, payload
    };
    private static object Entry(object item) => new { turnId = "turn_001", item };
    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value);
    private static async Task<string> ThreadAsync(DotCraft.CodexGateway.Workspace.CodexWorkspaceSession session) =>
        Json(await session.HandleAsync("thread/list", Json(new { }), default))
            .GetProperty("data")[0].GetProperty("id").GetString()!;
}
