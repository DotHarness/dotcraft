using DotCraft.Configuration;
using DotCraft.Modules;
using DotCraft.AppServer;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer.Integration;

/// <summary>
/// End-to-end integration tests for the AppServer wire protocol stack over WebSocket.
/// Mirrors <see cref="WireClientIntegrationTests"/> but uses <see cref="WebSocketTransport"/>
/// and <see cref="WebSocketClientConnection"/> in place of the pipe-based stdio transport.
///
/// A fake WebSocket pair is created in-process via <see cref="WebSocketTestHelper.CreateWebSocketPair"/>
/// — no actual network sockets are opened.
///
/// Test coverage:
/// <list type="bullet">
/// <item>Handshake (initialize → initialized) over WebSocket</item>
/// <item>Thread creation: thread/start returns a valid thread ID</item>
/// <item>Thread discovery: thread/list returns the created thread</item>
/// <item>Turn execution: turn/start streams agent notifications and completes</item>
/// <item>Approval flow: server sends item/approval/request, client responds</item>
/// <item>Multiple concurrent connections share the same session service</item>
/// </list>
/// </summary>
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

        // The server loop task owns the server transport lifetime
        _serverLoop = Task.Run(async () =>
        {
            await using var wsTransport = new WebSocketTransport(serverWs);
            wsTransport.Start();
            var connection = new AppServerConnection();
            var handler = new AppServerRequestHandler(
                _service, connection, wsTransport,
                new ModuleRegistryChannelListContributor(new ModuleRegistry(), null, null),
                new AppServerConnectionServices
                {
                    ServerVersion = "0.0.1-test",
                    HostWorkspacePath = _tempDir,
                    AppConfigMonitor = new AppConfigMonitor(AppConfigTestFactory.CreateOpenAI()),
                });
            await RunServerLoopAsync(wsTransport, connection, handler, _serverCts.Token);
        });

        // Client side: wrap the client WebSocket in a full wire client connection
        _connection = WebSocketClientConnection.FromWebSocket(clientWs);
    }

    // -------------------------------------------------------------------------
    // Handshake
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Initialize_OverWebSocket_ReturnsServerInfo()
    {
        var initDoc = await _connection.Wire.InitializeAsync(clientName: "test-cli", clientVersion: "0.0.1");

        var result = initDoc.RootElement.GetProperty("result");
        Assert.Equal("dotcraft", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Equal("1", result.GetProperty("serverInfo").GetProperty("protocolVersion").GetString());
        Assert.True(result.GetProperty("capabilities").GetProperty("threadManagement").GetBoolean());
    }

    // -------------------------------------------------------------------------
    // Thread creation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadStart_OverWebSocket_ReturnsThreadId()
    {
        await _connection.Wire.InitializeAsync();

        var result = await _connection.Wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new
            {
                channelName = "cli",
                userId = "local",
                workspacePath = _tempDir
            },
            historyMode = "server"
        });

        var thread = result.RootElement.GetProperty("result").GetProperty("thread");
        var threadId = thread.GetProperty("id").GetString();
        Assert.NotNull(threadId);
        Assert.StartsWith("thread_", threadId);
    }

    // -------------------------------------------------------------------------
    // Turn execution (streaming notifications)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TurnStart_WithCannedEvents_StreamsAgentResponse()
    {
        await _connection.Wire.InitializeAsync();

        var createResult = await _connection.Wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "cli", userId = "local", workspacePath = _tempDir },
            historyMode = "server"
        });
        var threadId = createResult.RootElement.GetProperty("result")
            .GetProperty("thread").GetProperty("id").GetString()!;

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

    // -------------------------------------------------------------------------
    // Approval flow (bidirectional server request over WebSocket)
    // -------------------------------------------------------------------------

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

        var approvalRequests = new List<string>();
        _connection.Wire.ServerRequestHandler = async doc =>
        {
            var method = doc.RootElement.TryGetProperty("method", out var m) ? m.GetString() : null;
            approvalRequests.Add(method ?? "unknown");
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

        Assert.Contains(DotCraft.Protocol.AppServer.AppServerMethodNames.ApprovalRequest, approvalRequests);
        Assert.Equal(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCompleted, methods[^1]);
    }

    // -------------------------------------------------------------------------
    // WebSocket-specific: multiple concurrent clients share the same session service
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MultipleConcurrentClients_EachGetIsolatedConnection_ShareSessionService()
    {
        // Create a second independent WebSocket connection to the SAME session service
        var (serverWs2, clientWs2) = WebSocketTestHelper.CreateWebSocketPair();
        var serverLoop2Cts = new CancellationTokenSource();

        var serverLoop2 = Task.Run(async () =>
        {
            await using var ws2Transport = new WebSocketTransport(serverWs2);
            ws2Transport.Start();
            var conn2 = new AppServerConnection();
            var handler2 = new AppServerRequestHandler(
                _service, conn2, ws2Transport,
                new ModuleRegistryChannelListContributor(new ModuleRegistry(), null, null),
                new AppServerConnectionServices
                {
                    ServerVersion = "0.0.1-test",
                    HostWorkspacePath = _tempDir,
                    AppConfigMonitor = new AppConfigMonitor(AppConfigTestFactory.CreateOpenAI()),
                });
            await RunServerLoopAsync(ws2Transport, conn2, handler2, serverLoop2Cts.Token);
        });

        await using var connection2 = WebSocketClientConnection.FromWebSocket(clientWs2);

        try
        {
            // Both clients initialize independently
            await _connection.Wire.InitializeAsync(clientName: "client-1");
            await connection2.Wire.InitializeAsync(clientName: "client-2");

            // Client 1 creates a thread
            var result1 = await _connection.Wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
            {
                identity = new { channelName = "cli", userId = "user1", workspacePath = _tempDir },
                historyMode = "server"
            });
            var threadId1 = result1.RootElement.GetProperty("result")
                .GetProperty("thread").GetProperty("id").GetString()!;

            // Client 2 lists threads — should see the thread created by client 1 (shared service)
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

    // -------------------------------------------------------------------------
    // Server loop (mirrors AppServerHost.RunLoopAsync)
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        // Dispose the wire connection first (sends WebSocket close frame to the server)
        await _connection.DisposeAsync();

        // Cancel and await the server loop
        await _serverCts.CancelAsync();
        try { await _serverLoop; } catch { }
        _serverCts.Dispose();

        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
