using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Mcp;
using DotCraft.McpTestServer;
using DotCraft.Security;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class McpElicitationIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DispatcherThroughSdkAndAppServer_ReverseAnswersContinueOriginalCalls(bool http)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        await using var server = http ? await McpFixtureProcess.StartHttpAsync() : null;
        await using var peer = new StdioPeer();
        var subscribers = new AppServerThreadSubscribers();
        var connection = new AppServerConnection();
        connection.TryMarkInitialized(new ClientConnectionInfo { Name = "input-client", Version = "1" },
            new ClientConnectionCapabilities { McpElicitation = true });
        connection.MarkClientReady();
        connection.TryAddSubscription("thread-a", new CancellationTokenSource());
        connection.TryAddSubscription("thread-b", new CancellationTokenSource());
        subscribers.Register(peer.Transport, connection);
        await using var manager = new McpClientManager(elicitationHandler: new AppServerMcpElicitationHandler(subscribers));
        await manager.ConnectAsync([new McpServerConfig
        {
            Name = "input-fixture", Transport = http ? "http" : "stdio",
            Url = server?.Endpoint ?? string.Empty, Command = "dotnet",
            Arguments = [McpFixtureProcess.AssemblyPath, "stdio"]
        }], ct);
        await manager.WaitForStartupCompletionAsync(ct);
        var status = Assert.Single(await manager.ListStatusesAsync(ct));
        Assert.True(status.StartupState == "ready", status.LastError);
        var config = new AppConfig();
        config.Tools.DeferredLoading.Strategy = AppConfig.DeferredLoadingStrategy.Off;
        var planning = new ToolPlanningContext("thread-a", null, Path.GetTempPath(), Path.GetTempPath(), "agent", null, null, 1);
        var registrations = await new McpToolSource(manager, config).GetRegistrationsAsync(planning, ct);
        var registration = Assert.Single(registrations);
        var snapshot = new EffectiveToolSnapshotBuilder().Build(registrations, 1);
        var approval = new CommonToolApprovalEvaluator();
        approval.Bind(new AutoApproveApprovalService());
        var dispatcher = new ToolDispatcher(approvalEvaluator: approval);
        var a = dispatcher.DispatchAsync(snapshot, registration.Definition.Name,
            new System.Text.Json.Nodes.JsonObject { ["owner"] = "A" },
            new ToolInvocationRequest("thread-a", "turn-a", "call-a", ToolInvocationAudience.Model), ct).AsTask();
        var b = dispatcher.DispatchAsync(snapshot, registration.Definition.Name,
            new System.Text.Json.Nodes.JsonObject { ["owner"] = "B" },
            new ToolInvocationRequest("thread-b", null, "call-b", ToolInvocationAudience.Host), ct).AsTask();

        using var first = await peer.ReadAsync(ct);
        using var second = await peer.ReadAsync(ct);
        var prompts = new[] { first.RootElement, second.RootElement }.ToDictionary(
            root => root.GetProperty("params").GetProperty("threadId").GetString()!);
        Assert.Equal("turn-a", prompts["thread-a"].GetProperty("params").GetProperty("turnId").GetString());
        var idA = prompts["thread-a"].GetProperty("id").GetInt32();
        var idB = prompts["thread-b"].GetProperty("id").GetInt32();
        await peer.AnswerAsync(idB, "answer-b", ct);
        var resultB = await b.WaitAsync(ct);
        Assert.True(resultB.Success, resultB.Error?.Message);
        Assert.Equal("B", resultB.StructuredContent!.Value.GetProperty("owner").GetString());
        Assert.Equal("answer-b", resultB.StructuredContent.Value.GetProperty("answers")[0].GetString());
        Assert.False(a.IsCompleted);
        await peer.AssertResolvedAsync(idB, ct);
        await peer.AnswerAsync(idA, "answer-a", ct);
        var resultA = await a.WaitAsync(ct);
        Assert.True(resultA.Success, resultA.Error?.Message);
        Assert.Equal("A", resultA.StructuredContent!.Value.GetProperty("owner").GetString());
        Assert.Equal("answer-a", resultA.StructuredContent.Value.GetProperty("answers")[0].GetString());
        await peer.AssertResolvedAsync(idA, ct);
        connection.MarkClosed();
    }

    private sealed class StdioPeer : IAsyncDisposable
    {
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        public StdioTransport Transport { get; }

        public StdioPeer()
        {
            var incoming = new Pipe();
            var outgoing = new Pipe();
            _writer = new StreamWriter(incoming.Writer.AsStream(), new UTF8Encoding(false)) { AutoFlush = true };
            _reader = new StreamReader(outgoing.Reader.AsStream());
            Transport = StdioTransport.Create(incoming.Reader.AsStream(), outgoing.Writer.AsStream());
            Transport.Start();
        }

        public async Task<JsonDocument> ReadAsync(CancellationToken ct) =>
            JsonDocument.Parse((await _reader.ReadLineAsync(ct))!);

        public Task AnswerAsync(int id, string answer, CancellationToken ct) =>
            _writer.WriteLineAsync(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0", id, result = new { action = "accept", content = new { answer } }
            }).AsMemory(), ct);

        public async Task AssertResolvedAsync(int id, CancellationToken ct)
        {
            using var resolved = await ReadAsync(ct);
            Assert.Equal("serverRequest/resolved", resolved.RootElement.GetProperty("method").GetString());
            Assert.Equal(id, resolved.RootElement.GetProperty("params").GetProperty("requestId").GetInt32());
        }

        public async ValueTask DisposeAsync()
        {
            await Transport.DisposeAsync();
            _reader.Dispose();
            await _writer.DisposeAsync();
        }
    }
}
