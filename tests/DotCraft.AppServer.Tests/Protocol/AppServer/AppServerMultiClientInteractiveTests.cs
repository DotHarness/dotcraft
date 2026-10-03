using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Contract = DotCraft.Protocol.AppServer;
using Methods = DotCraft.Protocol.AppServer.AppServerMethodNames;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerMultiClientInteractiveTests : IAsyncDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "MultiClientInteractive_" + Guid.NewGuid().ToString("N")[..8]);
    private readonly ApprovalModel _model = new();
    private readonly AgentFactory _factory;
    private readonly SessionService _service;
    private readonly List<Client> _clients = [];
    private readonly Channel<AppServerConnection> _accepted = Channel.CreateUnbounded<AppServerConnection>();
    private WebApplication? _server;
    private int _serverPort;

    public AppServerMultiClientInteractiveTests()
    {
        Directory.CreateDirectory(_root);
        _factory = new AgentFactory(
            _root,
            _root,
            AppConfigTestFactory.CreateOpenAI(),
            new MemoryStore(_root),
            new SkillsLoader(_root),
            new AutoApproveApprovalService(),
            null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            chatClient: _model);
        _service = new SessionService(
            _factory,
            new StreamingFunctionInvokingChatClient(_model).AsAIAgent(),
            new SessionPersistenceService(new ThreadStore(_root)),
            new SessionGate());
    }

    [Fact]
    public async Task Approval_RaisedWithNoSubscriber_IsReplayedToALaterSubscriberWhoseAnswerContinuesTheTurn()
    {
        var thread = await CreateThreadAsync();
        var turn = DrainAsync(_service.SubmitInputAsync(thread.Id, [new TextContent("write the notes")]));
        await WaitForAsync(() => thread.Turns.LastOrDefault()?.Status == TurnStatus.WaitingApproval);
        await Task.Delay(200);
        Assert.Equal(TurnStatus.WaitingApproval, thread.Turns[^1].Status);

        var phone = await ConnectAsync("dotcraft-mobile");
        await phone.SubscribeAsync(thread.Id);
        var request = await phone.NextRequestAsync();
        Assert.Equal(Methods.ApprovalRequest, request.Method);
        request.Answer(new { decision = "accept" });

        await turn.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(await _model.Approved);
        Assert.Equal(TurnStatus.Completed, thread.Turns[^1].Status);
    }

    [Fact]
    public async Task Approval_LastHolderDisconnecting_StaysPendingForTheNextSubscriber()
    {
        var thread = await CreateThreadAsync();
        var desktop = await ConnectAsync("dotcraft-desktop");
        await desktop.SubscribeAsync(thread.Id);
        await desktop.StartTurnAsync(thread.Id);
        var held = await desktop.NextRequestAsync();

        await desktop.DisconnectAsync();
        await Task.Delay(300);
        Assert.Equal(TurnStatus.WaitingApproval, thread.Turns[^1].Status);

        var phone = await ConnectAsync("dotcraft-mobile");
        await phone.SubscribeAsync(thread.Id);
        var replayed = await phone.NextRequestAsync();
        Assert.Equal(held.RequestId, replayed.RequestId);
        replayed.Answer(new { decision = "accept" });

        Assert.NotNull(await phone.Wire.WaitForNotificationAsync(Methods.TurnCompleted, TimeSpan.FromSeconds(10)));
        Assert.True(await _model.Approved);
    }

    [Fact]
    public async Task Approval_AnsweredWithoutAValidResult_StaysPendingAndReturnsOnTheNextSubscription()
    {
        var thread = await CreateThreadAsync();
        var desktop = await ConnectAsync("dotcraft-desktop");
        var phone = await ConnectAsync("dotcraft-mobile");
        await desktop.SubscribeAsync(thread.Id);
        await phone.SubscribeAsync(thread.Id);
        await desktop.StartTurnAsync(thread.Id);
        var onDesktop = await desktop.NextRequestAsync();
        var onPhone = await phone.NextRequestAsync();

        onDesktop.Response.TrySetResult(null);
        await Task.Delay(200);
        Assert.Equal(TurnStatus.WaitingApproval, thread.Turns[^1].Status);

        await desktop.SubscribeAsync(thread.Id);
        Assert.Equal(onPhone.RequestId, (await desktop.NextRequestAsync()).RequestId);

        onPhone.Answer(new { decision = "accept" });
        Assert.NotNull(await phone.Wire.WaitForNotificationAsync(Methods.TurnCompleted, TimeSpan.FromSeconds(10)));
        Assert.True(await _model.Approved);
    }

    [Fact]
    public async Task Replay_StopsAtAnApprovalAnsweredWithAnUnknownDecision_AndRestartsThereOnTheNextSubscription()
    {
        _model.Approvals = 2;
        var thread = await CreateThreadAsync();
        var turn = DrainAsync(_service.SubmitInputAsync(thread.Id, [new TextContent("write the notes")]));
        await WaitForAsync(() => thread.Turns.LastOrDefault()?.Items.Count(item => item.Payload is ApprovalRequestPayload) == 2);

        var phone = await ConnectAsync("dotcraft-mobile");
        await phone.SubscribeAsync(thread.Id);
        var first = await phone.NextRequestAsync();
        first.Answer(new { decision = "maybe" });
        await Task.Delay(200);
        Assert.Equal(TurnStatus.WaitingApproval, thread.Turns[^1].Status);

        await phone.SubscribeAsync(thread.Id);
        var again = await phone.NextRequestAsync();
        Assert.Equal(first.RequestId, again.RequestId);
        again.Answer(new { decision = "accept" });
        var second = await phone.NextRequestAsync();
        Assert.NotEqual(first.RequestId, second.RequestId);
        second.Answer(new { decision = "accept" });

        await turn.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(await _model.Approved);
    }

    [Fact]
    public async Task Interrupt_ResolvesPendingApprovalForEverySubscriber_AndIgnoresALateAnswer()
    {
        var thread = await CreateThreadAsync();
        var desktop = await ConnectAsync("dotcraft-desktop");
        var phone = await ConnectAsync("dotcraft-mobile");
        await desktop.SubscribeAsync(thread.Id);
        await phone.SubscribeAsync(thread.Id);
        var turnId = await desktop.StartTurnAsync(thread.Id);
        await desktop.NextRequestAsync();
        var onPhone = await phone.NextRequestAsync();

        await desktop.Wire.SendRequestAsync(Methods.TurnInterrupt, new { threadId = thread.Id, turnId }, TimeSpan.FromSeconds(5));

        foreach (var client in new[] { desktop, phone })
        {
            Assert.NotNull(await client.Wire.WaitForNotificationAsync(Methods.ApprovalResolved, TimeSpan.FromSeconds(5)));
            Assert.NotNull(await client.Wire.WaitForNotificationAsync(Methods.TurnCancelled, TimeSpan.FromSeconds(5)));
        }
        Assert.False(await _model.Approved);

        onPhone.Answer(new { decision = "accept" });
        await Task.Delay(200);
        var response = Assert.Single(thread.Turns[^1].Items.Select(item => item.Payload).OfType<ApprovalResponsePayload>());
        Assert.Equal(SessionApprovalDecision.CancelTurn, response.Decision);
        await phone.Wire.SendRequestAsync(Methods.ThreadRead, new { threadId = thread.Id }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Approval_InTurnStartedByClientWithoutApprovalSupport_ResolvesByPolicyImmediately()
    {
        var thread = await CreateThreadAsync();
        var desktop = await ConnectAsync("dotcraft-desktop");
        await desktop.SubscribeAsync(thread.Id);
        var bot = await ConnectAsync("channel-bot", approvalSupport: false);

        await bot.StartTurnAsync(thread.Id);

        Assert.NotNull(await bot.Wire.WaitForNotificationAsync(Methods.TurnCompleted, TimeSpan.FromSeconds(10)));
        Assert.NotNull(await desktop.Wire.WaitForNotificationAsync(Methods.ApprovalResolved, TimeSpan.FromSeconds(5)));
        Assert.False(await _model.Approved);
        var response = Assert.Single(thread.Turns[^1].Items.Select(item => item.Payload).OfType<ApprovalResponsePayload>());
        Assert.Equal(SessionApprovalDecision.Reject, response.Decision);
    }

    private Task<SessionThread> CreateThreadAsync() =>
        _service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "appserver",
            UserId = "local",
            WorkspacePath = _root
        });

    private async Task<Client> ConnectAsync(string name, bool approvalSupport = true)
    {
        var server = await EnsureServerAsync();
        var wire = await WebSocketClientConnection.ConnectAsync(new Uri($"ws://127.0.0.1:{server}/ws"));
        using var accept = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverConnection = await _accepted.Reader.ReadAsync(accept.Token);
        var client = new Client(wire, serverConnection);
        _clients.Add(client);

        await client.Wire.SendRequestAsync(
            Methods.Initialize,
            new Contract.InitializeParams
            {
                ClientInfo = new Contract.ClientInfo { Name = name, Version = "0.0.1" },
                Capabilities = new Contract.ClientCapabilities
                {
                    ApprovalSupport = approvalSupport,
                    StreamingSupport = true
                }
            },
            TimeSpan.FromSeconds(5));
        await client.Wire.SendNotificationAsync(Methods.Initialized);
        return client;
    }

    private async Task<int> EnsureServerAsync()
    {
        if (_server is not null)
            return _serverPort;

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.UseWebSockets();
        app.Map("/ws", async (HttpContext context) =>
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await using var transport = new WebSocketTransport(socket);
            transport.Start();
            var connection = new AppServerConnection();
            var handler = new AppServerRequestHandler(
                _service,
                connection,
                transport,
                new ModuleRegistryChannelListContributor(new ModuleRegistry()),
                new AppServerConnectionServices
                {
                    ServerVersion = "0.0.1-test",
                    HostWorkspacePath = _root,
                    WorkspaceCraftPath = Path.Combine(_root, ".craft"),
                    AppConfigMonitor = new AppConfigMonitor(AppConfigTestFactory.CreateOpenAI())
                });
            _accepted.Writer.TryWrite(connection);
            await RunServerLoopAsync(transport, connection, handler, context.RequestAborted);
        });
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        _server = app;
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _serverPort = new Uri(address).Port;
        return _serverPort;
    }

    private static async Task RunServerLoopAsync(
        IAppServerTransport transport,
        AppServerConnection connection,
        AppServerRequestHandler handler,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            AppServerIncomingMessage? message;
            try { message = await transport.ReadMessageAsync(ct); }
            catch (OperationCanceledException) { break; }
            if (message is null)
                break;

            if (message.IsNotification)
            {
                if (message.Method == Methods.Initialized)
                    handler.HandleInitializedNotification();
                continue;
            }
            if (!message.IsRequest)
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await handler.HandleRequestAsync(message, ct);
                    if (result is not null)
                        await transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(message.Id, result), ct);
                }
                catch (AppServerException ex)
                {
                    await transport.WriteMessageAsync(AppServerRequestHandler.BuildErrorResponse(message.Id, ex.ToError()), ct);
                }
                catch (Exception)
                {
                }
            }, ct);
        }

        connection.MarkClosed();
        connection.CancelAllSubscriptions();
    }

    private static async Task DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
            await client.DisconnectAsync();
        if (_server is not null)
        {
            await _server.StopAsync();
            await _server.DisposeAsync();
        }
        await _factory.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception) { }
    }

    private sealed class ApprovalModel : IChatClient
    {
        private readonly TaskCompletionSource<bool> _approved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> Approved => _approved.Task;

        public int Approvals { get; set; } = 1;

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var approvals = new SessionScopedApprovalService(new AutoApproveApprovalService());
            var results = await Task.WhenAll(Enumerable.Range(0, Approvals)
                .Select(index => approvals.RequestFileApprovalAsync("write", $"notes{index}.txt")));
            var approved = results.All(result => result);
            _approved.TrySetResult(approved);
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(ChatRole.Assistant, approved ? "Wrote the notes." : "Skipped the notes.");
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class Client
    {
        private readonly WebSocketClientConnection _connection;
        private readonly AppServerConnection _serverConnection;
        private readonly Channel<ServerRequest> _requests = Channel.CreateUnbounded<ServerRequest>();
        private int _disconnected;

        public Client(WebSocketClientConnection connection, AppServerConnection serverConnection)
        {
            _connection = connection;
            _serverConnection = serverConnection;
            connection.Wire.ServerRequestHandler = request =>
            {
                var root = request.RootElement;
                var pending = new ServerRequest(
                    root.GetProperty("method").GetString()!,
                    root.GetProperty("params").GetProperty("requestId").GetString()!);
                _requests.Writer.TryWrite(pending);
                return pending.Response.Task;
            };
        }

        public AppServerWireClient Wire => _connection.Wire;

        public Task SubscribeAsync(string threadId) =>
            Wire.SendRequestAsync(Methods.ThreadSubscribe, new { threadId }, TimeSpan.FromSeconds(5));

        public async Task<string> StartTurnAsync(string threadId)
        {
            using var response = await Wire.SendRequestAsync(Methods.TurnStart, new
            {
                threadId,
                input = new[] { new { type = "text", text = "write the notes" } }
            }, TimeSpan.FromSeconds(5));
            return response.RootElement.GetProperty("result").GetProperty("turn").GetProperty("id").GetString()!;
        }

        public async Task<ServerRequest> NextRequestAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return await _requests.Reader.ReadAsync(timeout.Token);
        }

        public async Task DisconnectAsync()
        {
            if (Interlocked.Exchange(ref _disconnected, 1) != 0)
                return;
            await _connection.DisposeAsync();
            await _serverConnection.Closed.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed record ServerRequest(string Method, string RequestId)
    {
        public TaskCompletionSource<object?> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Answer(object result) => Response.TrySetResult(result);
    }
}
