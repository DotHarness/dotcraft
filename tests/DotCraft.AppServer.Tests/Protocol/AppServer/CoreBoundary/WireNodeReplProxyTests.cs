using System.Text.Json;
using DotCraft.Tracing;
using DotCraft.AppServer;
using DotCraft.Security;
using DotCraft.Security.ShellCommands;
using DotCraft.Sessions.Wire;
using DotCraft.Tools;
using Xunit;

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

            if (method == DotCraft.Protocol.AppServer.AppServerMethodNames.ExtNodeReplCancel)
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
                new ClientConnectionInfo { Name = "desktop", Version = "1" },
                new ClientConnectionCapabilities
                {
                    NodeRepl = new NodeReplClientCapability { Backend = "desktop-node" },
                    BrowserUse = new BrowserUseClientCapability { Backend = "desktop-iab", ProtocolVersion = 2 }
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
    public async Task ForkThreadBinding_UsesChildIdentityAndDisconnectsWithParentTransport()
    {
        var prev = TracingChatClient.CurrentSessionKey;
        try
        {
            var proxy = new WireNodeReplProxy();
            var transport = new StubTransport();
            var connection = new AppServerConnection();
            Assert.True(connection.TryMarkInitialized(
                new ClientConnectionInfo { Name = "desktop", Version = "1" },
                new ClientConnectionCapabilities
                {
                    NodeRepl = new NodeReplClientCapability { Backend = "desktop-node" },
                    BrowserUse = new BrowserUseClientCapability { Backend = "desktop-iab", ProtocolVersion = 2 }
                }));

            proxy.BindThread("thread-parent", transport, connection);
            Assert.True(((IThreadForkToolBindingSource)proxy).TryForkThreadBinding(
                "thread-parent",
                "thread-child"));

            TracingChatClient.CurrentSessionKey = "thread-child";
            Assert.True(proxy.IsAvailable);
            await proxy.EvaluateAsync(
                "1 + 1",
                metadata: new NodeReplEvaluationMetadata
                {
                    ThreadId = "thread-child",
                    SessionId = "thread-child",
                    TurnId = "turn-child",
                    ProtocolVersion = 1
                });

            var request = Assert.IsType<JsonElement>(transport.LastParams);
            Assert.Equal("thread-child", request.GetProperty("threadId").GetString());
            Assert.Equal("thread-child", request.GetProperty("browserSession").GetProperty("threadId").GetString());

            proxy.UnbindTransport(transport);
            Assert.False(proxy.IsAvailable);
            TracingChatClient.CurrentSessionKey = "thread-parent";
            Assert.False(proxy.IsAvailable);
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = prev;
        }
    }

    [Fact]
    public void ForkThreadBinding_WithoutLiveParent_DoesNotCreateChildBinding()
    {
        var proxy = new WireNodeReplProxy();

        Assert.False(((IThreadForkToolBindingSource)proxy).TryForkThreadBinding(
            "thread-missing",
            "thread-child"));
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
                new ClientConnectionInfo { Name = "desktop", Version = "1" },
                new ClientConnectionCapabilities
                {
                    NodeRepl = new NodeReplClientCapability { Backend = "desktop-node" },
                    BrowserUse = new BrowserUseClientCapability { Backend = "desktop-iab", ProtocolVersion = 2 }
                });
            proxy.BindThread("thread-b", transport, connection);
            TracingChatClient.CurrentSessionKey = "thread-b";

            var result = await proxy.EvaluateAsync(
                "1 + 1",
                5,
                metadata: new DotCraft.Tools.NodeReplEvaluationMetadata
                {
                    ThreadId = "thread-b",
                    SessionId = "thread-b",
                    TurnId = "turn-b",
                    ProtocolVersion = 1
                });

            Assert.NotNull(result);
            Assert.Equal("ok", result!.ResultText);
            Assert.Equal(DotCraft.Protocol.AppServer.AppServerMethodNames.ExtNodeReplEvaluate, transport.LastMethod);
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
    public async Task RequestApprovalAsync_UsesTurnApprovalServiceOfInFlightEvaluation()
    {
        var prev = TracingChatClient.CurrentSessionKey;
        try
        {
            var proxy = new WireNodeReplProxy();
            var transport = new StubTransport { BlockEvaluate = true };
            var connection = new AppServerConnection();
            connection.TryMarkInitialized(
                new ClientConnectionInfo { Name = "desktop", Version = "1" },
                new ClientConnectionCapabilities
                {
                    NodeRepl = new NodeReplClientCapability { Backend = "desktop-node" },
                    ComputerUse = new ComputerUseClientCapability { Backend = "cua-driver" }
                });
            proxy.BindThread("thread-d", transport, connection);
            TracingChatClient.CurrentSessionKey = "thread-d";
            Assert.True(proxy.IsAvailable);
            Assert.True(proxy.IsComputerUseAvailable);
            Assert.False(proxy.IsBrowserUseAvailable);

            var approvals = new RecordingApprovalService(approve: true);
            using var cts = new CancellationTokenSource();
            Task<NodeReplEvaluation?> pending;
            using (ToolHostExecutionScope.Set(new ToolHostExecutionContext("thread-d", "turn-d", Path.GetTempPath(), approvals, null!)))
                pending = proxy.EvaluateAsync("await dotcraft.computer.list_apps()", 30, cts.Token);

            var evaluationId = transport.Calls
                .Single(call => call.Method == DotCraft.Protocol.AppServer.AppServerMethodNames.ExtNodeReplEvaluate)
                .Params.GetProperty("evaluationId").GetString()!;
            var request = new ResourceApprovalRequest("computerUse", "use", @"C:\Apps\Notepad.exe")
            {
                TargetLabel = "Notepad",
                PersistAcceptAlways = false
            };

            Assert.True(await proxy.RequestApprovalAsync(connection, "thread-d", evaluationId, request));
            Assert.Same(request, approvals.LastRequest);
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                proxy.RequestApprovalAsync(connection, "thread-other", evaluationId, request));
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                proxy.RequestApprovalAsync(new AppServerConnection(), "thread-d", evaluationId, request));

            cts.Cancel();
            await pending;
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                proxy.RequestApprovalAsync(connection, "thread-d", evaluationId, request));
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = prev;
        }
    }

    [Fact]
    public async Task PausableDeadline_DoesNotExpireWhilePaused()
    {
        using var deadline = new PausableDeadline(TimeSpan.FromMilliseconds(150), CancellationToken.None);
        deadline.Pause();
        await Task.Delay(400);
        Assert.False(deadline.Token.IsCancellationRequested);

        deadline.Resume();
        await Task.Delay(600);
        Assert.True(deadline.Token.IsCancellationRequested);
    }

    private sealed class RecordingApprovalService(bool approve) : IApprovalService
    {
        public ResourceApprovalRequest? LastRequest { get; private set; }

        public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null) =>
            Task.FromResult(false);

        public Task<bool> RequestShellApprovalAsync(ShellApprovalRequest request, ApprovalContext? context = null) =>
            Task.FromResult(false);

        public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null) =>
            Task.FromResult(false);

        public Task<bool> RequestResourceApprovalAsync(ResourceApprovalRequest request, ApprovalContext? context = null)
        {
            LastRequest = request;
            return Task.FromResult(approve);
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
                new ClientConnectionInfo { Name = "desktop", Version = "1" },
                new ClientConnectionCapabilities
                {
                    NodeRepl = new NodeReplClientCapability { Backend = "desktop-node" },
                    BrowserUse = new BrowserUseClientCapability { Backend = "desktop-iab", ProtocolVersion = 2 }
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
            var evaluate = transport.Calls.Single(call => call.Method == DotCraft.Protocol.AppServer.AppServerMethodNames.ExtNodeReplEvaluate);
            var cancel = transport.Calls.Single(call => call.Method == DotCraft.Protocol.AppServer.AppServerMethodNames.ExtNodeReplCancel);
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
