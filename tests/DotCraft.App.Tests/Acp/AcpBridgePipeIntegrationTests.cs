using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Acp;
using DotCraft.Configuration;
using DotCraft.Modules;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Tests.Sessions.Protocol.AppServer;

namespace DotCraft.Tests.Acp;

/// <summary>
/// Integration test: <see cref="AcpBridgeHandler"/> over in-memory stdio pipes against the same
/// AppServer harness as <see cref="WireClientIntegrationTests"/>, with <see cref="WireAcpExtensionProxy"/>.
/// </summary>
public sealed class AcpBridgePipeIntegrationTests
{
    [Fact]
    public async Task AcpBridge_InitializeAndSessionNew_RoundTripsOverPipes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AcpBridgePipe_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        var store = new ThreadStore(tempDir);
        var service = new TestableSessionService(store);

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var ideToBridge = new Pipe();
        var bridgeToIde = new Pipe();

        var serverTransport = StdioTransport.Create(
            clientToServer.Reader.AsStream(),
            serverToClient.Writer.AsStream());
        serverTransport.Start();

        var connection = new AppServerConnection();
        var wireAcp = new WireAcpExtensionProxy();
        var handler = new AppServerRequestHandler(
            service,
            connection,
            serverTransport,
            new ModuleRegistryChannelListContributor(new ModuleRegistry(), null, null),
            new AppServerConnectionServices
            {
                ServerVersion = "0.0.1-test",
                HostWorkspacePath = tempDir,
                WireAcpExtensionProxy = wireAcp,
                AppConfigMonitor = CreateOpenAIConfigMonitor(),
            });

        var serverCts = new CancellationTokenSource();
        var serverLoop = Task.Run(() => WireClientIntegrationTestsRunServerLoop.RunAsync(serverTransport, connection, handler, serverCts.Token));

        await using var wire = new AppServerWireClient(
            serverToClient.Reader.AsStream(),
            clientToServer.Writer.AsStream());
        wire.Start();

        await using var acp = new AcpTransport(ideToBridge.Reader.AsStream(), bridgeToIde.Writer.AsStream());
        acp.StartReaderLoop();

        var bridgeCts = new CancellationTokenSource();
        var bridge = new AcpBridgeHandler(acp, wire, tempDir);
        var bridgeTask = Task.Run(() => bridge.RunAsync(bridgeCts.Token));

        try
        {
            await using var ideWriter = new StreamWriter(ideToBridge.Writer.AsStream(), Encoding.UTF8) { AutoFlush = true };
            using var ideReader = new StreamReader(bridgeToIde.Reader.AsStream(), Encoding.UTF8);

            const string initLine =
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":1,"
                + "\"clientCapabilities\":{\"fs\":{\"readTextFile\":true}},"
                + "\"clientInfo\":{\"name\":\"test-ide\",\"version\":\"1.0\"}}}";
            await ideWriter.WriteLineAsync(initLine);

            var initResponse = await ReadJsonLineAsync(ideReader);
            using var initDoc = JsonDocument.Parse(initResponse);
            Assert.Equal(1, initDoc.RootElement.GetProperty("id").GetInt32());
            Assert.True(initDoc.RootElement.TryGetProperty("result", out var initResult));
            Assert.Equal(AcpBridgeHandler.ProtocolVersion, initResult.GetProperty("protocolVersion").GetInt32());

            await ideWriter.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"session/new\",\"params\":{}}");

            var sessionResponse = await ReadJsonLineAsync(ideReader);
            using var sessionDoc = JsonDocument.Parse(sessionResponse);
            Assert.Equal(2, sessionDoc.RootElement.GetProperty("id").GetInt32());
            Assert.True(sessionDoc.RootElement.TryGetProperty("result", out var sessionResult));
            var sessionId = sessionResult.GetProperty("sessionId").GetString();
            Assert.NotNull(sessionId);
            Assert.StartsWith("thread_", sessionId);
        }
        finally
        {
            bridgeToIde.Writer.Complete();
            ideToBridge.Writer.Complete();
            bridgeCts.Cancel();
            try
            {
                await bridgeTask.WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch
            {
                // Bridge may throw on cancel; ignore
            }

            await wire.DisposeAsync();
            clientToServer.Writer.Complete();
            serverToClient.Writer.Complete();
            serverCts.Cancel();
            try
            {
                await serverLoop.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
                /* ignore */
            }

            serverCts.Dispose();
            bridgeCts.Dispose();
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    [Fact]
    public async Task AcpBridge_SessionNew_ForwardsClientRuntimeToolsAsDynamicTools()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AcpBridgeRuntimeTools_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var ideToBridge = new Pipe();
        var bridgeToIde = new Pipe();

        var serverTransport = StdioTransport.Create(
            clientToServer.Reader.AsStream(),
            serverToClient.Writer.AsStream());
        serverTransport.Start();

        var capturedThreadStart = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverCts = new CancellationTokenSource();
        var serverLoop = Task.Run(() => RunRuntimeToolWireStubAsync(serverTransport, capturedThreadStart, serverCts.Token));

        await using var wire = new AppServerWireClient(
            serverToClient.Reader.AsStream(),
            clientToServer.Writer.AsStream());
        wire.Start();

        await using var acp = new AcpTransport(ideToBridge.Reader.AsStream(), bridgeToIde.Writer.AsStream());
        acp.StartReaderLoop();

        var bridgeCts = new CancellationTokenSource();
        var bridge = new AcpBridgeHandler(acp, wire, tempDir);
        var bridgeTask = Task.Run(() => bridge.RunAsync(bridgeCts.Token));

        try
        {
            await using var ideWriter = new StreamWriter(ideToBridge.Writer.AsStream(), Encoding.UTF8) { AutoFlush = true };
            using var ideReader = new StreamReader(bridgeToIde.Reader.AsStream(), Encoding.UTF8);

            const string initLine =
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":1,"
                + "\"clientCapabilities\":{\"extensions\":[\"_unity\"],\"_meta\":{\"dotcraft\":{\"runtimeTools\":[{"
                + "\"namespace\":\"unity\","
                + "\"name\":\"unity_scene_query\","
                + "\"description\":\"Query Unity scene hierarchy.\","
                + "\"inputSchema\":{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"}}},"
                + "\"acpMethod\":\"_unity/scene_query\","
                + "\"kind\":\"unity\","
                + "\"deferLoading\":true"
                + "}]}}},"
                + "\"clientInfo\":{\"name\":\"test-ide\",\"version\":\"1.0\"}}}";
            await ideWriter.WriteLineAsync(initLine);

            var initResponse = await ReadJsonLineAsync(ideReader);
            using var initDoc = JsonDocument.Parse(initResponse);
            Assert.True(initDoc.RootElement.TryGetProperty("result", out _));

            await ideWriter.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"session/new\",\"params\":{}}");

            var threadStartParams = await capturedThreadStart.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var dynamicTools = threadStartParams.GetProperty("dynamicTools");
            var toolNamespace = Assert.Single(dynamicTools.EnumerateArray());
            Assert.Equal("namespace", toolNamespace.GetProperty("type").GetString());
            Assert.Equal("unity", toolNamespace.GetProperty("name").GetString());
            var tool = Assert.Single(toolNamespace.GetProperty("tools").EnumerateArray());
            Assert.Equal("function", tool.GetProperty("type").GetString());
            Assert.Equal("unity_scene_query", tool.GetProperty("name").GetString());
            Assert.Equal("Query Unity scene hierarchy.", tool.GetProperty("description").GetString());
            Assert.True(tool.GetProperty("deferLoading").GetBoolean());
            Assert.Equal("object", tool.GetProperty("inputSchema").GetProperty("type").GetString());

            var sessionResponse = await ReadJsonLineAsync(ideReader);
            using var sessionDoc = JsonDocument.Parse(sessionResponse);
            Assert.True(sessionDoc.RootElement.TryGetProperty("result", out var sessionResult));
            Assert.Equal("thread_runtime_tools", sessionResult.GetProperty("sessionId").GetString());
        }
        finally
        {
            bridgeToIde.Writer.Complete();
            ideToBridge.Writer.Complete();
            bridgeCts.Cancel();
            try
            {
                await bridgeTask.WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch
            {
                // Bridge may throw on cancel; ignore
            }

            await wire.DisposeAsync();
            clientToServer.Writer.Complete();
            serverToClient.Writer.Complete();
            serverCts.Cancel();
            try
            {
                await serverLoop.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
                /* ignore */
            }

            serverCts.Dispose();
            bridgeCts.Dispose();
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    [Fact]
    public async Task AcpBridge_Prompt_MapsReasoningDeltaToAgentThoughtChunk()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AcpBridgeThought_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        var store = new ThreadStore(tempDir);
        var service = new TestableSessionService(store);

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var ideToBridge = new Pipe();
        var bridgeToIde = new Pipe();

        var serverTransport = StdioTransport.Create(
            clientToServer.Reader.AsStream(),
            serverToClient.Writer.AsStream());
        serverTransport.Start();

        var connection = new AppServerConnection();
        var wireAcp = new WireAcpExtensionProxy();
        var handler = new AppServerRequestHandler(
            service,
            connection,
            serverTransport,
            new ModuleRegistryChannelListContributor(new ModuleRegistry(), null, null),
            new AppServerConnectionServices
            {
                ServerVersion = "0.0.1-test",
                HostWorkspacePath = tempDir,
                WireAcpExtensionProxy = wireAcp,
                AppConfigMonitor = CreateOpenAIConfigMonitor(),
            });

        var serverCts = new CancellationTokenSource();
        var serverLoop = Task.Run(() => WireClientIntegrationTestsRunServerLoop.RunAsync(serverTransport, connection, handler, serverCts.Token));

        await using var wire = new AppServerWireClient(
            serverToClient.Reader.AsStream(),
            clientToServer.Writer.AsStream());
        wire.Start();

        await using var acp = new AcpTransport(ideToBridge.Reader.AsStream(), bridgeToIde.Writer.AsStream());
        acp.StartReaderLoop();

        var bridgeCts = new CancellationTokenSource();
        var bridge = new AcpBridgeHandler(acp, wire, tempDir);
        var bridgeTask = Task.Run(() => bridge.RunAsync(bridgeCts.Token));

        try
        {
            await using var ideWriter = new StreamWriter(ideToBridge.Writer.AsStream(), Encoding.UTF8) { AutoFlush = true };
            using var ideReader = new StreamReader(bridgeToIde.Reader.AsStream(), Encoding.UTF8);

            await ideWriter.WriteLineAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":1,"
                + "\"clientCapabilities\":{\"fs\":{\"readTextFile\":true}},"
                + "\"clientInfo\":{\"name\":\"test-ide\",\"version\":\"1.0\"}}}");
            _ = await ReadJsonLineForIdAsync(ideReader, 1);

            await ideWriter.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"session/new\",\"params\":{}}");
            using var sessionDoc = JsonDocument.Parse(await ReadJsonLineForIdAsync(ideReader, 2));
            var sessionId = sessionDoc.RootElement.GetProperty("result").GetProperty("sessionId").GetString()!;

            var turn = AppServerTestHarness.MakeTurn(sessionId);
            var completedTurn = AppServerTestHarness.MakeCompletedTurn(sessionId);
            var now = DateTimeOffset.UtcNow;
            service.EnqueueSubmitEvents(sessionId,
                new SessionEvent
                {
                    EventId = "e1",
                    EventType = SessionEventType.TurnStarted,
                    ThreadId = sessionId,
                    TurnId = turn.Id,
                    Timestamp = now,
                    Payload = turn
                },
                new SessionEvent
                {
                    EventId = "e2",
                    EventType = SessionEventType.ItemDelta,
                    ThreadId = sessionId,
                    TurnId = turn.Id,
                    ItemId = "item_reasoning_001",
                    Timestamp = now,
                    Payload = new ReasoningContentDelta { TextDelta = "I need to inspect state first." }
                },
                new SessionEvent
                {
                    EventId = "e3",
                    EventType = SessionEventType.ItemDelta,
                    ThreadId = sessionId,
                    TurnId = turn.Id,
                    ItemId = "item_agent_001",
                    Timestamp = now,
                    Payload = new AgentMessageDelta { TextDelta = "Visible answer." }
                },
                new SessionEvent
                {
                    EventId = "e4",
                    EventType = SessionEventType.TurnCompleted,
                    ThreadId = sessionId,
                    TurnId = turn.Id,
                    Timestamp = now,
                    Payload = completedTurn
                });

            await ideWriter.WriteLineAsync(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "session/prompt",
                @params = new
                {
                    sessionId,
                    prompt = new[] { new { type = "text", text = "Think, then answer" } }
                }
            }));

            var updates = await ReadSessionUpdateLinesUntilResponseAsync(ideReader, responseId: 3);

            AssertTextUpdate(updates, AcpUpdateKind.AgentThoughtChunk, "I need to inspect state first.");
            AssertTextUpdate(updates, AcpUpdateKind.AgentMessageChunk, "Visible answer.");
        }
        finally
        {
            bridgeToIde.Writer.Complete();
            ideToBridge.Writer.Complete();
            bridgeCts.Cancel();
            try
            {
                await bridgeTask.WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch
            {
                // Bridge may throw on cancel; ignore
            }

            await wire.DisposeAsync();
            clientToServer.Writer.Complete();
            serverToClient.Writer.Complete();
            serverCts.Cancel();
            try
            {
                await serverLoop.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
                /* ignore */
            }

            serverCts.Dispose();
            bridgeCts.Dispose();
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    [Fact]
    public async Task AcpBridge_ModelConfigOption_SetsThreadModel()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AcpBridgeModel_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        var threadConfig = new JsonObject { ["mode"] = "agent" };

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var ideToBridge = new Pipe();
        var bridgeToIde = new Pipe();

        var serverTransport = StdioTransport.Create(
            clientToServer.Reader.AsStream(),
            serverToClient.Writer.AsStream());
        serverTransport.Start();

        var serverCts = new CancellationTokenSource();
        var serverLoop = Task.Run(() => RunModelConfigWireStubAsync(serverTransport, threadConfig, serverCts.Token));

        await using var wire = new AppServerWireClient(
            serverToClient.Reader.AsStream(),
            clientToServer.Writer.AsStream());
        wire.Start();

        await using var acp = new AcpTransport(ideToBridge.Reader.AsStream(), bridgeToIde.Writer.AsStream());
        acp.StartReaderLoop();

        var bridgeCts = new CancellationTokenSource();
        var bridge = new AcpBridgeHandler(acp, wire, tempDir);
        var bridgeTask = Task.Run(() => bridge.RunAsync(bridgeCts.Token));

        try
        {
            await using var ideWriter = new StreamWriter(ideToBridge.Writer.AsStream(), Encoding.UTF8) { AutoFlush = true };
            using var ideReader = new StreamReader(bridgeToIde.Reader.AsStream(), Encoding.UTF8);

            await ideWriter.WriteLineAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":1,"
                + "\"clientCapabilities\":{\"fs\":{\"readTextFile\":true}},"
                + "\"clientInfo\":{\"name\":\"test-ide\",\"version\":\"1.0\"}}}");
            _ = await ReadJsonLineForIdAsync(ideReader, 1);

            await ideWriter.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"session/new\",\"params\":{}}");
            using var sessionDoc = JsonDocument.Parse(await ReadJsonLineForIdAsync(ideReader, 2));
            var sessionResult = sessionDoc.RootElement.GetProperty("result");
            var sessionId = sessionResult.GetProperty("sessionId").GetString()!;
            var modelOption = FindConfigOption(sessionResult.GetProperty("configOptions"), "model");
            Assert.Equal(AcpBridgeHandler.DefaultModelValue, modelOption.GetProperty("currentValue").GetString());
            Assert.Contains(modelOption.GetProperty("options").EnumerateArray(), o => o.GetProperty("value").GetString() == "gpt-beta");

            await ideWriter.WriteLineAsync(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "session/set_config_option",
                @params = new { sessionId, configId = "model", value = "gpt-beta" }
            }));
            using var setDoc = JsonDocument.Parse(await ReadJsonLineForIdAsync(ideReader, 3));
            var setModelOption = FindConfigOption(setDoc.RootElement.GetProperty("result").GetProperty("configOptions"), "model");
            Assert.Equal("gpt-beta", setModelOption.GetProperty("currentValue").GetString());
            using var updateDoc = JsonDocument.Parse(await ReadJsonLineAsync(ideReader));
            Assert.Equal(AcpUpdateKind.ConfigOptionsUpdate,
                updateDoc.RootElement.GetProperty("params").GetProperty("update").GetProperty("sessionUpdate").GetString());
            Assert.Equal("gpt-beta", threadConfig["model"]?.GetValue<string>());

            await ideWriter.WriteLineAsync(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 4,
                method = "session/set_config_option",
                @params = new { sessionId, configId = "model", value = AcpBridgeHandler.DefaultModelValue }
            }));
            using var clearDoc = JsonDocument.Parse(await ReadJsonLineForIdAsync(ideReader, 4));
            var clearModelOption = FindConfigOption(clearDoc.RootElement.GetProperty("result").GetProperty("configOptions"), "model");
            Assert.Equal(AcpBridgeHandler.DefaultModelValue, clearModelOption.GetProperty("currentValue").GetString());
            Assert.False(threadConfig.ContainsKey("model"));
        }
        finally
        {
            bridgeToIde.Writer.Complete();
            ideToBridge.Writer.Complete();
            bridgeCts.Cancel();
            try
            {
                await bridgeTask.WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch
            {
                // Bridge may throw on cancel; ignore
            }

            await wire.DisposeAsync();
            clientToServer.Writer.Complete();
            serverToClient.Writer.Complete();
            serverCts.Cancel();
            try
            {
                await serverLoop.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
                /* ignore */
            }

            serverCts.Dispose();
            bridgeCts.Dispose();
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private static async Task<string> ReadJsonLineAsync(StreamReader reader)
    {
        var line = await reader.ReadLineAsync();
        Assert.NotNull(line);
        return line;
    }

    private static async Task<string> ReadJsonLineForIdAsync(StreamReader reader, int id)
    {
        for (var i = 0; i < 10; i++)
        {
            var line = await ReadJsonLineAsync(reader);
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("id", out var idEl) && idEl.GetInt32() == id)
                return line;
        }

        throw new InvalidOperationException($"No ACP response with id {id} was sent.");
    }

    private static async Task<List<string>> ReadSessionUpdateLinesUntilResponseAsync(StreamReader reader, int responseId)
    {
        var updates = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            var line = await ReadJsonLineAsync(reader);
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("id", out var idEl) && idEl.GetInt32() == responseId)
            {
                Assert.Equal(AcpStopReason.EndTurn,
                    root.GetProperty("result").GetProperty("stopReason").GetString());
                return updates;
            }

            if (root.TryGetProperty("method", out var methodEl)
                && methodEl.GetString() == AcpMethods.SessionUpdate)
            {
                updates.Add(line);
            }
        }

        throw new InvalidOperationException($"No ACP response with id {responseId} was sent.");
    }

    private static void AssertTextUpdate(IEnumerable<string> updates, string updateKind, string expectedText)
    {
        var matches = updates.Where(line =>
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement
                .GetProperty("params")
                .GetProperty("update")
                .GetProperty("sessionUpdate")
                .GetString() == updateKind;
        }).ToList();

        var raw = Assert.Single(matches);
        using var match = JsonDocument.Parse(raw);
        var content = match.RootElement
            .GetProperty("params")
            .GetProperty("update")
            .GetProperty("content");
        Assert.Equal("text", content.GetProperty("type").GetString());
        Assert.Equal(expectedText, content.GetProperty("text").GetString());
    }

    private static JsonElement FindConfigOption(JsonElement configOptions, string id)
    {
        foreach (var option in configOptions.EnumerateArray())
        {
            if (option.GetProperty("id").GetString() == id)
                return option;
        }

        throw new InvalidOperationException($"Config option '{id}' was not returned.");
    }

    private static async Task RunModelConfigWireStubAsync(
        IAppServerTransport transport,
        JsonObject threadConfig,
        CancellationToken ct)
    {
        const string threadId = "thread_model_config";
        while (!ct.IsCancellationRequested)
        {
            AppServerIncomingMessage? msg;
            try
            {
                msg = await transport.ReadMessageAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (msg == null)
                break;
            if (msg.IsNotification)
                continue;
            if (!msg.IsRequest)
                continue;

            object? result = msg.Method switch
            {
                AppServerMethods.Initialize => new AppServerInitializeResult(),
                AppServerMethods.ThreadStart => new
                {
                    thread = new
                    {
                        id = threadId,
                        configuration = threadConfig
                    }
                },
                AppServerMethods.ModelList => new ModelListResult
                {
                    Success = true,
                    Models =
                    [
                        new ModelCatalogItem { Id = "gpt-alpha" },
                        new ModelCatalogItem { Id = "gpt-beta" }
                    ]
                },
                AppServerMethods.WorkspaceConfigUpdate => new WorkspaceConfigUpdateResult(),
                AppServerMethods.ThreadRead => new
                {
                    thread = new
                    {
                        id = threadId,
                        configuration = threadConfig,
                        turns = Array.Empty<object>()
                    }
                },
                AppServerMethods.ThreadConfigUpdate => UpdateThreadConfig(msg.Params, threadConfig),
                _ => new { }
            };

            await transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(msg.Id, result), ct);
        }
    }

    private static async Task RunRuntimeToolWireStubAsync(
        IAppServerTransport transport,
        TaskCompletionSource<JsonElement> capturedThreadStart,
        CancellationToken ct)
    {
        const string threadId = "thread_runtime_tools";
        while (!ct.IsCancellationRequested)
        {
            AppServerIncomingMessage? msg;
            try
            {
                msg = await transport.ReadMessageAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (msg == null)
                break;
            if (msg.IsNotification)
                continue;
            if (!msg.IsRequest)
                continue;

            object? result;
            switch (msg.Method)
            {
                case AppServerMethods.Initialize:
                    result = new AppServerInitializeResult();
                    break;
                case AppServerMethods.ThreadStart:
                    if (msg.Params is { ValueKind: JsonValueKind.Object } p)
                        capturedThreadStart.TrySetResult(p.Clone());
                    result = new
                    {
                        thread = new
                        {
                            id = threadId,
                            configuration = new JsonObject()
                        }
                    };
                    break;
                case AppServerMethods.ModelList:
                    result = new ModelListResult { Success = false };
                    break;
                default:
                    result = new { };
                    break;
            }

            await transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(msg.Id, result), ct);
        }
    }

    private static string? TryReadModelParam(JsonElement? @params)
    {
        if (@params is not { ValueKind: JsonValueKind.Object } p
            || !p.TryGetProperty("model", out var model)
            || model.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return model.GetString();
    }

    private static object UpdateThreadConfig(JsonElement? @params, JsonObject target)
    {
        if (@params is { ValueKind: JsonValueKind.Object } p
            && p.TryGetProperty("config", out var config)
            && JsonNode.Parse(config.GetRawText()) is JsonObject next)
        {
            target.Clear();
            foreach (var kv in next.ToList())
            {
                next.Remove(kv.Key);
                target[kv.Key] = kv.Value;
            }
        }

        return new { };
    }

    private static AppConfigMonitor CreateOpenAIConfigMonitor() =>
        new(new AppConfig
        {
            ProviderId = "openai",
            ProviderPreferences = new() { ["openai"] = new ModelPreference { Model = "gpt-4o-mini"  } },
            Providers =
            {
                ["openai"] = new AppConfig.ModelProviderConfig
                {
                    DisplayName = "OpenAI",
                    Protocol = ModelProviderProtocols.OpenAI,
                    ApiKey = "sk-test-not-used-for-network",
                    EndPoint = "https://127.0.0.1:9/v1"
                }
            }
        });
}

/// <summary>
/// Exposes the private <see cref="WireClientIntegrationTests"/> server loop for reuse without duplication.
/// </summary>
internal static class WireClientIntegrationTestsRunServerLoop
{
    public static async Task RunAsync(
        IAppServerTransport transport,
        AppServerConnection connection,
        AppServerRequestHandler handler,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            AppServerIncomingMessage? msg;
            try
            {
                msg = await transport.ReadMessageAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (msg == null)
                break;

            if (msg.IsNotification)
            {
                if (msg.Method == AppServerMethods.Initialized)
                    handler.HandleInitializedNotification();
                continue;
            }

            if (!msg.IsRequest)
                continue;

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
                catch (OperationCanceledException)
                {
                    return;
                }
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
}
