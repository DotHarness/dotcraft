using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Contributions;
using DotCraft.GenerativeUi;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Persistence;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Methods = DotCraft.Protocol.AppServer.AppServerMethodNames;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerDynamicToolLifetimeTests : IDisposable
{
    private const string ToolNamespace = "notes";
    private const string SaveNoteName = "SaveNote";
    private const string FindNoteName = "FindNote";
    private const string DirectTool = ToolNamespace + "__" + SaveNoteName;
    private const string DeferredTool = ToolNamespace + "__" + FindNoteName;
    private const string SearchTool = "SearchTools";
    private readonly string _workspace = Directory.CreateTempSubdirectory("DynamicToolLifetime_").FullName;

    [Fact]
    public async Task DesktopDisconnect_KeepsTheModelToolListOnMobileWithoutRedirectingTheCallback()
    {
        await using var host = new Host(_workspace);
        var desktop = await host.ConnectAsync("dotcraft-desktop", generativeUi: true);
        var threadId = await host.StartThreadAsync(desktop);
        var original = await host.RunTurnAsync(desktop, threadId, DiscoverDeferred(), SaveNote());
        AssertInitialTools(original.Requests[0]);
        Assert.Contains(original.Requests[1], tool => tool.Name == DeferredTool);
        Assert.True(DynamicCall(original, SaveNoteName).GetProperty("success").GetBoolean());

        await host.DisconnectAsync(desktop);
        var phone = await host.ConnectAsync("dotcraft-mobile");
        await phone.RequireResultAsync(Methods.ThreadResume, new { threadId });
        var otherSubscriber = await host.ConnectAsync("dotcraft-desktop");
        await otherSubscriber.RequireResultAsync(Methods.ThreadResume, new { threadId });

        var resumed = await host.RunTurnAsync(phone, threadId, DiscoverDeferred(), SaveNote());
        Assert.Equal(original.Requests[0], resumed.Requests[0]);
        Assert.Equal(original.Requests[1], resumed.Requests[1]);
        AssertDisconnected(DynamicCall(resumed, SaveNoteName));
        Assert.Equal(1, desktop.DynamicCalls);
        Assert.Equal(0, phone.DynamicCalls);
        Assert.Equal(0, otherSubscriber.DynamicCalls);
    }

    [Fact]
    public async Task ColdResume_RestoresDirectAndDeferredDeclarationsWithoutAnExecutor()
    {
        string threadId;
        ToolShape[][] originalRequests;
        await using (var original = new Host(_workspace))
        {
            var desktop = await original.ConnectAsync("dotcraft-desktop");
            threadId = await original.StartThreadAsync(desktop);
            await desktop.RequireResultAsync(Methods.ThreadResume, new
            {
                threadId,
                dynamicTools = Declarations("Save the final note")
            });
            var turn = await original.RunTurnAsync(desktop, threadId, DiscoverDeferred());
            originalRequests = turn.Requests;
            Assert.Equal("Save the final note", Assert.Single(originalRequests[0], tool => tool.Name == DirectTool).Description);
            Assert.Contains(originalRequests[1], tool => tool.Name == DeferredTool);
        }

        await using var restarted = new Host(_workspace);
        var phone = await restarted.ConnectAsync("dotcraft-mobile");
        await phone.RequireResultAsync(Methods.ThreadResume, new { threadId });

        var resumed = await restarted.RunTurnAsync(phone, threadId, DiscoverDeferred(), ToolCall(DeferredTool, "title", "Summary"));
        Assert.Equal(originalRequests[0], resumed.Requests[0]);
        Assert.Equal(originalRequests[1], resumed.Requests[1]);
        AssertDisconnected(DynamicCall(resumed, FindNoteName));
        Assert.Equal(0, phone.DynamicCalls);
    }

    [Fact]
    public async Task Resume_RejectsInvalidReplacementAndOnlyTheCurrentOwnerCanClearDeclarations()
    {
        await using var host = new Host(_workspace);
        var owner = await host.ConnectAsync("dotcraft-desktop");
        var threadId = await host.StartThreadAsync(owner);
        var original = await host.RunTurnAsync(owner, threadId);
        AssertInitialTools(original.Requests[0]);

        var successor = await host.ConnectAsync("dotcraft-desktop");
        await successor.RequireResultAsync(Methods.ThreadResume, new
        {
            threadId,
            dynamicTools = Array.Empty<RuntimeDynamicToolDeclarationSpec>()
        });
        var invalid = await successor.SendAsync(Methods.ThreadResume, new
        {
            threadId,
            dynamicTools = Declarations(string.Empty)
        });
        Assert.Equal(AppServerErrors.InvalidParamsCode, invalid.GetProperty("error").GetProperty("code").GetInt32());

        var unchanged = await host.RunTurnAsync(successor, threadId, SaveNote());
        Assert.Equal(original.Requests[0], unchanged.Requests[0]);
        Assert.True(DynamicCall(unchanged, SaveNoteName).GetProperty("success").GetBoolean());
        Assert.Equal(1, owner.DynamicCalls);
        Assert.Equal(0, successor.DynamicCalls);

        await successor.RequireResultAsync(Methods.ThreadResume, new { threadId, dynamicTools = Declarations() });
        var rebound = await host.RunTurnAsync(owner, threadId, SaveNote());
        Assert.Equal(original.Requests[0], rebound.Requests[0]);
        Assert.True(DynamicCall(rebound, SaveNoteName).GetProperty("success").GetBoolean());
        Assert.Equal(1, owner.DynamicCalls);
        Assert.Equal(1, successor.DynamicCalls);

        await successor.RequireResultAsync(Methods.ThreadResume, new
        {
            threadId,
            dynamicTools = Array.Empty<RuntimeDynamicToolDeclarationSpec>()
        });
        var cleared = await host.RunTurnAsync(successor, threadId);
        Assert.Equal(
            original.Requests[0].Where(tool => tool.Name is not (DirectTool or SearchTool)).ToArray(),
            cleared.Requests[0]);
    }

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    private static RuntimeDynamicToolDeclarationSpec[] Declarations(string directDescription = "Save a note") =>
    [
        new RuntimeDynamicToolNamespaceSpec
        {
            Name = ToolNamespace,
            Description = "Note tools",
            Tools =
            [
                new RuntimeDynamicToolFunctionSpec
                {
                    Name = SaveNoteName,
                    Description = directDescription,
                    InputSchema = StringParameterSchema("text")
                },
                new RuntimeDynamicToolFunctionSpec
                {
                    Name = FindNoteName,
                    Description = "Find a note",
                    InputSchema = StringParameterSchema("title"),
                    DeferLoading = true
                }
            ]
        }
    ];

    private static JsonObject StringParameterSchema(string name) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { [name] = new JsonObject { ["type"] = "string" } },
        ["required"] = new JsonArray(JsonValue.Create(name)),
        ["additionalProperties"] = false
    };

    private static void AssertInitialTools(ToolShape[] tools)
    {
        Assert.Contains(tools, tool => tool.Name == DirectTool);
        Assert.Contains(tools, tool => tool.Name == SearchTool);
        Assert.DoesNotContain(tools, tool => tool.Name == DeferredTool);
    }

    private static FunctionCallContent SaveNote() => ToolCall(DirectTool, "text", "Done.");
    private static FunctionCallContent DiscoverDeferred() => ToolCall(SearchTool, "query", FindNoteName);

    private static FunctionCallContent ToolCall(string name, string parameter, string value) =>
        new("call_" + Guid.NewGuid().ToString("N"), name, new Dictionary<string, object?> { [parameter] = value });

    private static void AssertDisconnected(JsonElement call)
    {
        Assert.Equal("failed", call.GetProperty("status").GetString());
        Assert.False(call.GetProperty("success").GetBoolean());
        Assert.Equal(ToolErrorCodes.DynamicDisconnected, call.GetProperty("errorCode").GetString());
    }

    private static JsonElement DynamicCall(TurnRun turn, string toolName) =>
        Assert.Single(turn.DynamicCalls,
            payload => payload.GetProperty("toolName").GetString() == toolName);

    private sealed record ToolShape(string Name, string Description, string Schema);
    private sealed record TurnRun(ToolShape[][] Requests, IReadOnlyList<JsonElement> DynamicCalls);

    private sealed class Host : IAsyncDisposable
    {
        private readonly string _workspace;
        private readonly string _dataPath;
        private readonly WireDynamicToolProxy _dynamicTools = new();
        private readonly GenerativeUiRuntimeRegistry _generativeUi = new();
        private readonly Model _model = new();
        private readonly AppConfigMonitor _monitor;
        private readonly AgentFactory _factory;
        private readonly WorkspaceStateDatabase _database;
        private readonly ThreadStore _store;
        private readonly SessionService _sessions;
        private readonly HashSet<Client> _clients = [];

        public Host(string workspace)
        {
            _workspace = workspace;
            _dataPath = Directory.CreateDirectory(Path.Combine(workspace, ".craft")).FullName;
            var config = AppConfigTestFactory.CreateOpenAI();
            config.Tools.DeferredLoading.Strategy = AppConfig.DeferredLoadingStrategy.Simulated;
            _monitor = new AppConfigMonitor(config);
            var contributions = new ContributionRegistry();
            _factory = new AgentFactory(_dataPath, workspace, config, new MemoryStore(_dataPath),
                new SkillsLoader(_dataPath), new AutoApproveApprovalService(), null,
                chatClientRegistry: TestModelProviderRegistry.Create(), chatClient: _model, toolSources: [_dynamicTools],
                toolDispatcher: new ToolDispatcher(contributions: contributions));
            _database = new WorkspaceStateDatabase(_dataPath);
            _store = new ThreadStore(_dataPath, _database);
            _sessions = new SessionService(_factory, null, new SessionPersistenceService(_store), new SessionGate(),
                appConfigMonitor: _monitor);
            contributions.Add<IToolInvocationRecorder>(_sessions);
        }

        public async Task<Client> ConnectAsync(string name, bool generativeUi = false)
        {
            var transport = new InMemoryTransport();
            var connection = new AppServerConnection();
            var handler = new AppServerRequestHandler(_sessions, connection, transport,
                new ModuleRegistryChannelListContributor(new ModuleRegistry()),
                new AppServerConnectionServices
                {
                    ServerVersion = "0.0.1-test",
                    HostWorkspacePath = _workspace,
                    WorkspaceCraftPath = _dataPath,
                    AppConfigMonitor = _monitor,
                    WireDynamicToolProxy = _dynamicTools,
                    GenerativeUiRuntimeRegistry = _generativeUi
                });
            var client = new Client(connection, transport, handler);
            _clients.Add(client);
            await client.RequireResultAsync(Methods.Initialize, new
            {
                clientInfo = new { name, version = "0.0.1" },
                capabilities = new { streamingSupport = true, generativeUi }
            });
            Assert.True(handler.HandleNotification(InMemoryTransport.BuildNotification(Methods.Initialized)));
            return client;
        }

        public async Task<string> StartThreadAsync(Client client)
        {
            var result = await client.RequireResultAsync(Methods.ThreadStart, new
            {
                identity = new { workspacePath = _workspace, channelName = "appserver", userId = "local" },
                dynamicTools = Declarations()
            });
            return result.GetProperty("thread").GetProperty("id").GetString()!;
        }

        public async Task<TurnRun> RunTurnAsync(Client caller, string threadId, params FunctionCallContent[] calls)
        {
            var firstRequest = _model.Requests.Count;
            foreach (var call in calls)
                _model.Calls.Enqueue(call);
            var response = await caller.RequireResultAsync(Methods.TurnStart, new
            {
                threadId,
                input = new[] { new { type = "text", text = "Continue the review." } }
            });
            var turnId = response.GetProperty("turn").GetProperty("id").GetString();
            var dynamicCalls = new List<JsonElement>();
            var terminal = await caller.ReadAsync(message =>
            {
                if (!message.TryGetProperty("method", out var method))
                    return false;

                var parameters = message.GetProperty("params");
                if (method.GetString() == Methods.ItemCompleted && parameters.GetProperty("turnId").GetString() == turnId)
                {
                    var item = parameters.GetProperty("item");
                    if (item.GetProperty("payloadKind").GetString() == "dynamicToolCall")
                        dynamicCalls.Add(item.GetProperty("payload").Clone());
                }

                return method.GetString() is Methods.TurnCompleted or Methods.TurnFailed or Methods.TurnCancelled
                    && parameters.GetProperty("turn").GetProperty("id").GetString() == turnId;
            });
            Assert.True(terminal.GetProperty("method").GetString() == Methods.TurnCompleted, terminal.GetRawText());
            var requests = _model.Requests.Skip(firstRequest).ToArray();
            Assert.Equal(calls.Length + 1, requests.Length);
            return new TurnRun(requests, dynamicCalls);
        }

        public async Task DisconnectAsync(Client client)
        {
            _clients.Remove(client);
            client.Connection.MarkClosed();
            _dynamicTools.UnbindTransport(client.Transport);
            foreach (var threadId in _generativeUi.UnbindConnection(client.Connection))
                await _sessions.RefreshThreadAgentAsync(threadId);
            await client.Transport.DisposeAsync();
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var client in _clients.ToArray())
                await DisconnectAsync(client);
            await _factory.DisposeAsync();
            await _store.DisposeAsync();
            _database.Dispose();
        }
    }

    private sealed class Client
    {
        private readonly AppServerRequestHandler _handler;
        private int _requestId;
        private int _dynamicCalls;

        public Client(AppServerConnection connection, InMemoryTransport transport, AppServerRequestHandler handler)
        {
            Connection = connection;
            Transport = transport;
            _handler = handler;
            transport.ApprovalHandler = (method, _) =>
            {
                Assert.Equal(Methods.DynamicToolCall, method);
                Interlocked.Increment(ref _dynamicCalls);
                return InMemoryTransport.BuildClientResponse(1, new
                {
                    success = true,
                    contentItems = new[] { new { type = "text", text = "Saved." } }
                });
            };
        }

        public AppServerConnection Connection { get; }
        public InMemoryTransport Transport { get; }
        public int DynamicCalls => Volatile.Read(ref _dynamicCalls);

        public async Task<JsonElement> RequireResultAsync(string method, object parameters)
        {
            var response = await SendAsync(method, parameters);
            Assert.True(response.TryGetProperty("result", out var result), response.GetRawText());
            return result;
        }

        public async Task<JsonElement> SendAsync(string method, object parameters)
        {
            var id = ++_requestId;
            var request = InMemoryTransport.BuildRequest(method, parameters, id);
            try
            {
                var result = await _handler.HandleRequestAsync(request, CancellationToken.None);
                if (result is not null)
                    await Transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(request.Id, result));
            }
            catch (AppServerException ex)
            {
                await Transport.WriteMessageAsync(AppServerRequestHandler.BuildErrorResponse(request.Id, ex.ToError()));
            }

            return await ReadAsync(message => message.TryGetProperty("id", out var responseId)
                && !message.TryGetProperty("method", out _) && responseId.GetInt32() == id);
        }

        public async Task<JsonElement> ReadAsync(Func<JsonElement, bool> predicate)
        {
            while (true)
            {
                using var message = await Transport.ReadNextSentAsync();
                if (predicate(message.RootElement))
                    return message.RootElement.Clone();
            }
        }
    }

    private sealed class Model : IChatClient
    {
        public ConcurrentQueue<ToolShape[]> Requests { get; } = new();
        public ConcurrentQueue<FunctionCallContent> Calls { get; } = new();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Enqueue((options?.Tools ?? []).Select(tool =>
            {
                var function = Assert.IsAssignableFrom<AIFunctionDeclaration>(tool);
                return new ToolShape(function.Name, function.Description, function.JsonSchema.GetRawText());
            }).ToArray());

            if (Calls.TryDequeue(out var call))
            {
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    FinishReason = ChatFinishReason.ToolCalls,
                    Contents = [call]
                };
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Done.") { FinishReason = ChatFinishReason.Stop };
            }
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
