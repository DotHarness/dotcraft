using System.IO.Pipelines;
using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Modules;
using DotCraft.AppServer;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer.Integration;

public sealed class WireClientIntegrationTests : IAsyncDisposable
{
    private readonly string _tempDir;
    private readonly TestableSessionService _service;

    private readonly Pipe _clientToServer = new();
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

        var serverTransport = StdioTransport.Create(
            _clientToServer.Reader.AsStream(),
            _serverToClient.Writer.AsStream());
        serverTransport.Start();

        var connection = new AppServerConnection();
        var handler = new AppServerRequestHandler(
            _service, connection, serverTransport,
            new ModuleRegistryChannelListContributor(new ModuleRegistry()),
            new AppServerConnectionServices
            {
                ServerVersion = "0.0.1-test",
                HostWorkspacePath = _tempDir,
                WorkspaceCraftPath = Path.Combine(_tempDir, ".agents"),
                AppConfigMonitor = new AppConfigMonitor(AppConfigTestFactory.CreateOpenAI()),
            });

        _serverLoop = Task.Run(() => RunServerLoopAsync(serverTransport, connection, handler, _serverCts.Token));

        _wire = new AppServerWireClient(
            _serverToClient.Reader.AsStream(),
            _clientToServer.Writer.AsStream());
        _wire.Start();
    }

    [Fact]
    public async Task WireClient_OverPipes_HandshakesStartsListsAndStreamsATurn()
    {
        var initDoc = await _wire.InitializeAsync(clientName: "test-cli", clientVersion: "0.0.1");
        var init = initDoc.RootElement.GetProperty("result");
        Assert.Equal("dotcraft", init.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Equal("1", init.GetProperty("serverInfo").GetProperty("protocolVersion").GetString());
        Assert.True(init.GetProperty("capabilities").GetProperty("threadManagement").GetBoolean());

        var createResult = await _wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "cli", userId = "local", workspacePath = _tempDir },
            historyMode = "server"
        });
        var threadId = createResult.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;
        Assert.StartsWith("thread_", threadId);

        var withoutWorkspace = await _wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "cli", userId = "local_no_ws" },
            historyMode = "server"
        });
        Assert.Equal(_tempDir, withoutWorkspace.RootElement.GetProperty("result").GetProperty("thread").GetProperty("workspacePath").GetString());

        var listResult = await _wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadList, new
        {
            identity = new { channelName = "cli", userId = "local", workspacePath = _tempDir }
        });
        Assert.Contains(
            threadId,
            listResult.RootElement.GetProperty("result").GetProperty("data").EnumerateArray().Select(el => el.GetProperty("id").GetString()));

        _service.EnqueueSubmitEvents(threadId, AppServerTestHarness.BuildTurnEventSequence(threadId));
        var turnResult = await _wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        Assert.NotNull(turnResult.RootElement.GetProperty("result").GetProperty("turn").GetProperty("id").GetString());

        var notifications = new List<string>();
        await foreach (var notif in _wire.ReadTurnNotificationsAsync(ct: CancellationToken.None))
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
        await _wire.InitializeAsync(approvalSupport: true);

        var createResult = await _wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "cli", userId = "local", workspacePath = _tempDir },
            historyMode = "server"
        });
        var threadId = createResult.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;

        _service.EnqueueSubmitEvents(threadId, AppServerTestHarness.BuildApprovalEventSequence(threadId));

        var approvalRequest = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _wire.ServerRequestHandler = async doc =>
        {
            approvalRequest.TrySetResult(doc.RootElement.TryGetProperty("method", out var m) ? m.GetString() : null);
            return new { decision = "accept" };
        };

        await _wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
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

        Assert.Equal(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ApprovalRequest,
            await approvalRequest.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCompleted, methods[^1]);
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

    public async ValueTask DisposeAsync()
    {
        await _wire.DisposeAsync();

        _clientToServer.Writer.Complete();
        _serverToClient.Writer.Complete();

        await _serverCts.CancelAsync();
        try { await _serverLoop; } catch { }
        _serverCts.Dispose();

        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
