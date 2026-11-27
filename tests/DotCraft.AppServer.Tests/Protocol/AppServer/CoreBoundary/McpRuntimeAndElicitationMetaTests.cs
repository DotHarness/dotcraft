using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.AppServer;
using DotCraft.Mcp;
using DotCraft.Tools;
using ModelContextProtocol.Protocol;
using Contract = DotCraft.Protocol.AppServer;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class McpRuntimeAndElicitationMetaTests
{
    [Fact]
    public async Task ElicitationHandler_ForwardsMetaAndFallsBackToParentThreadSubscriberForSubagent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = timeout.Token;
        await using var peer = new StdioPeer();
        var subscribers = new AppServerThreadSubscribers();
        var connection = new AppServerConnection();
        connection.TryMarkInitialized(
            new ClientConnectionInfo { Name = "desktop", Version = "1" },
            new ClientConnectionCapabilities { McpElicitation = true });
        connection.MarkClientReady();
        connection.TryAddSubscription("parent-thread", new CancellationTokenSource());
        subscribers.Register(peer.Transport, connection);

        var handler = new AppServerMcpElicitationHandler(subscribers);
        var context = new ToolInvocationContext(
            ThreadId: "child-thread",
            TurnId: "child-turn",
            CallId: "call-1",
            Audience: ToolInvocationAudience.Model,
            ToolName: new ToolName("NodeReplJs", "node_repl"),
            DefinitionId: new ToolDefinitionId(ToolSourceKind.Mcp, "node_repl", new SourceToolId("NodeReplJs")),
            RuntimeBindingId: new RuntimeBindingId("mcp:node_repl:NodeReplJs:1"),
            SnapshotRevision: 1,
            StartedAt: DateTimeOffset.UtcNow,
            IsSubagent: true,
            ParentThreadId: "parent-thread");

        var request = new ElicitRequestParams
        {
            Mode = "form",
            Message = "Approve computer-use click",
            Meta = new JsonObject
            {
                ["approvalKind"] = "computerUse",
                ["operation"] = "click",
                ["target"] = "Notepad"
            },
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    ["approved"] = new ElicitRequestParams.BooleanSchema()
                },
                Required = ["approved"]
            }
        };

        var elicitTask = handler.HandleAsync(context, "node_repl", request, ct).AsTask();

        using var wireRequest = await peer.ReadAsync(ct);
        Assert.Equal("mcpServer/elicitation/request", wireRequest.RootElement.GetProperty("method").GetString());
        var reqParams = wireRequest.RootElement.GetProperty("params");
        Assert.Equal("child-thread", reqParams.GetProperty("threadId").GetString());
        var wireMeta = reqParams.GetProperty("_meta");
        Assert.Equal("computerUse", wireMeta.GetProperty("approvalKind").GetString());
        Assert.Equal("click", wireMeta.GetProperty("operation").GetString());

        var id = wireRequest.RootElement.GetProperty("id").GetInt32();
        await peer.WriteLineAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            result = new
            {
                action = "accept",
                content = new { approved = true },
                _meta = new { scope = "session" }
            }
        }), ct);

        var result = await elicitTask.WaitAsync(ct);
        Assert.Equal("accept", result.Action);
        Assert.NotNull(result.Meta);
        Assert.Equal("session", result.Meta!["scope"]?.GetValue<string>());
        connection.MarkClosed();
    }

    [Fact]
    public void McpContractMapper_OmitsSensitiveHeadersForHostRuntimeServers()
    {
        var registry = new RuntimeMcpRegistry();
        var server = registry.Register("conn-1", new McpServerConfig
        {
            Name = "node_repl",
            Transport = "streamableHttp",
            Url = "http://127.0.0.1:45123/mcp",
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer top-secret" }
        });

        var wire = McpContractMapper.ToContract(server);

        Assert.Equal("node_repl", wire.Name.Value);
        Assert.Equal("hostRuntime", wire.Origin.Value?.Kind.Value);
        Assert.Null(wire.HttpHeaders.Value);
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

        public Task WriteLineAsync(string line, CancellationToken ct) =>
            _writer.WriteLineAsync(line.AsMemory(), ct);

        public async ValueTask DisposeAsync()
        {
            await Transport.DisposeAsync();
            _reader.Dispose();
            await _writer.DisposeAsync();
        }
    }
}
