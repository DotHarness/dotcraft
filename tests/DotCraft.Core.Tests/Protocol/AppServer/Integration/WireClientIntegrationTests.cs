using System.IO.Pipelines;
using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Modules;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer.Integration;

/// <summary>
/// End-to-end integration tests for the AppServer wire protocol stack.
/// Each test spins up a full AppServer server loop (via <see cref="StdioTransport"/> over in-memory
/// pipes) against a <see cref="TestableSessionService"/>, and then exercises the client side
/// using <see cref="AppServerWireClient"/> - the component used by out-of-process clients.
///
/// Test coverage:
/// <list type="bullet">
/// <item>Handshake (initialize → initialized) round-trip over pipes</item>
/// <item>Thread creation: <c>thread/start</c> returns a valid thread ID</item>
/// <item>Thread discovery: <c>thread/list</c> returns the created thread</item>
/// <item>Turn execution: <c>turn/start</c> streams agent notifications and completes</item>
/// <item>Approval flow: server sends <c>item/approval/request</c>, client responds</item>
/// </list>
/// </summary>
public sealed class WireClientIntegrationTests : IAsyncDisposable
{
    // -------------------------------------------------------------------------
    // Pipe-based server setup
    // -------------------------------------------------------------------------

    private readonly string _tempDir;
    private readonly TestableSessionService _service;

    // Pipe pair: client sends → server reads
    private readonly Pipe _clientToServer = new();
    // Pipe pair: server sends → client reads
    private readonly Pipe _serverToClient = new();

    private readonly Task _serverLoop;
    private readonly CancellationTokenSource _serverCts = new();
    private readonly AppServerWireClient _wire;

    public WireClientIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WireClientTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        var store = new ThreadStore(_tempDir);
        _service = new TestableSessionService(store);

        // Server side: reads from clientToServer.Reader, writes to serverToClient.Writer
        var serverTransport = StdioTransport.Create(
            _clientToServer.Reader.AsStream(),
            _serverToClient.Writer.AsStream());
        serverTransport.Start();

        var connection = new AppServerConnection();
        var handler = new AppServerRequestHandler(
            _service, connection, serverTransport,
            new ModuleRegistryChannelListContributor(new ModuleRegistry(), null, null),
            new AppServerConnectionServices
            {
                ServerVersion = "0.0.1-test",
                HostWorkspacePath = _tempDir,
                AppConfigMonitor = new AppConfigMonitor(AppConfigTestFactory.CreateOpenAI()),
            });

        _serverLoop = Task.Run(() => RunServerLoopAsync(serverTransport, connection, handler, _serverCts.Token));

        // Client side: reads from serverToClient.Reader, writes to clientToServer.Writer
        _wire = new AppServerWireClient(
            _serverToClient.Reader.AsStream(),
            _clientToServer.Writer.AsStream());
        _wire.Start();
    }

    // -------------------------------------------------------------------------
    // Handshake test
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Initialize_OverPipes_ReturnsServerInfo()
    {
        var initDoc = await _wire.InitializeAsync(clientName: "test-cli", clientVersion: "0.0.1");

        var result = initDoc.RootElement.GetProperty("result");
        Assert.Equal("dotcraft", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Equal("1", result.GetProperty("serverInfo").GetProperty("protocolVersion").GetString());
        Assert.True(result.GetProperty("capabilities").GetProperty("threadManagement").GetBoolean());
    }

    // -------------------------------------------------------------------------
    // Thread creation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadStart_OverPipes_ReturnsThreadId()
    {
        await _wire.InitializeAsync();

        var result = await _wire.SendRequestAsync(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ThreadStart, new
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

    [Fact]
    public async Task ThreadStart_OmitsWorkspacePath_NormalizesToHostWorkspace()
    {
        await _wire.InitializeAsync();

        var result = await _wire.SendRequestAsync(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "cli", userId = "local_no_ws" },
            historyMode = "server"
        });

        var thread = result.RootElement.GetProperty("result").GetProperty("thread");
        Assert.Equal(_tempDir, thread.GetProperty("workspacePath").GetString());
    }

    // -------------------------------------------------------------------------
    // Thread discovery
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadList_AfterCreate_ContainsThread()
    {
        await _wire.InitializeAsync();

        // Create a thread
        var createResult = await _wire.SendRequestAsync(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "cli", userId = "local", workspacePath = _tempDir },
            historyMode = "server"
        });
        var threadId = createResult.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;

        // List threads
        var listResult = await _wire.SendRequestAsync(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ThreadList, new
        {
            identity = new { channelName = "cli", userId = "local", workspacePath = _tempDir }
        });

        var data = listResult.RootElement.GetProperty("result").GetProperty("data");
        Assert.Equal(JsonValueKind.Array, data.ValueKind);

        var ids = data.EnumerateArray()
            .Select(el => el.GetProperty("id").GetString())
            .ToList();
        Assert.Contains(threadId, ids);
    }

    // -------------------------------------------------------------------------
    // Turn execution (streaming notifications)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TurnStart_WithCannedEvents_StreamsAgentResponse()
    {
        await _wire.InitializeAsync();

        // Create a thread
        var createResult = await _wire.SendRequestAsync(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "cli", userId = "local", workspacePath = _tempDir },
            historyMode = "server"
        });
        var threadId = createResult.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;

        // Enqueue canned events so SubmitInputAsync produces them
        _service.EnqueueSubmitEvents(threadId, AppServerTestHarness.BuildTurnEventSequence(threadId));

        // Start the turn
        var turnResult = await _wire.SendRequestAsync(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId,
            input = new[] { new { type = "text", text = "Hello" } }
        });

        var turnId = turnResult.RootElement.GetProperty("result").GetProperty("turn").GetProperty("id").GetString();
        Assert.NotNull(turnId);

        // Collect all notifications until turn/completed
        var notifications = new List<string>();
        await foreach (var notif in _wire.ReadTurnNotificationsAsync(ct: CancellationToken.None))
        {
            if (notif.RootElement.TryGetProperty("method", out var m))
                notifications.Add(m.GetString() ?? string.Empty);
        }

        // Should have received streaming events ending with turn/completed
        Assert.Contains(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.AgentMessageDelta, notifications);
        Assert.Equal(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.TurnCompleted, notifications[^1]);
    }

    // -------------------------------------------------------------------------
    // Approval flow
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TurnStart_WithApproval_ClientReceivesApprovalRequest()
    {
        await _wire.InitializeAsync(approvalSupport: true);

        var createResult = await _wire.SendRequestAsync(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "cli", userId = "local", workspacePath = _tempDir },
            historyMode = "server"
        });
        var threadId = createResult.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;

        _service.EnqueueSubmitEvents(threadId, AppServerTestHarness.BuildApprovalEventSequence(threadId));

        // Install an approval handler that auto-accepts
        var approvalRequests = new List<string>();
        _wire.ServerRequestHandler = async doc =>
        {
            var method = doc.RootElement.TryGetProperty("method", out var m) ? m.GetString() : null;
            approvalRequests.Add(method ?? "unknown");
            return new { decision = "accept" };
        };

        await _wire.SendRequestAsync(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId,
            input = new[] { new { type = "text", text = "shell command please" } }
        });

        var methods = new List<string>();
        await foreach (var notif in _wire.ReadTurnNotificationsAsync(ct: CancellationToken.None))
        {
            if (notif.RootElement.TryGetProperty("method", out var m))
                methods.Add(m.GetString() ?? string.Empty);
        }

        // The approval request should have been received by the handler
        Assert.Contains(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ApprovalRequest, approvalRequests);

        // The turn should have completed after approval
        Assert.Equal(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.TurnCompleted, methods[^1]);
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

            if (msg == null) break; // EOF

            if (msg.IsNotification)
            {
                if (msg.Method == DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.Initialized)
                    handler.HandleInitializedNotification();
                continue;
            }

            if (!msg.IsRequest) continue;

            // Process requests concurrently (mirrors AppServerHost behavior)
            _ = Task.Run(async () =>
            {
                object? result;
                try
                {
                    result = await handler.HandleRequestAsync(msg, ct);
                }
                catch (AppServerException ex)
                {
                    await transport.WriteMessageAsync(AppServerRequestHandler.BuildErrorResponse(msg.Id, ex.ToError()), ct);
                    return;
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    var err = AppServerErrors.InternalError(ex.Message).ToError();
                    await transport.WriteMessageAsync(AppServerRequestHandler.BuildErrorResponse(msg.Id, err), ct);
                    return;
                }

                if (result != null)
                    await transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(msg.Id, result), ct);
            }, ct);
        }

        connection.CancelAllSubscriptions();
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        await _wire.DisposeAsync();

        // Complete client-to-server writer to signal EOF to server
        _clientToServer.Writer.Complete();
        _serverToClient.Writer.Complete();

        await _serverCts.CancelAsync();
        try { await _serverLoop; } catch { }
        _serverCts.Dispose();

        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
