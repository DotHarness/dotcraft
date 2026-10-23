using DotCraft.Configuration;
using DotCraft.Modules;
using DotCraft.AppServer;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer.Integration;

public sealed class WireClientWebSocketIntegrationTests : IAsyncDisposable
{
    private readonly string _tempDir;
    private readonly TestableSessionService _service;
    private readonly Task _serverLoop;
    private readonly CancellationTokenSource _serverCts = new();
    private readonly WebSocketClientConnection _connection;

    public WireClientWebSocketIntegrationTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "WsIntegrationTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        var store = new ThreadStore(_tempDir);
        _service = new TestableSessionService(store);

        var (serverWs, clientWs) = WebSocketTestHelper.CreateWebSocketPair();

        _serverLoop = Task.Run(async () =>
        {
            await using var wsTransport = new WebSocketTransport(serverWs);
            wsTransport.Start();
            var connection = new AppServerConnection();
            var handler = new AppServerRequestHandler(
                _service, connection, wsTransport,
                new ModuleRegistryChannelListContributor(new ModuleRegistry()),
                new AppServerConnectionServices
                {
                    ServerVersion = "0.0.1-test",
                    HostWorkspacePath = _tempDir,
                    WorkspaceCraftPath = Path.Combine(_tempDir, ".agents"),
                    AppConfigMonitor = new AppConfigMonitor(AppConfigTestFactory.CreateOpenAI()),
                });
            await RunServerLoopAsync(wsTransport, connection, handler, _serverCts.Token);
        });

        _connection = WebSocketClientConnection.FromWebSocket(clientWs);
    }

    [Fact]
    public async Task WireClient_OverWebSocket_HandshakesStartsAThreadAndStreamsATurn()
    {
        var initDoc = await _connection.Wire.InitializeAsync(clientName: "test-cli", clientVersion: "0.0.1");
        var init = initDoc.RootElement.GetProperty("result");
        Assert.Equal("dotcraft", init.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Equal("1", init.GetProperty("serverInfo").GetProperty("protocolVersion").GetString());
        Assert.True(init.GetProperty("capabilities").GetProperty("threadManagement").GetBoolean());

        var createResult = await _connection.Wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "cli", userId = "local", workspacePath = _tempDir },
            historyMode = "server"
        });
        var threadId = createResult.RootElement.GetProperty("result")
            .GetProperty("thread").GetProperty("id").GetString()!;
        Assert.StartsWith("thread_", threadId);

        _service.EnqueueSubmitEvents(threadId, AppServerTestHarness.BuildTurnEventSequence(threadId));

        var turnResult = await _connection.Wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId,
            input = new[] { new { type = "text", text = "Hello" } }
        });

        var turnId = turnResult.RootElement.GetProperty("result")
            .GetProperty("turn").GetProperty("id").GetString();
        Assert.NotNull(turnId);

        var notifications = new List<string>();
        await foreach (var notif in _connection.Wire.ReadTurnNotificationsAsync(ct: CancellationToken.None))
        {
            if (notif.RootElement.TryGetProperty("method", out var m))
                notifications.Add(m.GetString() ?? string.Empty);
        }

        Assert.Contains(DotCraft.Protocol.AppServer.AppServerMethodNames.AgentMessageDelta, notifications);
        Assert.Equal(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCompleted, notifications[^1]);
    }

    [Fact]
    public async Task TurnStart_WithApproval_ClientReceivesApprovalRequest()
    {
        await _connection.Wire.InitializeAsync(approvalSupport: true);

        var createResult = await _connection.Wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "cli", userId = "local", workspacePath = _tempDir },
            historyMode = "server"
        });
        var threadId = createResult.RootElement.GetProperty("result")
            .GetProperty("thread").GetProperty("id").GetString()!;

        _service.EnqueueSubmitEvents(threadId, AppServerTestHarness.BuildApprovalEventSequence(threadId));

        var approvalRequest = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _connection.Wire.ServerRequestHandler = async doc =>
        {
            approvalRequest.TrySetResult(doc.RootElement.TryGetProperty("method", out var m) ? m.GetString() : null);
            return new { decision = "accept" };
        };

        await _connection.Wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId,
            input = new[] { new { type = "text", text = "shell command please" } }
        });

        var methods = new List<string>();
        await foreach (var notif in _connection.Wire.ReadTurnNotificationsAsync(ct: CancellationToken.None))
        {
            if (notif.RootElement.TryGetProperty("method", out var m))
                methods.Add(m.GetString() ?? string.Empty);
        }

        Assert.Equal(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ApprovalRequest,
            await approvalRequest.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCompleted, methods[^1]);
    }

    [Fact]
    public async Task MultipleConcurrentClients_EachGetIsolatedConnection_ShareSessionService()
    {
        var (serverWs2, clientWs2) = WebSocketTestHelper.CreateWebSocketPair();
        var serverLoop2Cts = new CancellationTokenSource();

        var serverLoop2 = Task.Run(async () =>
        {
            await using var ws2Transport = new WebSocketTransport(serverWs2);
            ws2Transport.Start();
            var conn2 = new AppServerConnection();
            var handler2 = new AppServerRequestHandler(
                _service, conn2, ws2Transport,
                new ModuleRegistryChannelListContributor(new ModuleRegistry()),
                new AppServerConnectionServices
                {
                    ServerVersion = "0.0.1-test",
                    HostWorkspacePath = _tempDir,
                    WorkspaceCraftPath = Path.Combine(_tempDir, ".agents"),
                    AppConfigMonitor = new AppConfigMonitor(AppConfigTestFactory.CreateOpenAI()),
                });
            await RunServerLoopAsync(ws2Transport, conn2, handler2, serverLoop2Cts.Token);
        });

        await using var connection2 = WebSocketClientConnection.FromWebSocket(clientWs2);

        try
        {
            await _connection.Wire.InitializeAsync(clientName: "client-1");
            await connection2.Wire.InitializeAsync(clientName: "client-2");

            var result1 = await _connection.Wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
            {
                identity = new { channelName = "cli", userId = "user1", workspacePath = _tempDir },
                historyMode = "server"
            });
            var threadId1 = result1.RootElement.GetProperty("result")
                .GetProperty("thread").GetProperty("id").GetString()!;

            var listResult = await connection2.Wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadList, new
            {
                identity = new { channelName = "cli", userId = "user1", workspacePath = _tempDir }
            });

            var threads = listResult.RootElement.GetProperty("result")
                .GetProperty("data").EnumerateArray()
                .Select(el => el.GetProperty("id").GetString())
                .ToList();

            Assert.Contains(threadId1, threads);
        }
        finally
        {
            await serverLoop2Cts.CancelAsync();
            try { await serverLoop2; } catch { }
            serverLoop2Cts.Dispose();
        }
    }

    private static async Task RunServerLoopAsync(
        IAppServerTransport transport,
        AppServerConnection connection,
        AppServerRequestHandler handler,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            AppServerIncomingMessage? msg;
            try { msg = await transport.ReadMessageAsync(ct); }
            catch (OperationCanceledException) { break; }

            if (msg == null) break;

            if (msg.IsNotification)
            {
                if (msg.Method == DotCraft.Protocol.AppServer.AppServerMethodNames.Initialized)
                    handler.HandleInitializedNotification();
                continue;
            }

            if (!msg.IsRequest) continue;

            _ = Task.Run(async () =>
            {
                object? result;
                try
                {
                    result = await handler.HandleRequestAsync(msg, ct);
                }
                catch (AppServerException ex)
                {
                    await transport.WriteMessageAsync(
                        AppServerRequestHandler.BuildErrorResponse(msg.Id, ex.ToError()), ct);
                    return;
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    var err = AppServerErrors.InternalError(ex.Message).ToError();
                    await transport.WriteMessageAsync(
                        AppServerRequestHandler.BuildErrorResponse(msg.Id, err), ct);
                    return;
                }

                if (result != null)
                    await transport.WriteMessageAsync(
                        AppServerRequestHandler.BuildResponse(msg.Id, result), ct);
            }, ct);
        }

        connection.CancelAllSubscriptions();
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();

        await _serverCts.CancelAsync();
        try { await _serverLoop; } catch { }
        _serverCts.Dispose();

        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
