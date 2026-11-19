using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.SessionImport.Tests;

public sealed class LocalImportDiscoveryTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _home;
    private readonly string _chat;
    private readonly string _user;
    private readonly string _catalog;
    private readonly IReadOnlyDictionary<string, string> _roots;
    private readonly IReadOnlyList<ISessionImportSource> _sources;

    public LocalImportDiscoveryTests()
    {
        _home = _temp.CreateDirectory("home");
        _user = Path.Combine(_home, ".craft");
        _chat = Path.Combine(_user, "workspaces", "chats");
        _catalog = Path.Combine(_user, "hub", "projects.json");
        _roots = new Dictionary<string, string>
        {
            ["claude-code"] = Path.Combine(_home, ".claude"),
            ["codex"] = Path.Combine(_home, ".codex"),
            ["cursor"] = Path.Combine(_home, ".cursor")
        };
        _sources = [new ClaudeCodeSessionSource(_roots["claude-code"]),
            new CodexSessionSource(_roots["codex"]), new CursorSessionSource(_roots["cursor"])];
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task ScansAcrossProjectsAndDoesNotCreateAnyDestination()
    {
        var parent = _temp.CreateDirectory("repo");
        var child = _temp.CreateDirectory("repo", "package");
        var inner = _temp.CreateDirectory("repo", "package", "src");
        var other = _temp.CreateDirectory("other");
        var newcomer = _temp.CreateDirectory("new");
        var setupOnly = _temp.CreateDirectory("setup-only");
        var missing = _temp.Combine("gone");

        Claude("child", inner);
        Claude("other", other);
        Claude("new", newcomer);
        Claude("missing", missing);
        Claude("home", _home);
        Codex("no-project", null);
        Skill(_roots["claude-code"]);
        Skill(Path.Combine(other, ".claude"));
        Skill(Path.Combine(setupOnly, ".claude"));
        Jsonl.Write(Path.Combine(_home, ".claude.json"),
            new { projects = new Dictionary<string, object> { [setupOnly] = new { } } });

        var result = await Discovery().DetectAsync([parent, child, other]);
        var claude = result.Single(source => source.Source == "claude-code");
        var claudeSessions = claude.Items.Where(item => item.Candidate.Category == "sessions")
            .ToDictionary(item => item.Candidate.SourceId);

        Assert.Equal((child, "project", inner), (claudeSessions["child"].ProjectPath, claudeSessions["child"].Destination, claudeSessions["child"].Candidate.Cwd));
        Assert.Equal(other, claudeSessions["other"].ProjectPath);
        Assert.Equal((newcomer, "newProject"), (claudeSessions["new"].ProjectPath, claudeSessions["new"].Destination));
        Assert.Equal(("unavailable", "import_project_unavailable"), (claudeSessions["missing"].Destination, claudeSessions["missing"].Candidate.Reason));
        Assert.Equal((_chat, "chats", _chat), (claudeSessions["home"].ProjectPath, claudeSessions["home"].Destination, claudeSessions["home"].Candidate.Cwd));
        var projectless = Assert.Single(result.Single(source => source.Source == "codex").Items);
        Assert.Equal((_chat, "chats"), (projectless.ProjectPath, projectless.Destination));

        var skills = claude.Items.Where(item => item.Candidate.Category == "skills").ToArray();
        Assert.Single(skills, item => item.Destination == "personal");
        Assert.Single(skills, item => item.ProjectPath == other && item.Destination == "project");
        Assert.Single(skills, item => item.ProjectPath == setupOnly && item.Destination == "newProject");
        Assert.Equal(skills.Length, skills.Select(item => item.Key).Distinct().Count());
        Assert.All(new[] { parent, child, other, newcomer, setupOnly }, path => Assert.False(Directory.Exists(Path.Combine(path, ".craft"))));
        Assert.False(Directory.Exists(_user));
    }

    [Fact]
    public async Task ScopedPassConsumesNewlyRegisteredAndProjectlessPreviewReferences()
    {
        var newProject = _temp.CreateDirectory("new-project");
        Claude("new", newProject);
        Codex("no-project", null);
        var before = await Discovery().DetectAsync([]);
        var newcomer = Assert.Single(before.Single(source => source.Source == "claude-code").Items);
        var chat = Assert.Single(before.Single(source => source.Source == "codex").Items);

        Register(newProject);
        var projectService = ScopedService(newProject);
        await RunAsync(projectService.Service, newcomer);
        Assert.Equal(newProject, Assert.Single(projectService.Sessions.ImportRequests).Identity.WorkspacePath);

        var chatService = ScopedService(_chat);
        await RunAsync(chatService.Service, chat);
        var chatRequest = Assert.Single(chatService.Sessions.ImportRequests);
        Assert.Equal(_chat, chatRequest.Identity.WorkspacePath);
        Assert.Null(chatRequest.Cwd);
        Assert.All(await Discovery().DetectAsync([newProject]), source => Assert.Equal(0, source.ImportableCount));
    }

    [Fact]
    public async Task RemovingAProjectInvalidatesThePreviewKeyWithoutChangingTheSourceReference()
    {
        var project = _temp.CreateDirectory("project");
        Claude("chat", project);

        var registered = Assert.Single((await Discovery().DetectAsync([project]))
            .Single(source => source.Source == "claude-code").Items);
        var unregistered = Assert.Single((await Discovery().DetectAsync([]))
            .Single(source => source.Source == "claude-code").Items);

        Assert.Equal("project", registered.Destination);
        Assert.Equal((project, "newProject"), (unregistered.ProjectPath, unregistered.Destination));
        Assert.NotEqual(registered.Key, unregistered.Key);
        Assert.Equal((registered.Candidate.SourceId, registered.Candidate.Fingerprint),
            (unregistered.Candidate.SourceId, unregistered.Candidate.Fingerprint));
    }

    [Fact]
    public async Task ExplicitNestedProjectOffersItsOwnSetupWithoutReassigningChats()
    {
        var parent = _temp.CreateDirectory("repo");
        var child = _temp.CreateDirectory("repo", "explicit");
        var incidental = _temp.CreateDirectory("repo", "incidental");
        Jsonl.Write(Path.Combine(_home, ".claude.json"),
            new { projects = new Dictionary<string, object> { [child] = new { } } });
        Skill(Path.Combine(child, ".claude"));
        Skill(Path.Combine(incidental, ".claude"));
        Claude("explicit-chat", child);
        Claude("incidental-chat", incidental);

        var items = (await Discovery().DetectAsync([parent])).Single(source => source.Source == "claude-code").Items;

        var setup = Assert.Single(items, item => item.Candidate.Category == "skills");
        Assert.Equal((child, "newProject"), (setup.ProjectPath, setup.Destination));
        var chats = items.Where(item => item.Candidate.Category == "sessions").ToArray();
        Assert.Equal(["explicit-chat", "incidental-chat"], chats.Select(chat => chat.Candidate.SourceId).Order());
        Assert.All(chats, chat => Assert.Equal(parent, chat.ProjectPath));
    }

    [Fact]
    public async Task PreviewAndScopedDetectionUseEachProjectsEffectiveAgeWindow()
    {
        var shortWindow = _temp.CreateDirectory("short-window");
        var longWindow = _temp.CreateDirectory("long-window");
        Register(shortWindow, longWindow);
        Jsonl.WriteText(Path.Combine(_user, "config.json"), """{"AgentImport":{"MaxSessionAgeDays":7}}""");
        Jsonl.WriteText(Path.Combine(shortWindow, ".craft", "config.json"), """{"AgentImport":{"MaxSessionAgeDays":1}}""");
        Jsonl.WriteText(Path.Combine(longWindow, ".craft", "config.json"), """{"AgentImport":{"MaxSessionAgeDays":21}}""");
        var now = DateTimeOffset.UtcNow;
        Claude("recent", shortWindow, now.AddHours(-1));
        Claude("too-old", shortWindow, now.AddDays(-3));
        Claude("historical", longWindow, now.AddDays(-10));

        var items = (await Discovery().DetectAsync([shortWindow, longWindow])).Single(source => source.Source == "claude-code").Items;

        await AssertSessionIdsAsync(items, shortWindow, "recent");
        await AssertSessionIdsAsync(items, longWindow, "historical");
    }

    [Fact]
    public async Task PreviewAndScopedDetectionApplyEachProjectsCountLimitBeforeMembership()
    {
        var small = _temp.CreateDirectory("small");
        var large = _temp.CreateDirectory("large");
        Register(small, large);
        Jsonl.WriteText(Path.Combine(_user, "config.json"), """{"AgentImport":{"MaxSessionsPerSource":3}}""");
        Jsonl.WriteText(Path.Combine(small, ".craft", "config.json"), """{"AgentImport":{"MaxSessionsPerSource":2}}""");
        Jsonl.WriteText(Path.Combine(large, ".craft", "config.json"), """{"AgentImport":{"MaxSessionsPerSource":5}}""");
        var now = DateTimeOffset.UtcNow;
        Claude("large-new", large, now.AddHours(-1));
        Claude("small-new", small, now.AddHours(-2));
        Claude("small-before-membership", small, now.AddHours(-3));
        Claude("large-older", large, now.AddHours(-4));
        Claude("large-oldest", large, now.AddHours(-5));

        var items = (await Discovery().DetectAsync([small, large])).Single(source => source.Source == "claude-code").Items;

        await AssertSessionIdsAsync(items, small, "small-new");
        await AssertSessionIdsAsync(items, large, "large-new", "large-older", "large-oldest");
    }

    [Fact]
    public async Task ExistingParentImportTakesPrecedenceOverNestedProject()
    {
        var parent = _temp.CreateDirectory("repo");
        var child = _temp.CreateDirectory("repo", "nested");
        var modified = DateTimeOffset.UtcNow.AddHours(-1);
        var sourcePath = Claude("existing", child, modified);
        Register(parent);
        var original = Assert.Single((await Discovery().DetectAsync([parent])).Single(source => source.Source == "claude-code").Items);
        var parentService = ScopedService(parent);
        await RunAsync(parentService.Service, original);
        Assert.Equal(child, Assert.Single(parentService.Sessions.ImportRequests).Cwd);

        Register(parent, child);
        Claude("fresh", child);
        Jsonl.Touch(sourcePath, modified.AddMinutes(10));
        var updated = (await Discovery().DetectAsync([parent, child])).Single(source => source.Source == "claude-code");
        Assert.Contains(updated.Items, candidate => candidate.Candidate.SourceId == "fresh" && candidate.ProjectPath == child);
        Assert.Contains(updated.Items, candidate => candidate.Candidate.SourceId == "existing" && candidate.ProjectPath == parent
            && candidate.Candidate.State == "current");
        var childService = ScopedService(child);
        var detected = Assert.Single((await childService.Service.DetectAsync(null)).Single(source => source.Source == "claude-code").Items);
        Assert.Equal("fresh", detected.SourceId);
    }

    [Fact]
    public async Task RecoversOwnershipFromPersistedThreadAndExcludesDotCraftSpawnedSessions()
    {
        var parent = _temp.CreateDirectory("repo");
        var child = _temp.CreateDirectory("repo", "nested");
        var data = Path.Combine(parent, ".craft");
        var importedAt = DateTimeOffset.UtcNow.AddHours(-1);
        Claude("archived", child);
        Codex("spawned", child);

        await using var store = new ThreadStore(data);
        await store.SaveThreadAsync(new SessionThread
        {
            Id = SessionImportIdentity.ThreadIdFor("claude-code", "archived"),
            WorkspacePath = parent,
            OriginChannel = ThreadImportConstants.ChannelName,
            Status = ThreadStatus.Archived,
            CreatedAt = importedAt,
            LastActiveAt = importedAt,
            Metadata = new Dictionary<string, string>
            {
                [SessionImportIdentity.SourceMetadataKey] = "claude-code",
                [SessionImportIdentity.SessionIdMetadataKey] = "archived",
                [SessionImportIdentity.ImportedAtMetadataKey] = importedAt.ToString("O")
            }
        });
        await store.SaveThreadAsync(new SessionThread
        {
            Id = "thread-parent",
            WorkspacePath = parent,
            OriginChannel = "appserver",
            Status = ThreadStatus.Active,
            CreatedAt = importedAt,
            LastActiveAt = importedAt,
            Metadata = new Dictionary<string, string>
            {
                [SessionImportWorkspaceState.ExternalCliSessionsMetadataKey] = "[{\"sessionId\":\"spawned\"}]"
            }
        });

        var results = await Discovery().DetectAsync([parent, child]);
        var recovered = Assert.Single(results.Single(source => source.Source == "claude-code").Items);
        Assert.Equal((parent, "deferred"), (recovered.ProjectPath, recovered.Candidate.State));
        Assert.Empty(results.Single(source => source.Source == "codex").Items);
        Assert.False(File.Exists(Path.Combine(data, "imports", "sessions.json")));
    }

    [Fact]
    public async Task AmbiguousOrUnknownCursorSlugDoesNotGuessAProject()
    {
        var parent = _temp.CreateDirectory("known");
        var hyphenated = _temp.CreateDirectory("known", "a-b");
        _temp.CreateDirectory("known", "a", "b");
        var resolvable = _temp.CreateDirectory("known", "safe");
        var unregistered = _temp.CreateDirectory("unknown");
        Cursor("ambiguous", hyphenated);
        Cursor("unknown", unregistered);
        Cursor("resolved", resolvable);

        var source = (await Discovery().DetectAsync([parent])).Single(source => source.Source == "cursor");
        var byId = source.Items.ToDictionary(item => item.Candidate.SourceId);
        Assert.Equal("unavailable", byId["ambiguous"].Destination);
        Assert.Equal("unavailable", byId["unknown"].Destination);
        Assert.Equal((parent, resolvable), (byId["resolved"].ProjectPath, byId["resolved"].Candidate.Cwd));
    }

    [Fact]
    public async Task CursorWorkspaceMetadataFindsAnUnregisteredProject()
    {
        var project = _temp.CreateDirectory("cursor project");
        var storage = _temp.CreateDirectory("cursor-workspaces");
        Cursor("cursor-project", project);
        Jsonl.Write(Path.Combine(storage, "known", "workspace.json"), new { folder = new Uri(project).AbsoluteUri });

        var discovered = await new LocalImportDiscovery(_user, _chat, _home, _sources, _roots, storage).DetectAsync([]);
        var item = Assert.Single(discovered.Single(source => source.Source == "cursor").Items);
        Assert.Equal((project, "newProject"), (item.ProjectPath, item.Destination));
    }

    private LocalImportDiscovery Discovery() => new(_user, _chat, _home, _sources, _roots);

    private async Task AssertSessionIdsAsync(IEnumerable<LocalImportItem> items, string path, params string[] expected)
    {
        Assert.Equal(expected.Order(), items.Where(item => item.ProjectPath == path)
            .Select(item => item.Candidate.SourceId).Order());
        var scoped = await ScopedService(path).Service.DetectAsync(["claude-code"]);
        Assert.Equal(expected.Order(), Assert.Single(scoped).Items.Select(item => item.SourceId).Order());
    }

    private (SessionImportService Service, FakeSessionService Sessions) ScopedService(string path)
    {
        Directory.CreateDirectory(path);
        var data = Path.Combine(path, ".craft");
        var userConfig = Path.Combine(_user, "config.json");
        var workspaceConfig = Path.Combine(data, "config.json");
        var settings = new SessionImportSettingsStore(userConfig, workspaceConfig);
        var config = AppConfig.LoadWithGlobalFallback(workspaceConfig, userConfig).GetSection<SessionImportConfig>("AgentImport");
        var service = new SessionImportService(new SessionImportServiceOptions(path, data, config)
        {
            LocalHost = new LocalImportHostOptions(_catalog, _chat, _home, _roots, null)
        }, settings, _sources);
        var sessions = new FakeSessionService();
        service.SetSessionService(sessions);
        return (service, sessions);
    }

    private static async Task RunAsync(SessionImportService service, LocalImportItem item)
    {
        var completion = new TaskCompletionSource<ImportCompletedNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Completed += notification => completion.TrySetResult(notification);
        service.Run([item.Candidate.Source], new ImportSelection { Sessions = true },
            [new ImportItemReference
            {
                Source = item.Candidate.Source, SourceId = item.Candidate.SourceId, Fingerprint = item.Candidate.Fingerprint
            }]);
        var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("imported", Assert.Single(result.Outcomes).Status);
    }

    private void Register(params string[] projects) =>
        Jsonl.WriteText(_catalog, JsonSerializer.Serialize(new { projects = projects.Select(path => new { path }) }));

    private string Claude(string id, string? cwd, DateTimeOffset? modified = null) => Jsonl.WriteText(
        Path.Combine(_roots["claude-code"], "projects", "fixtures", id + ".jsonl"),
        Jsonl.Lines(new { type = "user", cwd, message = new { content = id } }), modified);

    private void Cursor(string id, string cwd) => Jsonl.Write(
        Path.Combine(_roots["cursor"], "projects", WorkspacePathMatcher.CursorSlug(cwd), "agent-transcripts", id + ".jsonl"),
        new { role = "user", message = new { content = new[] { new { type = "text", text = id } } } });

    private void Codex(string id, string? cwd) => Jsonl.Write(
        Path.Combine(_roots["codex"], "sessions", $"rollout-2026-11-19T10-00-00-{id}.jsonl"),
        new { type = "session_meta", payload = new { id, cwd, source = "cli", thread_source = "user" } },
        new { type = "event_msg", payload = new { type = "user_message", message = id } });

    private static void Skill(string configRoot) =>
        Jsonl.WriteText(Path.Combine(configRoot, "skills", "test", "SKILL.md"), "---\nname: test\ndescription: Test skill\n---\n\nContent.\n");
}
