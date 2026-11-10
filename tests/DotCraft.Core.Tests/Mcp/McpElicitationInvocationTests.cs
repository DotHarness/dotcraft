using System.Text.Json;
using System.Threading.Channels;
using DotCraft.Mcp;
using DotCraft.McpTestServer;
using DotCraft.Tools;
using ModelContextProtocol.Protocol;
using Xunit;

namespace DotCraft.Tests.Mcp;

public sealed partial class McpElicitationInvocationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentCalls_KeepOwnersWhenAnswersArriveInReverseOrder(bool http)
    {
        await using var server = http ? await McpFixtureProcess.StartHttpAsync() : null;
        var handler = new InputHandler();
        await using var manager = await ConnectAsync(Config(server), handler);
        var a = CallAsync(manager, "A", "turn-A");
        var b = CallAsync(manager, "B", null);
        var pending = new[] { await handler.ReadAsync(), await handler.ReadAsync() }.ToDictionary(input => input.Context.ThreadId);

        Assert.Equal("turn-A", pending["A"].Context.TurnId);
        Assert.Null(pending["B"].Context.TurnId);
        Assert.Equal("call-A", pending["A"].Context.CallId);
        Assert.Equal("A:0", pending["A"].Request.Message);
        Assert.Equal("B:0", pending["B"].Request.Message);
        pending["B"].Answer("answer-B");
        AssertAnswer(await b.WaitAsync(Deadline), "B", "answer-B");
        Assert.False(a.IsCompleted);
        pending["A"].Answer("answer-A");
        AssertAnswer(await a.WaitAsync(Deadline), "A", "answer-A");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingOneCall_EndsOnlyItsInputAndDoesNotReviveWithLateAnswer(bool http)
    {
        await using var server = http ? await McpFixtureProcess.StartHttpAsync() : null;
        var handler = new InputHandler();
        await using var manager = await ConnectAsync(Config(server), handler);
        using var cancellation = new CancellationTokenSource();
        var a = CallAsync(manager, "A", "turn-A", cancellation.Token);
        var b = CallAsync(manager, "B", "turn-B");
        var pending = new[] { await handler.ReadAsync(), await handler.ReadAsync() }.ToDictionary(input => input.Context.ThreadId);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a.WaitAsync(Deadline));
        Assert.True(pending["A"].CancellationToken.IsCancellationRequested);
        Assert.False(pending["B"].CancellationToken.IsCancellationRequested);
        pending["A"].Answer("late");
        pending["B"].Answer("answer-B");
        AssertAnswer(await b.WaitAsync(Deadline), "B", "answer-B");

        var next = CallAsync(manager, "A", "next-turn");
        var nextInput = await handler.ReadAsync();
        Assert.Equal("next-turn", nextInput.Context.TurnId);
        Assert.False(next.IsCompleted);
        nextInput.Answer("next");
        AssertAnswer(await next.WaitAsync(Deadline), "A", "next");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultipleInputs_KeepOneInvocationAndIndependentAnswers(bool http)
    {
        await using var server = http ? await McpFixtureProcess.StartHttpAsync() : null;
        var handler = new InputHandler();
        await using var manager = await ConnectAsync(Config(server), handler);
        var call = manager.CallToolAsync("inputs", "choose", new Dictionary<string, object?> { ["owner"] = "A", ["count"] = 2 },
            context: Context("A", "turn-A"));
        var inputs = new[] { await handler.ReadAsync(), await handler.ReadAsync() }.OrderBy(input => input.Request.Message).ToArray();
        Assert.All(inputs, input => Assert.Equal("call-A", input.Context.CallId));
        inputs[1].Answer("second");
        inputs[0].Answer("first");
        var result = await call.WaitAsync(Deadline);
        var structured = JsonSerializer.SerializeToElement(result.StructuredContent);
        Assert.Equal(new[] { "first", "second" }, structured.GetProperty("answers").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task McpConnectionClosure_CancelsInputWait()
    {
        var handler = new InputHandler();
        await using var manager = await ConnectAsync(Config(null), handler);
        var call = CallAsync(manager, "A", "turn-A");
        var input = await handler.ReadAsync();
        _ = await Assert.ThrowsAnyAsync<Exception>(() => manager.CallToolAsync("inputs", "disconnect").WaitAsync(Deadline));
        var error = await Assert.ThrowsAsync<McpElicitationUnavailableException>(() => call.WaitAsync(Deadline));
        Assert.Equal("disconnected", error.Reason);
        Assert.True(input.CancellationToken.IsCancellationRequested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyReverseRequest_DoesNotInheritActiveInvocation(bool http)
    {
        await using var server = http ? await McpFixtureProcess.StartHttpAsync(legacy: true) : null;
        var handler = new InputHandler(answerImmediately: true);
        var config = Config(server);
        config.Arguments.Add("legacy");
        await using var manager = await ConnectAsync(config, handler);
        var result = await CallAsync(manager, "A", "turn-A").WaitAsync(Deadline);
        var response = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains(ToolErrorCodes.McpElicitationUnavailable, response);
        Assert.Contains("unattributed", response);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task MissingHostHandler_PreservesStableToolFailure()
    {
        await using var manager = await ConnectAsync(Config(null), null);
        var generation = (await manager.GetGenerationAsync("inputs"))!.Value;
        var runtime = new McpToolRuntime(manager, "inputs", "choose", generation);
        var result = await runtime.InvokeAsync(Context("A", "turn-A"), new System.Text.Json.Nodes.JsonObject { ["owner"] = "A" });
        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.McpElicitationUnavailable, result.Error!.Code);
        Assert.Equal("no_client", result.Error.Parameters["reason"].GetString());
    }

    private static McpServerConfig Config(McpFixtureProcess? server) => new()
    {
        Name = "inputs", Transport = server is null ? "stdio" : "http", Url = server?.Endpoint ?? string.Empty,
        Command = "dotnet", Arguments = [McpFixtureProcess.AssemblyPath, "stdio"], StartupTimeoutSec = 15
    };

    private static async Task<McpClientManager> ConnectAsync(McpServerConfig config, IMcpElicitationHandler? handler)
    {
        var manager = new McpClientManager(elicitationHandler: handler);
        await manager.ConnectAsync([config]);
        await manager.WaitForStartupCompletionAsync();
        var status = Assert.Single(await manager.ListStatusesAsync());
        Assert.True(status.StartupState == "ready", status.LastError);
        return manager;
    }

    private static Task<CallToolResult> CallAsync(McpClientManager manager, string owner,
        string? turnId, CancellationToken ct = default) =>
        manager.CallToolAsync("inputs", "choose", new Dictionary<string, object?> { ["owner"] = owner },
            cancellationToken: ct, context: Context(owner, turnId));

    private static ToolInvocationContext Context(string owner, string? turnId) => new(owner, turnId, $"call-{owner}",
        turnId is null ? ToolInvocationAudience.Host : ToolInvocationAudience.Model,
        new ToolName("inputs", "choose"), new ToolDefinitionId(ToolSourceKind.Mcp, "inputs", new SourceToolId("choose")),
        new RuntimeBindingId("binding"), 1, DateTimeOffset.UtcNow);

    private static void AssertAnswer(CallToolResult result, string owner, string answer)
    {
        var structured = JsonSerializer.SerializeToElement(result.StructuredContent);
        Assert.Equal(owner, structured.GetProperty("owner").GetString());
        Assert.Equal(answer, Assert.Single(structured.GetProperty("answers").EnumerateArray()).GetString());
        var capabilities = structured.GetProperty("clientCapabilities");
        var elicitation = capabilities.GetProperty("elicitation");
        Assert.True(elicitation.TryGetProperty("form", out _));
        Assert.True(elicitation.TryGetProperty("url", out _));
        var extension = capabilities.GetProperty("extensions").GetProperty(McpAppMetadataParser.ExtensionIdentifier);
        Assert.Equal(McpAppMetadataParser.HtmlMimeType, Assert.Single(extension.GetProperty("mimeTypes").EnumerateArray()).GetString());
    }

    private sealed class InputHandler(bool answerImmediately = false) : IMcpElicitationHandler
    {
        private readonly Channel<PendingInput> _inputs = Channel.CreateUnbounded<PendingInput>();
        private int _count;
        public int Count => Volatile.Read(ref _count);

        public async ValueTask<ElicitResult> HandleAsync(ToolInvocationContext context, string serverName,
            ElicitRequestParams request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            if (answerImmediately) return new ElicitResult { Action = "decline" };
            var input = new PendingInput(context, serverName, request, cancellationToken);
            _inputs.Writer.TryWrite(input);
            return await input.Response.Task.WaitAsync(cancellationToken);
        }

        public async Task<PendingInput> ReadAsync() => await _inputs.Reader.ReadAsync().AsTask().WaitAsync(Deadline);
    }

    private sealed record PendingInput(ToolInvocationContext Context, string ServerName, ElicitRequestParams Request, CancellationToken CancellationToken)
    {
        public TaskCompletionSource<ElicitResult> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Answer(string answer) => Response.TrySetResult(new ElicitResult
        {
            Action = "accept", Content = new Dictionary<string, JsonElement> { ["answer"] = JsonSerializer.SerializeToElement(answer) }
        });
    }
}
