using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Context.WorldState;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SkillCatalogWorldStateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dotcraft-skill-catalog-{Guid.NewGuid():N}");
    private readonly string _workspace;
    private readonly SkillsLoader _skills;
    private readonly SessionPersistenceService _persistence;

    public SkillCatalogWorldStateTests()
    {
        _workspace = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        _skills = new SkillsLoader(Path.Combine(_root, "craft"));
        _persistence = new SessionPersistenceService(new ThreadStore(Path.Combine(_root, "data")));
    }

    [Fact]
    public async Task ASkillInstalledBetweenTurnsIsAppendedForTheNextTurn()
    {
        WriteSkill("code-review");
        var recorder = new RecordingChatClient();
        await using var factory = CreateAgentFactory();
        var service = new SessionService(factory, recorder.AsAIAgent(), _persistence, new SessionGate());
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "user",
            WorkspacePath = _workspace
        });

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("first")]));
        var firstList = Assert.Single(SkillItems(recorder.LastMessages));
        Assert.DoesNotContain("release-notes", firstList, StringComparison.Ordinal);

        WriteSkill("release-notes");
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("second")]));

        var lists = SkillItems(recorder.LastMessages);
        Assert.Equal(2, lists.Count);
        Assert.Equal(firstList, lists[0]);
        Assert.Contains("<name>release-notes</name>", lists[1], StringComparison.Ordinal);
    }

    private static List<string> SkillItems(IEnumerable<ChatMessage> messages) =>
        messages
            .Where(message => ThreadContextItems.IsKind(message, WorldStateComposer.ItemKind(SkillsSection.SectionId)))
            .Select(message => message.Text)
            .ToList();

    private void WriteSkill(string name)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_skills.WorkspaceSkillsPath, name)).FullName;
        File.WriteAllText(Path.Combine(directory, "SKILL.md"), $"---\nname: {name}\ndescription: {name} skill\n---\nBody");
    }

    private AgentFactory CreateAgentFactory()
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        config.GlobalConfigPath = Path.Combine(_root, "user", ".craft", "config.json");
        return new AgentFactory(
            dotcraftPath: Path.Combine(_root, "craft"),
            workspacePath: _workspace,
            config: config,
            memoryStore: new MemoryStore(Path.Combine(_root, "craft")),
            skillsLoader: _skills,
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            toolSources: Array.Empty<IToolSource>());
    }

    private static async Task DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = chatMessages.ToList();
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = chatMessages.ToList();
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
