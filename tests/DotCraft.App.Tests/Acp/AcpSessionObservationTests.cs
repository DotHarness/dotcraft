using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using DotCraft.Acp;
using DotCraft.AppServer;
using Xunit;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Acp;

public sealed class AcpSessionObservationTests
{
    [Fact]
    public async Task Load_RestoresPermission_AndDetachWaitsForTheActualIdeDecision()
    {
        await using var fixture = new BridgeFixture(restorePermission: true);
        await fixture.InitializeAsync();
        await fixture.SendAsync(2, AcpMethods.SessionLoad, new { sessionId = "session", cwd = "/workspace" });

        JsonElement permission = default;
        var loaded = false;
        while (!loaded || permission.ValueKind == JsonValueKind.Undefined)
        {
            var message = await fixture.ReadAsync();
            if (message.TryGetProperty("method", out var method) && method.GetString() == AcpMethods.RequestPermission)
                permission = message;
            if (message.TryGetProperty("id", out var id) && id.GetInt32() == 2)
            {
                Assert.True(message.TryGetProperty("result", out _));
                loaded = true;
            }
        }

        Assert.Equal("session", permission.GetProperty("params").GetProperty("sessionId").GetString());
        Assert.Equal("pending", permission.GetProperty("params").GetProperty("toolCall").GetProperty("toolCallId").GetString());
        await fixture.WaitForDetachAsync();
        Assert.False(fixture.PermissionAnswer.IsCompleted);

        await fixture.SendIdeResponseAsync(permission.GetProperty("id").GetInt32(),
            new { outcome = new { outcome = "selected", optionId = "allow-once" } });
        var answer = await fixture.PermissionAnswer.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("accept", answer.Result!.Value.GetProperty("decision").GetString());

        await fixture.SendAsync(3, AcpMethods.SessionPrompt,
            new { sessionId = "session", prompt = new[] { new { type = "text", text = "continue" } } });
        var promptResult = await fixture.ReadUntilResponseAsync(3);
        Assert.Equal(AcpStopReason.EndTurn, promptResult.GetProperty("result").GetProperty("stopReason").GetString());
        await fixture.WaitForDetachAsync();

        var resumes = fixture.Requests.Where(r => r.Method == Contract.AppServerMethodNames.ThreadResume).ToArray();
        Assert.Equal(2, resumes.Length);
        var warmResume = resumes[1].Params!.Value;
        Assert.Equal(new[] { "threadId" }, warmResume.EnumerateObject().Select(p => p.Name));
    }

    [Theory]
    [InlineData("turn/completed", AcpStopReason.EndTurn)]
    [InlineData("turn/cancelled", AcpStopReason.Cancelled)]
    [InlineData("turn/failed", null)]
    public async Task Prompt_RejoinsAfterInitialization_ConsumesOnlyItsTurn_AndReleasesObservation(
        string terminalMethod, string? expectedStopReason)
    {
        await using var fixture = new BridgeFixture(terminalMethod: terminalMethod);
        await fixture.InitializeAsync();
        await fixture.SendAsync(2, AcpMethods.SessionNew, new { });
        Assert.True((await fixture.ReadUntilResponseAsync(2)).TryGetProperty("result", out _));
        await fixture.WaitForDetachAsync();

        await fixture.SendAsync(3, AcpMethods.SessionPrompt,
            new { sessionId = "session", prompt = new[] { new { type = "text", text = "work" } } });
        var output = new List<string>();
        var result = await fixture.ReadUntilResponseAsync(3, message =>
        {
            if (message.TryGetProperty("params", out var parameters)
                && parameters.TryGetProperty("update", out var update)
                && update.TryGetProperty("content", out var content))
                output.Add(content.GetProperty("text").GetString()!);
        });
        Assert.Equal(new[] { "target answer" }, output);
        if (expectedStopReason == null)
            Assert.Equal("execution failed", result.GetProperty("error").GetProperty("message").GetString());
        else
            Assert.Equal(expectedStopReason, result.GetProperty("result").GetProperty("stopReason").GetString());
        await fixture.WaitForDetachAsync();

        var methods = fixture.Requests.Select(r => r.Method).ToList();
        Assert.True(methods.IndexOf(Contract.AppServerMethodNames.ThreadResume)
            < methods.IndexOf(Contract.AppServerMethodNames.TurnStart));
        Assert.Equal(2, methods.Count(m => m == Contract.AppServerMethodNames.ThreadUnsubscribe));
    }

    private sealed class BridgeFixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(15));
        private readonly StdioTransport _server;
        private readonly AppServerWireClient _wire;
        private readonly AcpTransport _acp;
        private readonly StreamWriter _ideWriter;
        private readonly StreamReader _ideReader;
        private readonly Task _serverTask;
        private readonly Task _bridgeTask;
        private readonly Channel<bool> _detached = Channel.CreateUnbounded<bool>();
        private readonly TaskCompletionSource<AppServerIncomingMessage> _permissionAnswer =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool _restorePermission;
        private readonly string _terminalMethod;
        private bool _attached;
        private int _resumes;

        public BridgeFixture(bool restorePermission = false, string terminalMethod = "turn/completed")
        {
            _restorePermission = restorePermission;
            _terminalMethod = terminalMethod;
            var clientToServer = new Pipe();
            var serverToClient = new Pipe();
            var ideToBridge = new Pipe();
            var bridgeToIde = new Pipe();
            _server = StdioTransport.Create(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());
            _server.Start();
            _wire = new AppServerWireClient(serverToClient.Reader.AsStream(), clientToServer.Writer.AsStream());
            _wire.Start();
            _acp = new AcpTransport(ideToBridge.Reader.AsStream(), bridgeToIde.Writer.AsStream())
            {
                HeartbeatInterval = Timeout.InfiniteTimeSpan
            };
            _acp.StartReaderLoop();
            _ideWriter = new StreamWriter(ideToBridge.Writer.AsStream(), new UTF8Encoding(false)) { AutoFlush = true };
            _ideReader = new StreamReader(bridgeToIde.Reader.AsStream());
            _serverTask = ServeAsync();
            _bridgeTask = new AcpBridgeHandler(_acp, _wire, "/workspace").RunAsync(_lifetime.Token);
        }

        public ConcurrentQueue<AppServerIncomingMessage> Requests { get; } = new();
        public Task<AppServerIncomingMessage> PermissionAnswer => _permissionAnswer.Task;

        public async Task InitializeAsync()
        {
            await SendAsync(1, AcpMethods.Initialize,
                new { protocolVersion = 1, clientCapabilities = new { }, clientInfo = new { name = "test" } });
            Assert.True((await ReadUntilResponseAsync(1)).TryGetProperty("result", out _));
        }

        public Task SendAsync(int id, string method, object parameters) =>
            _ideWriter.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters }));

        public Task SendIdeResponseAsync(int id, object result) =>
            _ideWriter.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }));

        public async Task<JsonElement> ReadAsync()
        {
            var line = await _ideReader.ReadLineAsync(_lifetime.Token);
            using var document = JsonDocument.Parse(line ?? throw new EndOfStreamException());
            return document.RootElement.Clone();
        }

        public async Task<JsonElement> ReadUntilResponseAsync(int id, Action<JsonElement>? onNotification = null)
        {
            while (true)
            {
                var message = await ReadAsync();
                if (!message.TryGetProperty("method", out _)
                    && message.TryGetProperty("id", out var messageId) && messageId.GetInt32() == id)
                    return message;
                onNotification?.Invoke(message);
            }
        }

        public async Task WaitForDetachAsync() => await _detached.Reader.ReadAsync(_lifetime.Token);

        private async Task ServeAsync()
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var request = await _server.ReadMessageAsync(_lifetime.Token);
                if (request == null) return;
                if (!request.IsRequest) continue;
                Requests.Enqueue(request);
                object result = request.Method switch
                {
                    Contract.AppServerMethodNames.ThreadStart or Contract.AppServerMethodNames.ThreadResume =>
                        new { thread = new { id = "session", configuration = new { mode = "agent" } }, currentState = new { runtime = new { running = false } } },
                    Contract.AppServerMethodNames.ThreadItemsList => new { data = Array.Empty<object>() },
                    Contract.AppServerMethodNames.ModelList => new Contract.ModelListResult { Success = false },
                    Contract.AppServerMethodNames.TurnStart => new { turn = new { id = "target", threadId = "session" } },
                    _ => new { }
                };
                if (request.Method is Contract.AppServerMethodNames.ThreadStart or Contract.AppServerMethodNames.ThreadResume)
                    _attached = true;
                if (request.Method == Contract.AppServerMethodNames.TurnStart)
                    Assert.True(_attached, "A prompt must rejoin before admitting a Turn.");
                await _server.WriteMessageAsync(AppServerRequestHandler.BuildResponse(request.Id, result), _lifetime.Token);
                if (request.Method == Contract.AppServerMethodNames.ThreadUnsubscribe)
                {
                    _attached = false;
                    _detached.Writer.TryWrite(true);
                }
                if (request.Method == Contract.AppServerMethodNames.ThreadResume && ++_resumes == 1 && _restorePermission)
                    _ = RestorePermissionAsync();
                if (request.Method == Contract.AppServerMethodNames.TurnStart)
                {
                    await NotifyAsync("turn/completed", new { turn = new { id = "target", threadId = "another" } });
                    await NotifyAsync("turn/failed", new { turn = new { id = "previous", threadId = "session" }, error = "wrong failure" });
                    await NotifyAsync("item/agentMessage/delta", new { threadId = "session", turnId = "previous", delta = "wrong output" });
                    await NotifyAsync("item/agentMessage/delta", new { threadId = "session", turnId = "target", delta = "target answer" });
                    await NotifyAsync(_terminalMethod, new { turn = new { id = "target", threadId = "session" }, error = "execution failed" });
                }
            }
        }

        private async Task RestorePermissionAsync()
        {
            try
            {
                var pending = await _server.StartClientRequestAsync(Contract.AppServerMethodNames.ApprovalRequest,
                    new { threadId = "session", turnId = "existing", requestId = "pending", approvalType = "shell", operation = "build" },
                    _lifetime.Token, TimeSpan.FromSeconds(10));
                _permissionAnswer.TrySetResult(await pending.Response);
            }
            catch (Exception ex)
            {
                _permissionAnswer.TrySetException(ex);
            }
        }

        private Task NotifyAsync(string method, object parameters) =>
            _server.WriteMessageAsync(new { jsonrpc = "2.0", method, @params = parameters }, _lifetime.Token);

        public async ValueTask DisposeAsync()
        {
            await _lifetime.CancelAsync();
            await _ideWriter.DisposeAsync();
            await _wire.DisposeAsync();
            await _acp.DisposeAsync();
            await _server.DisposeAsync();
            try { await Task.WhenAll(_bridgeTask, _serverTask); }
            catch (OperationCanceledException) { }
            _ideReader.Dispose();
            _lifetime.Dispose();
        }
    }
}
