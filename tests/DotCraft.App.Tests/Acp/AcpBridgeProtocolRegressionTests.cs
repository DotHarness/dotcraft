using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using DotCraft.Acp;
using DotCraft.AppServer;
using DotCraft.Commands.Core;
using DotCraft.Commands.Handlers;
using Xunit;
using AcpStdioTransport = DotHarness.Acp.Transport.StdioTransport;
using Contract = DotCraft.Protocol.AppServer;
using WireStdioTransport = DotCraft.AppServer.StdioTransport;

namespace DotCraft.Tests.Acp;

public sealed class AcpBridgeProtocolRegressionTests
{
    [Fact]
    public async Task Prompt_ForwardsEmbeddedBlobAttachments()
    {
        var turnStartInput = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new StubBridgeFixture(onRequest: (request, ct) =>
        {
            if (request.Method == Contract.AppServerMethodNames.TurnStart && request.Params.HasValue)
                turnStartInput.TrySetResult(request.Params.Value.GetProperty("input").Clone());
            return Task.FromResult<object?>(null);
        });

        await fixture.InitializeAsync();
        await fixture.SendAsync(2, "session/new", new { cwd = "/workspace", mcpServers = Array.Empty<object>() });
        Assert.True((await fixture.ReadUntilResponseAsync(2)).TryGetProperty("result", out _));

        await fixture.SendAsync(3, "session/prompt", new
        {
            sessionId = "session",
            prompt = new object[]
            {
                new
                {
                    type = "resource",
                    resource = new
                    {
                        uri = "file:///diagram.png",
                        mimeType = "image/png",
                        blob = "iVBORw0KGgo="
                    }
                },
                new
                {
                    type = "resource",
                    resource = new
                    {
                        uri = "file:///spec.pdf",
                        mimeType = "application/pdf",
                        blob = "JVBERi0="
                    }
                }
            }
        });

        var promptResponse = await fixture.ReadUntilResponseAsync(3);
        Assert.Equal("end_turn", promptResponse.GetProperty("result").GetProperty("stopReason").GetString());

        var parts = (await turnStartInput.Task.WaitAsync(TimeSpan.FromSeconds(5))).EnumerateArray().ToArray();
        Assert.Equal(2, parts.Length);
        Assert.Equal("image", parts[0].GetProperty("type").GetString());
        Assert.Equal("data:image/png;base64,iVBORw0KGgo=", parts[0].GetProperty("url").GetString());
        Assert.Equal("text", parts[1].GetProperty("type").GetString());
        Assert.Equal("[File: file:///spec.pdf]", parts[1].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Prompt_CancelledDuringTurnStartAdmission_InterruptsAdmittedTurn()
    {
        var turnStartArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTurnStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interruptedTurnId = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var fixture = new StubBridgeFixture(
            autoCompleteTurn: false,
            onRequest: async (request, ct) =>
            {
                if (request.Method == Contract.AppServerMethodNames.TurnStart)
                {
                    turnStartArrived.TrySetResult();
                    await releaseTurnStart.Task.WaitAsync(ct);
                    return new { turn = new { id = "turn_delayed", threadId = "session" } };
                }

                if (request.Method == Contract.AppServerMethodNames.TurnInterrupt && request.Params.HasValue)
                {
                    interruptedTurnId.TrySetResult(request.Params.Value.GetProperty("turnId").GetString() ?? "");
                    return new { };
                }

                return null;
            });

        await fixture.InitializeAsync();
        await fixture.SendAsync(2, "session/new", new { cwd = "/workspace", mcpServers = Array.Empty<object>() });
        Assert.True((await fixture.ReadUntilResponseAsync(2)).TryGetProperty("result", out _));

        await fixture.SendAsync(3, "session/prompt", new
        {
            sessionId = "session",
            prompt = new[] { new { type = "text", text = "slow admission" } }
        });

        await turnStartArrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.SendNotificationAsync("session/cancel", new { sessionId = "session" });
        await Task.Delay(30);
        releaseTurnStart.TrySetResult();

        var promptResponse = await fixture.ReadUntilResponseAsync(3);
        Assert.Equal("cancelled", promptResponse.GetProperty("result").GetProperty("stopReason").GetString());
        Assert.Equal("turn_delayed", await interruptedTurnId.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task SessionNewAndLoad_SendAvailableCommandsUpdateStrictlyAfterResponse()
    {
        var registry = new CommandRegistry();
        registry.RegisterHandler(new NewCommandHandler(), new CommandRegistration
        {
            Name = "/deploy",
            FallbackDescription = "Deploy workspace",
            Category = "workflow"
        });

        await using var fixture = new StubBridgeFixture(
            commandRegistry: registry,
            unsubscribeDelay: TimeSpan.FromMilliseconds(50));

        await fixture.InitializeAsync();

        await fixture.SendAsync(2, "session/new", new { cwd = "/workspace", mcpServers = Array.Empty<object>() });
        var firstNewFrame = await fixture.ReadAsync();
        Assert.False(firstNewFrame.TryGetProperty("method", out _));
        Assert.Equal(2, firstNewFrame.GetProperty("id").GetInt32());
        Assert.Equal("session", firstNewFrame.GetProperty("result").GetProperty("sessionId").GetString());

        var newCommandsFrame = await fixture.ReadAsync();
        AssertAvailableCommandsUpdate(newCommandsFrame, "session", "deploy");

        await fixture.SendRawAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":\"\\u0061\",\"method\":\"session/load\",\"params\":{\"sessionId\":\"session\",\"cwd\":\"/workspace\",\"mcpServers\":[]}}");
        var firstLoadFrame = await fixture.ReadAsync();
        Assert.False(firstLoadFrame.TryGetProperty("method", out _));
        Assert.Equal("a", firstLoadFrame.GetProperty("id").GetString());
        Assert.True(firstLoadFrame.TryGetProperty("result", out _));

        var loadCommandsFrame = await fixture.ReadAsync();
        AssertAvailableCommandsUpdate(loadCommandsFrame, "session", "deploy");
    }

    private static void AssertAvailableCommandsUpdate(JsonElement frame, string expectedSessionId, string expectedCommand)
    {
        Assert.Equal("session/update", frame.GetProperty("method").GetString());
        var @params = frame.GetProperty("params");
        Assert.Equal(expectedSessionId, @params.GetProperty("sessionId").GetString());
        var update = @params.GetProperty("update");
        Assert.Equal("available_commands_update", update.GetProperty("sessionUpdate").GetString());
        var command = Assert.Single(update.GetProperty("availableCommands").EnumerateArray());
        Assert.Equal(expectedCommand, command.GetProperty("name").GetString());
    }

    private sealed class StubBridgeFixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(15));
        private readonly WireStdioTransport _server;
        private readonly AppServerWireClient _wire;
        private readonly AcpStdioTransport _acp;
        private readonly StreamWriter _ideWriter;
        private readonly StreamReader _ideReader;
        private readonly Task _serverTask;
        private readonly Task _bridgeTask;
        private readonly bool _autoCompleteTurn;
        private readonly TimeSpan _unsubscribeDelay;
        private readonly Func<AppServerIncomingMessage, CancellationToken, Task<object?>>? _onRequest;

        public StubBridgeFixture(
            CommandRegistry? commandRegistry = null,
            bool autoCompleteTurn = true,
            TimeSpan unsubscribeDelay = default,
            Func<AppServerIncomingMessage, CancellationToken, Task<object?>>? onRequest = null)
        {
            _autoCompleteTurn = autoCompleteTurn;
            _unsubscribeDelay = unsubscribeDelay;
            _onRequest = onRequest;

            var clientToServer = new Pipe();
            var serverToClient = new Pipe();
            var ideToBridge = new Pipe();
            var bridgeToIde = new Pipe();

            _server = WireStdioTransport.Create(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());
            _server.Start();
            _wire = new AppServerWireClient(serverToClient.Reader.AsStream(), clientToServer.Writer.AsStream());
            _wire.Start();
            _acp = new AcpStdioTransport(ideToBridge.Reader.AsStream(), bridgeToIde.Writer.AsStream());
            _ideWriter = new StreamWriter(ideToBridge.Writer.AsStream(), new UTF8Encoding(false)) { AutoFlush = true };
            _ideReader = new StreamReader(bridgeToIde.Reader.AsStream(), Encoding.UTF8);

            _serverTask = ServeAsync();
            _bridgeTask = new AcpBridgeHandler(_wire, "/workspace", commandRegistry: commandRegistry)
                .RunAsync(_acp, _lifetime.Token);
        }

        public async Task InitializeAsync()
        {
            await SendAsync(1, "initialize",
                new { protocolVersion = 1, clientCapabilities = new { }, clientInfo = new { name = "test", version = "1.0" } });
            Assert.True((await ReadUntilResponseAsync(1)).TryGetProperty("result", out _));
        }

        public Task SendAsync(int id, string method, object parameters) =>
            _ideWriter.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters }));

        public Task SendRawAsync(string json) => _ideWriter.WriteLineAsync(json);

        public Task SendNotificationAsync(string method, object parameters) =>
            _ideWriter.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", method, @params = parameters }));

        public async Task<JsonElement> ReadAsync()
        {
            var line = await _ideReader.ReadLineAsync(_lifetime.Token);
            using var document = JsonDocument.Parse(line ?? throw new EndOfStreamException());
            return document.RootElement.Clone();
        }

        public async Task<JsonElement> ReadUntilResponseAsync(int id)
        {
            while (true)
            {
                var message = await ReadAsync();
                if (!message.TryGetProperty("method", out _)
                    && message.TryGetProperty("id", out var messageId)
                    && messageId.GetInt32() == id)
                {
                    return message;
                }
            }
        }

        private async Task ServeAsync()
        {
            var pendingRequests = new ConcurrentBag<Task>();
            try
            {
                while (!_lifetime.IsCancellationRequested)
                {
                    var request = await _server.ReadMessageAsync(_lifetime.Token);
                    if (request == null)
                        break;
                    if (!request.IsRequest)
                        continue;

                    pendingRequests.Add(HandleRequestAsync(request));
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                try { await Task.WhenAll(pendingRequests); } catch { }
            }
        }

        private async Task HandleRequestAsync(AppServerIncomingMessage request)
        {
            try
            {
                if (request.Method == Contract.AppServerMethodNames.ThreadUnsubscribe && _unsubscribeDelay > TimeSpan.Zero)
                    await Task.Delay(_unsubscribeDelay, _lifetime.Token);

                var customResult = _onRequest != null
                    ? await _onRequest(request, _lifetime.Token)
                    : null;

                object result = customResult ?? request.Method switch
                {
                    Contract.AppServerMethodNames.ThreadStart or Contract.AppServerMethodNames.ThreadResume =>
                        new { thread = new { id = "session", configuration = new { mode = "agent" } } },
                    Contract.AppServerMethodNames.ThreadItemsList => new { data = Array.Empty<object>() },
                    Contract.AppServerMethodNames.ModelList => new Contract.ModelListResult { Success = false },
                    Contract.AppServerMethodNames.TurnStart => new { turn = new { id = "turn_1", threadId = "session" } },
                    _ => new { }
                };

                await _server.WriteMessageAsync(AppServerRequestHandler.BuildResponse(request.Id, result), _lifetime.Token);

                if (request.Method == Contract.AppServerMethodNames.TurnStart && _autoCompleteTurn)
                {
                    await _server.WriteMessageAsync(new
                    {
                        jsonrpc = "2.0",
                        method = Contract.AppServerMethodNames.TurnCompleted,
                        @params = new { turn = new { id = "turn_1", threadId = "session" } }
                    }, _lifetime.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

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
