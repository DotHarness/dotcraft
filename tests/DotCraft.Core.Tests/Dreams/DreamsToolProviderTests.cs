using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Dreams;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace DotCraft.Tests.Dreams;

public sealed class DreamsToolProviderTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;
    private readonly string _craft;
    private readonly ThreadStore _threadStore;
    private readonly MemoryStore _memoryStore;
    private readonly DreamStore _dreamStore;
    private readonly DreamsRunRegistry _registry;

    public DreamsToolProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "DreamsTools_" + Guid.NewGuid().ToString("N")[..8]);
        _workspace = Path.Combine(_root, "workspace");
        _craft = Path.Combine(_workspace, ".craft");
        Directory.CreateDirectory(_craft);
        _threadStore = new ThreadStore(_craft);
        _memoryStore = new MemoryStore(_craft);
        _dreamStore = new DreamStore(_craft);
        _registry = new DreamsRunRegistry(_threadStore, _memoryStore, _dreamStore);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task DreamsFileTools_ReadSearchInputSnapshotsAndWriteOnlyOutputStore()
    {
        await SaveThreadAsync("thread_source", "Use typed clients for protocol work.");
        _memoryStore.WriteLongTerm("# Memory\nPrefer focused tests.");
        _dreamStore.SaveDreamRun(
            "# Dream Memory\n\n- See memory/protocol.md",
            [new DreamTopicFileWrite { Path = "protocol.md", Content = "# Protocol\nUse typed clients." }],
            null,
            null);

        var input = new DreamsRunInput(
            "memory preview",
            "# Dream Memory\n\n- preview",
            _dreamStore.ListTopicFiles(),
            [
                new DreamsThreadInput(
                    "thread_source",
                    "Protocol work",
                    "desktop",
                    ThreadStatus.Active,
                    DateTimeOffset.UtcNow.AddMinutes(-5),
                    DateTimeOffset.UtcNow,
                    1)
            ],
            1);
        var outputStore = _dreamStore.CreateOutputStore("dream_run", DateTimeOffset.UtcNow);
        var workspace = await _registry.PrepareRunWorkspaceAsync("dream_run", input, outputStore, _workspace);
        _registry.Register("thread_dream", workspace, input);

        var tools = new DreamsToolProvider(_registry).CreateTools(CreateContext("thread_dream")).ToList();

        var manifest = await InvokeToolAsync(tools, "ReadFile", new()
        {
            ["path"] = workspace.ManifestPath
        });
        Assert.Contains("sessions/thread_source.md", manifest, StringComparison.Ordinal);
        Assert.Contains("active-dream-store/memory/*.md", manifest, StringComparison.Ordinal);

        var search = await InvokeToolAsync(tools, "GrepFiles", new()
        {
            ["pattern"] = "typed clients",
            ["path"] = workspace.InputPath
        });
        Assert.Contains("thread_source.md", search, StringComparison.Ordinal);
        Assert.Contains("protocol.md", search, StringComparison.Ordinal);

        var read = await InvokeToolAsync(tools, "ReadFile", new()
        {
            ["path"] = Path.Combine(workspace.InputPath, "sessions", "thread_source.md"),
            ["offset"] = 1,
            ["limit"] = 40
        });
        Assert.Contains("Use typed clients for protocol work.", read, StringComparison.Ordinal);

        var write = await InvokeToolAsync(tools, "WriteFile", new()
        {
            ["path"] = "INDEX.md",
            ["content"] = "# Dream Memory\n\n- pending candidate"
        });
        Assert.Contains("Successfully wrote", write, StringComparison.Ordinal);
        Assert.Contains("pending candidate", File.ReadAllText(outputStore.IndexPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DreamsFileTools_RejectWritesOutsideOutputStore()
    {
        var input = new DreamsRunInput("", "", [], [], 0);
        var outputStore = _dreamStore.CreateOutputStore("dream_run", DateTimeOffset.UtcNow);
        var workspace = await _registry.PrepareRunWorkspaceAsync("dream_run", input, outputStore, _workspace);
        _registry.Register("thread_dream", workspace, input);

        var tools = new DreamsToolProvider(_registry).CreateTools(CreateContext("thread_dream")).ToList();

        var result = await InvokeToolAsync(tools, "WriteFile", new()
        {
            ["path"] = Path.Combine(_workspace, "should-not-write.md"),
            ["content"] = "bad"
        });

        Assert.Contains("outside workspace boundary", result, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(_workspace, "should-not-write.md")));
    }

    private async Task SaveThreadAsync(string id, string userText)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new SessionItem
        {
            Id = "item_user",
            TurnId = "turn_001",
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            Payload = new UserMessagePayload { Text = userText },
            CreatedAt = now
        };
        var assistant = new SessionItem
        {
            Id = "item_agent",
            TurnId = "turn_001",
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            Payload = new AgentMessagePayload { Text = "Acknowledged." },
            CreatedAt = now
        };

        await _threadStore.SaveThreadAsync(new SessionThread
        {
            Id = id,
            WorkspacePath = _workspace,
            OriginChannel = "desktop",
            DisplayName = "Protocol work",
            Source = ThreadSource.User(),
            Status = ThreadStatus.Active,
            CreatedAt = now.AddMinutes(-5),
            LastActiveAt = now,
            HistoryMode = HistoryMode.Server,
            Turns =
            [
                new SessionTurn
                {
                    Id = "turn_001",
                    ThreadId = id,
                    Status = TurnStatus.Completed,
                    Input = user,
                    Items = [user, assistant],
                    StartedAt = now.AddMinutes(-1),
                    CompletedAt = now
                }
            ]
        });
    }

    private ToolProviderContext CreateContext(string currentThreadId) =>
        new()
        {
            Config = new AppConfig(),
            ChatClient = null!,
            WorkspacePath = _workspace,
            BotPath = _craft,
            MemoryStore = _memoryStore,
            DreamStore = _dreamStore,
            SkillsLoader = new SkillsLoader(_craft),
            ApprovalService = new AutoApproveApprovalService(),
            PathBlacklist = new PathBlacklist([]),
            CurrentThreadId = currentThreadId
        };

    private static async Task<string> InvokeToolAsync(
        IReadOnlyList<AITool> tools,
        string name,
        Dictionary<string, object?>? args = null)
    {
        var tool = Assert.IsAssignableFrom<AIFunction>(tools.Single(t => t.Name == name));
        var result = await tool.InvokeAsync(new AIFunctionArguments(args ?? []));
        if (result is string text)
            return text;
        if (result is IList<AIContent> contents)
            return string.Join("\n", contents.OfType<TextContent>().Select(content => content.Text));

        var json = Assert.IsType<JsonElement>(result);
        return json.ValueKind == JsonValueKind.String ? json.GetString()! : json.GetRawText();
    }
}
