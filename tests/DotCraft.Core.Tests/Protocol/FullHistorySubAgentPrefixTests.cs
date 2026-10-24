using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class FullHistorySubAgentPrefixTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dotcraft-fork-prefix-{Guid.NewGuid():N}");
    private readonly string _workspace;

    public FullHistorySubAgentPrefixTests()
    {
        _workspace = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        Directory.CreateDirectory(Path.Combine(_workspace, ".git"));
    }

    [Fact]
    public async Task FullHistoryChild_FirstRequestExtendsParentRequestPrefix()
    {
        var client = new RecordingChatClient();
        client.Respond = request => Task.FromResult<IReadOnlyList<AIContent>>(
            client.Requests.Count == 1 ? [new FunctionCallContent("call-lookup", "LookUp")] : [new TextContent("ok")]);
        await using var factory = CreateFactory(client);
        var service = CreateService(factory);
        var parent = await CreateParentAsync(service);
        await foreach (var _ in service.SubmitInputAsync(parent.Id, [new TextContent("parent request")]))
        {
        }

        var parentRequest = client.Requests[^1];
        await SubAgentSessionControl.SpawnAgentAsync(
            new SubAgentSessionContext
            {
                SessionService = service,
                ParentThread = parent,
                ParentTurnId = parent.Turns[^1].Id,
                RootThreadId = parent.Id,
                ParentModelHistory = parentRequest.Messages
            },
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect the change",
                TaskName = "inspect",
                ForkTurns = "all",
                MaxDepth = 1
            },
            waitForCompletion: false,
            coordinator: null,
            CancellationToken.None);
        await WaitUntilAsync(() => client.Requests.Count > 2);
        var childRequest = client.Requests[2];

        Assert.Contains(parentRequest.Messages, static message => message.Role == ChatRole.Tool);
        Assert.Contains(parentRequest.Tools, static tool => tool.StartsWith($"{nameof(UserCoordinationTools.SendUserMessageAsync)}:", StringComparison.Ordinal));
        Assert.Equal(parentRequest.Tools, childRequest.Tools);
        Assert.Equal(parentRequest.Instructions, childRequest.Instructions);
        Assert.True(childRequest.Messages.Count > parentRequest.Messages.Count);
        Assert.Equal(
            parentRequest.Messages.Select(Describe),
            childRequest.Messages.Take(parentRequest.Messages.Count).Select(Describe));
    }

    [Fact]
    public async Task ChildSpawnedDuringParentTurn_CannotMoveTheParentWorktree()
    {
        var client = new RecordingChatClient();
        await using var factory = CreateFactory(client);
        var service = CreateService(factory);
        var parent = await CreateParentAsync(service);
        client.Respond = async request =>
        {
            if (request.ThreadId == parent.Id)
            {
                if (client.Requests.Count == 1)
                {
                    await SubAgentSessionControl.SpawnAgentAsync(
                        SubAgentSessionScope.Current!,
                        new SubAgentSpawnOptions
                        {
                            AgentPrompt = "move into a worktree",
                            TaskName = "mover",
                            ForkTurns = "all",
                            MaxDepth = 1
                        },
                        waitForCompletion: false,
                        coordinator: null,
                        CancellationToken.None);
                }
                return [new TextContent("ok")];
            }
            return WorktreeResult(request) is null
                ? [new FunctionCallContent("call-worktree", WorktreeToolNames.EnterWorktree, new Dictionary<string, object?> { ["name"] = "child" })]
                : [new TextContent("done")];
        };

        await foreach (var _ in service.SubmitInputAsync(parent.Id, [new TextContent("parent request")]))
        {
        }
        await WaitUntilAsync(() => client.Requests.Any(request => WorktreeResult(request) is not null));

        var result = client.Requests.Select(WorktreeResult).First(static result => result is not null);
        Assert.Contains("top-level thread", result, StringComparison.Ordinal);
        Assert.Null((await service.GetThreadAsync(parent.Id)).Worktree);
    }

    private static string? WorktreeResult(RecordedRequest request) => request.Messages
        .SelectMany(static message => message.Contents)
        .OfType<FunctionResultContent>()
        .FirstOrDefault(static result => result.CallId == "call-worktree")
        ?.Result?.ToString();

    private SessionService CreateService(AgentFactory factory) => new(
        factory,
        defaultAgent: null,
        new SessionPersistenceService(new ThreadStore(Path.Combine(_workspace, ".craft"))),
        new SessionGate());

    private Task<SessionThread> CreateParentAsync(SessionService service) => service.CreateThreadAsync(new SessionIdentity
    {
        ChannelName = "test",
        UserId = "user",
        WorkspacePath = _workspace
    }, new ThreadConfiguration
    {
        SubAgentModelCatalogSnapshot = new SubAgentModelCatalogSnapshot
        {
            ProviderId = "openai",
            Models = [new SubAgentModelCatalogItem { Id = "gpt-test" }]
        }
    });

    private AgentFactory CreateFactory(IChatClient client)
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        return new AgentFactory(
            dotcraftPath: Path.Combine(_root, "craft"),
            workspacePath: _workspace,
            config: config,
            memoryStore: new MemoryStore(Path.Combine(_root, "craft")),
            skillsLoader: new SkillsLoader(Path.Combine(_root, "craft")),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            chatClientRegistry: new ChatClientRegistry(new RecordingProvider(client)),
            toolSources: [new WorktreeToolSource()]);
    }

    private static string Describe(ChatMessage message) =>
        $"{message.Role}:{JsonSerializer.Serialize(message.Contents, AIJsonUtilities.DefaultOptions)}";

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the child request.");
            await Task.Delay(20);
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    private sealed record RecordedRequest(
        string? ThreadId,
        IReadOnlyList<ChatMessage> Messages,
        IReadOnlyList<string> Tools,
        string? Instructions);

    private sealed class RecordingProvider(IChatClient client) : IModelProvider
    {
        public IReadOnlyCollection<string> Protocols => [ModelProviderProtocols.OpenAI];

        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => client;
    }

    private sealed class RecordingChatClient : IChatClient
    {
        private readonly object _gate = new();
        private readonly List<RecordedRequest> _requests = [];

        public IReadOnlyList<RecordedRequest> Requests
        {
            get { lock (_gate) return _requests.ToArray(); }
        }

        public Func<RecordedRequest, Task<IReadOnlyList<AIContent>>> Respond { get; set; } =
            static _ => Task.FromResult<IReadOnlyList<AIContent>>([new TextContent("ok")]);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Record(chatMessages, options);
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var contents = await Respond(Record(chatMessages, options));
            yield return new ChatResponseUpdate(ChatRole.Assistant, [.. contents]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private RecordedRequest Record(IEnumerable<ChatMessage> chatMessages, ChatOptions? options)
        {
            var tools = (options?.Tools ?? [])
                .Select(static tool => tool is AIFunctionDeclaration function
                    ? $"{function.Name}:{function.Description}:{function.JsonSchema.GetRawText()}"
                    : tool.Name)
                .ToArray();
            var request = new RecordedRequest(
                ToolExecutionRuntimeScope.Current?.ThreadId,
                chatMessages.Select(static message => message.Clone()).ToList(),
                tools,
                options?.Instructions);
            lock (_gate)
                _requests.Add(request);
            return request;
        }
    }
}
