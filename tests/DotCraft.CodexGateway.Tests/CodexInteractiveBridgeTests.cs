using System.Text.Json;
using System.Threading.Channels;
using DotCraft.CodexGateway.Protocol;
using DotCraft.CodexGateway.Workspace;
using DotCraft.Protocol.AppServer;
using Xunit;

namespace DotCraft.CodexGateway.Tests;

public sealed class CodexInteractiveBridgeTests
{
    [Fact]
    public async Task ReverseResponsesStayConnectionLocalAndResolvedUsesOriginalOutboundId()
    {
        var a = Channel.CreateUnbounded<JsonElement>();
        var b = Channel.CreateUnbounded<JsonElement>();
        await using var first = new CodexRequestBroker((message, ct) => a.Writer.WriteAsync(message, ct).AsTask());
        await using var second = new CodexRequestBroker((message, ct) => b.Writer.WriteAsync(message, ct).AsTask());
        var pendingA = first.RequestAsync("source-a", "item/tool/requestUserInput", new { }, default);
        var pendingB = second.RequestAsync("source-b", "item/tool/requestUserInput", new { }, default);
        var requestA = await Next(a);
        var requestB = await Next(b);
        Assert.Equal(requestA.GetProperty("id").GetString(), requestB.GetProperty("id").GetString());
        Assert.False(first.TryReceive(Json(new { id = requestA.GetProperty("id"), method = "thread/read" })));
        Assert.True(first.TryReceive(Json(new { id = requestA.GetProperty("id"), result = new { answers = new { } } })));
        await pendingA;
        Assert.False(pendingB.IsCompleted);

        await second.ResolveAsync("source-b", "external-thread", default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingB);
        var resolved = await Next(b);
        Assert.Equal("serverRequest/resolved", resolved.GetProperty("method").GetString());
        Assert.Equal(requestB.GetProperty("id").GetString(), resolved.GetProperty("params").GetProperty("requestId").GetString());
        Assert.True(second.TryReceive(Json(new { id = requestB.GetProperty("id"), result = new { decision = "accept" } })));

        using var cancelled = new CancellationTokenSource();
        var waiting = first.RequestAsync("source-c", "item/fileChange/requestApproval", new { }, cancelled.Token);
        var requestC = await Next(a);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        await first.ResolveAsync("source-c", "external-thread", default);
        Assert.Equal(requestC.GetProperty("id").GetString(), (await Next(a)).GetProperty("params").GetProperty("requestId").GetString());
    }

    [Fact]
    public async Task CodingApprovalUsesActualOperationIdentityAndNeverWidensPhoneDecision()
    {
        using var fixture = new BridgeFixture();
        var approval = Approval("shell", "call-exec", "echo safe", fixture.Root, "request-shell");
        var pending = fixture.Bridge.HandleApprovalAsync(approval, default);
        var message = await Next(fixture.Output);
        var p = message.GetProperty("params");
        Assert.Equal("item/commandExecution/requestApproval", message.GetProperty("method").GetString());
        Assert.Equal(fixture.Routes.External("item", "thread", "turn", "command:call-exec"), p.GetProperty("itemId").GetString());
        Assert.Equal("echo safe", p.GetProperty("command").GetString());
        fixture.Reply(message, new { decision = "decline" });
        Assert.Equal("decline", (await pending).Decision);
        await fixture.Bridge.ObserveAsync("item/approval/resolved", Json(new
        {
            threadId = "thread", turnId = "turn",
            item = new { payload = new { requestId = "request-shell" } }
        }), default);
        Assert.Equal(message.GetProperty("id").GetString(), (await Next(fixture.Output)).GetProperty("params").GetProperty("requestId").GetString());

        var wide = fixture.Bridge.HandleApprovalAsync(Approval("file", "call-file", "write", "/outside/file", "request-file"), default);
        var file = await Next(fixture.Output);
        Assert.Equal("item/fileChange/requestApproval", file.GetProperty("method").GetString());
        Assert.Equal(fixture.Routes.External("item", "thread", "turn", "call:call-file"),
            file.GetProperty("params").GetProperty("itemId").GetString());
        fixture.Reply(file, new { decision = "acceptForSession" });
        await Assert.ThrowsAsync<ArgumentException>(() => wide);

        var read = fixture.Bridge.HandleApprovalAsync(Approval("file", "call-read", "read", "/outside/notes", "request-read"), default);
        var permission = await Next(fixture.Output);
        Assert.Equal("item/permissions/requestApproval", permission.GetProperty("method").GetString());
        fixture.Reply(permission, new
        {
            scope = "turn", permissions = new
            {
                fileSystem = new
                {
                    entries = new[] { new { path = new { type = "path", path = "/outside/notes" }, access = "read" } }
                }
            }
        });
        Assert.Equal("accept", (await read).Decision);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Bridge.HandleApprovalAsync(
            Approval("computerUse", null, "use", "desktop", "unsupported"), default));
        Assert.Equal("warning", (await Next(fixture.Output)).GetProperty("method").GetString());
    }

    [Fact]
    public async Task QuestionsPreserveStructureAndOtherClientResolutionAbandonsOnlyTheCallback()
    {
        using var fixture = new BridgeFixture();
        var request = new UserInputRequestParams
        {
            ThreadId = "thread", TurnId = "turn", ItemId = "question-item", RequestId = "question",
            IsBlocking = false,
            Questions = [new UserInputQuestion
            {
                Id = "choice", Header = "Target", Question = "Which target?", IsOther = true, IsSecret = true,
                Options = [new UserInputOption { Label = "Local", Description = "Use the local target" }]
            }]
        };
        var pending = fixture.Bridge.HandleUserInputAsync(request, default);
        var message = await Next(fixture.Output);
        var p = message.GetProperty("params");
        Assert.False(p.GetProperty("isBlocking").GetBoolean());
        var question = p.GetProperty("questions")[0];
        Assert.True(question.GetProperty("isOther").GetBoolean());
        Assert.True(question.GetProperty("isSecret").GetBoolean());
        Assert.Equal("Use the local target", question.GetProperty("options")[0].GetProperty("description").GetString());
        fixture.Reply(message, new { answers = new { choice = new { answers = new[] { "custom" } } } });
        Assert.Equal(["custom"], (await pending).Answers["choice"].Answers);

        var unresolved = new UserInputRequestParams
        {
            ThreadId = request.ThreadId, TurnId = request.TurnId, ItemId = request.ItemId,
            RequestId = "pending-other-client", IsBlocking = true, Questions = request.Questions
        };
        using var disconnected = new CancellationTokenSource();
        var oldCallback = fixture.Bridge.HandleUserInputAsync(unresolved, disconnected.Token);
        await Next(fixture.Output);
        disconnected.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oldCallback);
        var restored = fixture.Bridge.HandleUserInputAsync(unresolved, default);
        var restoredMessage = await Next(fixture.Output);
        Assert.NotEqual(message.GetProperty("id").GetString(), restoredMessage.GetProperty("id").GetString());
        await fixture.Bridge.ObserveAsync("item/tool/requestUserInput/resolved", Json(new
        {
            threadId = "thread", turnId = "turn", item = new { payload = new { requestId = "pending-other-client" } }
        }), default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restored);
        var resolution = await Next(fixture.Output);
        Assert.Equal(restoredMessage.GetProperty("id").GetString(), resolution.GetProperty("params").GetProperty("requestId").GetString());
        fixture.Reply(restoredMessage, new { answers = new { choice = new { answers = new[] { "late" } } } });
    }

    private static ApprovalRequestParams Approval(string type, string? callId, string operation, string target, string requestId) => new()
    {
        ThreadId = "thread", TurnId = "turn", ItemId = "approval-item", RequestId = requestId,
        ApprovalType = type, CallId = callId, Operation = operation, Target = target, ScopeKey = "scope"
    };

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    private static async Task<JsonElement> Next(Channel<JsonElement> channel) =>
        await channel.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    private sealed class BridgeFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "codex-interactions-" + Guid.NewGuid().ToString("N"));
        public Channel<JsonElement> Output { get; } = Channel.CreateUnbounded<JsonElement>();
        public WorkspaceRoutes Routes { get; }
        public CodexRequestBroker Broker { get; }
        public CodexInteractiveBridge Bridge { get; }

        public BridgeFixture()
        {
            Routes = WorkspaceRoutes.Open(Root, Root);
            Task Send(JsonElement message, CancellationToken ct) => Output.Writer.WriteAsync(message, ct).AsTask();
            Broker = new(Send);
            Bridge = new(Routes, Root, Broker, Send);
        }

        public void Reply(JsonElement message, object result) =>
            Assert.True(Broker.TryReceive(Json(new { id = message.GetProperty("id"), result })));

        public void Dispose()
        {
            Broker.DisposeAsync().GetAwaiter().GetResult();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
