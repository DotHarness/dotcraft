using DotCraft.Agents;
using DotCraft.Commands.Core;
using DotCraft.Configuration;
using DotCraft.GeneratedTools.Core;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Commands;

internal sealed class CommandSessionHarness : IAsyncDisposable
{
    public const string StartShellPrompt = "start shell";

    private readonly string _root;
    private readonly AgentFactory _agentFactory;
    private readonly CommandRegistry _commands = CommandRegistry.CreateDefault(".craft");
    private readonly ModelProviderRegistry _models;

    private CommandSessionHarness(
        string root,
        SessionService service,
        BackgroundTerminalService terminals,
        AgentFactory agentFactory,
        AppConfig config,
        ModelProviderRegistry models,
        SessionIdentity identity)
    {
        _root = root;
        Service = service;
        Terminals = terminals;
        _agentFactory = agentFactory;
        Config = config;
        _models = models;
        Identity = identity;
    }

    public SessionService Service { get; }

    public BackgroundTerminalService Terminals { get; }

    public AppConfig Config { get; }

    public SessionIdentity Identity { get; }

    public static CommandSessionHarness Create(params string[] catalogModels)
    {
        var root = Path.Combine(Directory.GetCurrentDirectory(), "TestArtifacts", "Commands_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var terminals = new BackgroundTerminalService(root, new AppConfig.ShellBackgroundConfig());
        var config = AppConfigTestFactory.CreateOpenAI(model: "model-a");
        var chatClient = new ScriptedChatClient();
        var agentFactory = new AgentFactory(
            dotcraftPath: root,
            workspacePath: root,
            config: config,
            memoryStore: new MemoryStore(root),
            skillsLoader: new SkillsLoader(root),
            approvalService: new SessionScopedApprovalService(new AutoApproveApprovalService()),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            chatClient: chatClient,
            toolDispatcher: new ToolDispatcher(),
            toolSources: [new ShellToolSource(new ShellTools(root, terminals, timeoutSeconds: 30))]);
        var service = new SessionService(
            agentFactory,
            new StreamingFunctionInvokingChatClient(chatClient).AsAIAgent(),
            new SessionPersistenceService(new ThreadStore(root)),
            new SessionGate(),
            backgroundTerminalService: terminals);
        service.AttachTerminalNotices();
        var identity = new SessionIdentity
        {
            ChannelName = "chat",
            UserId = "user-1",
            ChannelContext = "group:1",
            WorkspacePath = root
        };
        return new CommandSessionHarness(
            root,
            service,
            terminals,
            agentFactory,
            config,
            new ModelProviderRegistry([new CatalogProvider(catalogModels)]),
            identity);
    }

    public async Task<SessionThread> CreateThreadAsync()
    {
        var thread = await Service.CreateThreadAsync(Identity);
        await Service.RefreshThreadAgentAsync(thread.Id);
        return thread;
    }

    public async Task RunTurnAsync(string threadId, string text)
    {
        await foreach (var _ in Service.SubmitInputAsync(threadId, [new TextContent(text)]))
        {
        }
    }

    public async Task<SessionTurn> StartBlockedTurnAsync(string threadId)
    {
        _ = Task.Run(() => RunTurnAsync(threadId, "keep working"));
        var thread = await WaitForAsync(threadId, static thread =>
            thread.Turns.LastOrDefault()?.Status == TurnStatus.Running);
        return thread.Turns[^1];
    }

    public async Task<BackgroundTerminalSnapshot> StartBackgroundShellAsync(string threadId)
    {
        await RunTurnAsync(threadId, StartShellPrompt);
        return Assert.Single(await Terminals.ListAsync(threadId), static terminal => terminal.Status == BackgroundTerminalStatus.Running);
    }

    public Task<CommandResult> ExecuteAsync(string threadId, string rawText, bool isAdmin = false) =>
        _commands.TryExecuteAsync(
            rawText,
            new CommandContext
            {
                SessionId = threadId,
                RawText = rawText,
                IsAdmin = isAdmin,
                SessionService = Service,
                CommandRegistry = _commands,
                Config = Config,
                ModelProviders = _models
            },
            new NullResponder());

    public async Task<SessionThread> WaitForAsync(string threadId, Func<SessionThread, bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (true)
        {
            var thread = await Service.GetThreadAsync(threadId);
            if (condition(thread))
                return thread;
            Assert.True(DateTimeOffset.UtcNow < deadline, "The thread did not reach the expected state.");
            await Task.Delay(50);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Service.DrainTerminalNoticesAsync();
        await Terminals.DisposeAsync();
        await _agentFactory.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static string LongRunningCommand() =>
        OperatingSystem.IsWindows() ? "Start-Sleep -Seconds 60" : "sleep 60";

    private sealed class NullResponder : ICommandResponder
    {
        public Task SendTextAsync(string message) => Task.CompletedTask;

        public Task SendMarkdownAsync(string markdown) => Task.CompletedTask;
    }

    private sealed class ShellToolSource(ShellTools shellTools) : AIFunctionToolSource
    {
        public override string SourceId => "command-test";

        protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
        {
            yield return GeneratedToolFunctions.ShellTools_Exec(shellTools);
        }
    }

    private sealed class ScriptedChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "unused")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var last = chatMessages.Last();
            if (last.Role == ChatRole.Tool)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "started");
                yield break;
            }

            if (last.Text.Contains(StartShellPrompt, StringComparison.Ordinal))
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant,
                [
                    new FunctionCallContent("call-shell", "Exec", new Dictionary<string, object?>
                    {
                        ["command"] = LongRunningCommand(),
                        ["runInBackground"] = true,
                        ["yieldTimeMs"] = 100
                    })
                ]);
                yield break;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class CatalogProvider(string[] models) : IModelProvider, IModelCatalogProvider
    {
        public IReadOnlyCollection<string> Protocols => [ModelProviderProtocols.OpenAI];

        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => throw new NotSupportedException();

        public Task<ModelCatalogResult> FetchModelsAsync(EffectiveModelRuntime runtime, CancellationToken cancellationToken) =>
            Task.FromResult(new ModelCatalogResult
            {
                Success = true,
                Models = models.Select(static id => new ModelCatalogEntry { Id = id }).ToList()
            });
    }
}
