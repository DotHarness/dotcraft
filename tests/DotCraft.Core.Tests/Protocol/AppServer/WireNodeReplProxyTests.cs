using System.Text.Json;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Tracing;

namespace DotCraft.Core.Tests.Protocol.AppServer;

public sealed class WireNodeReplProxyTests
{
    private sealed class StubTransport : IAppServerTransport
    {
        private readonly TaskCompletionSource<object?> _cancelSeen = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? LastMethod { get; private set; }
        public JsonElement? LastParams { get; private set; }
        public List<(string Method, JsonElement Params)> Calls { get; } = [];
        public bool BlockEvaluate { get; set; }
        public Task CancelSeen => _cancelSeen.Task;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default) =>
            Task.FromResult<AppServerIncomingMessage?>(null);

        public Task WriteMessageAsync(object message, CancellationToken ct = default) => Task.CompletedTask;

        public async Task<AppServerIncomingMessage> SendClientRequestAsync(
            string method,
            object? @params,
            CancellationToken ct = default,
            TimeSpan? timeout = null)
        {
            LastMethod = method;
            LastParams = JsonSerializer.SerializeToElement(@params, SessionWireJsonOptions.Default);
            Calls.Add((method, LastParams.Value));

            if (method == AppServerMethods.ExtNodeReplCancel)
            {
                _cancelSeen.TrySetResult(null);
                var cancelResponse = JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    result = new { ok = true }
                }, SessionWireJsonOptions.Default);
                return JsonSerializer.Deserialize<AppServerIncomingMessage>(cancelResponse, SessionWireJsonOptions.Default)!;
            }

            if (BlockEvaluate)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }

            var response = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                result = new
                {
                    resultText = "ok",
                    images = new[]
                    {
                        new { mediaType = "image/png", dataBase64 = Convert.ToBase64String([1, 2, 3]) }
                    },
                    logs = new[] { "log" }
                }
            }, SessionWireJsonOptions.Default);
            return JsonSerializer.Deserialize<AppServerIncomingMessage>(response, SessionWireJsonOptions.Default)!;
        }
    }

    [Fact]
    public void BindThread_UnbindTransport_ControlsAvailability()
    {
        var prev = TracingChatClient.CurrentSessionKey;
        try
        {
            var proxy = new WireNodeReplProxy();
            var transport = new StubTransport();
            var connection = new AppServerConnection();
            Assert.True(connection.TryMarkInitialized(
                new AppServerClientInfo { Name = "desktop", Version = "1" },
                new AppServerClientCapabilities
                {
                    NodeRepl = new NodeReplCapability { Backend = "desktop-node" },
                    BrowserUse = new BrowserUseCapability { Backend = "desktop-iab", ProtocolVersion = 2 }
                }));

            proxy.BindThread("thread-a", transport, connection);
            TracingChatClient.CurrentSessionKey = "thread-a";
            Assert.True(proxy.IsAvailable);

            proxy.UnbindTransport(transport);
            Assert.False(proxy.IsAvailable);
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = prev;
        }
    }

    [Fact]
    public async Task EvaluateAsync_SendsThreadParameters()
    {
        var prev = TracingChatClient.CurrentSessionKey;
        try
        {
            var proxy = new WireNodeReplProxy();
            var transport = new StubTransport();
            var connection = new AppServerConnection();
            connection.TryMarkInitialized(
                new AppServerClientInfo { Name = "desktop", Version = "1" },
                new AppServerClientCapabilities
                {
                    NodeRepl = new NodeReplCapability { Backend = "desktop-node" },
                    BrowserUse = new BrowserUseCapability { Backend = "desktop-iab", ProtocolVersion = 2 }
                });
            proxy.BindThread("thread-b", transport, connection);
            TracingChatClient.CurrentSessionKey = "thread-b";

            var result = await proxy.EvaluateAsync(
                "1 + 1",
                5,
                metadata: new DotCraft.Abstractions.NodeReplEvaluationMetadata
                {
                    ThreadId = "thread-b",
                    SessionId = "thread-b",
                    TurnId = "turn-b",
                    ProtocolVersion = 1
                });

            Assert.NotNull(result);
            Assert.Equal("ok", result!.ResultText);
            Assert.Equal(AppServerMethods.ExtNodeReplEvaluate, transport.LastMethod);
            Assert.True(transport.LastParams.HasValue);
            var p = transport.LastParams.Value;
            Assert.Equal("thread-b", p.GetProperty("threadId").GetString());
            Assert.StartsWith("node-repl-", p.GetProperty("evaluationId").GetString());
            Assert.Equal("turn-b", p.GetProperty("turnId").GetString());
            var browserSession = p.GetProperty("browserSession");
            Assert.Equal(1, browserSession.GetProperty("protocolVersion").GetInt32());
            Assert.Equal("thread-b", browserSession.GetProperty("sessionId").GetString());
            Assert.Equal("thread-b", browserSession.GetProperty("threadId").GetString());
            Assert.Equal("turn-b", browserSession.GetProperty("turnId").GetString());
            Assert.Equal(p.GetProperty("evaluationId").GetString(), browserSession.GetProperty("evaluationId").GetString());
            Assert.Equal("1 + 1", p.GetProperty("code").GetString());
            Assert.Equal(5_000, p.GetProperty("timeoutMs").GetInt32());
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = prev;
        }
    }

    [Fact]
    public async Task EvaluateAsync_CancellationSendsCancelRequest()
    {
        var prev = TracingChatClient.CurrentSessionKey;
        try
        {
            var proxy = new WireNodeReplProxy();
            var transport = new StubTransport { BlockEvaluate = true };
            var connection = new AppServerConnection();
            connection.TryMarkInitialized(
                new AppServerClientInfo { Name = "desktop", Version = "1" },
                new AppServerClientCapabilities
                {
                    NodeRepl = new NodeReplCapability { Backend = "desktop-node" },
                    BrowserUse = new BrowserUseCapability { Backend = "desktop-iab", ProtocolVersion = 2 }
                });
            proxy.BindThread("thread-c", transport, connection);
            TracingChatClient.CurrentSessionKey = "thread-c";

            using var cts = new CancellationTokenSource();
            var pending = proxy.EvaluateAsync("await new Promise(() => {})", 120, cts.Token);
            cts.Cancel();

            var result = await pending;
            await transport.CancelSeen.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.NotNull(result);
            Assert.Contains("cancelled", result!.Error);
            var evaluate = transport.Calls.Single(call => call.Method == AppServerMethods.ExtNodeReplEvaluate);
            var cancel = transport.Calls.Single(call => call.Method == AppServerMethods.ExtNodeReplCancel);
            Assert.Equal("thread-c", cancel.Params.GetProperty("threadId").GetString());
            Assert.Equal(
                evaluate.Params.GetProperty("evaluationId").GetString(),
                cancel.Params.GetProperty("evaluationId").GetString());
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = prev;
        }
    }
}
