using System.Text.Json.Nodes;
using CorePlugins = DotCraft.Plugins;
using DotCraft.Configuration;
using DotCraft.Hooks;
using DotCraft.Mcp;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using DotCraft.Protocol.AppServer;
using DotCraft.Skills;
using DotCraft.Workspaces;

namespace DotCraft.SessionImport.Tests;

public sealed class SetupImportConcurrencyTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task ConcurrentDocumentEditsKeepBothServersAndUnrelatedKeys()
    {
        var path = _temp.Combine("config.json");
        File.WriteAllText(path, "{\"Other\":42}");
        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() =>
            McpScopeStore.Upsert(path, new McpServerConfig { Name = "server-" + index, Command = "test", Enabled = false }))));
        Assert.Equal(12, McpScopeStore.Read(path, "user").Count);
        Assert.Equal(42, AtomicConfigDocument.Read(path)["Other"]!.GetValue<int>());
    }

    [Fact]
    public void McpScopeMutationKeepsTheOtherScopeAndRevealsGlobalAfterRemovingOverride()
    {
        var user = _temp.Combine("user.json");
        var workspace = _temp.Combine("workspace.json");
        McpScopeStore.Upsert(user, new() { Name = "shared", Command = "global", Enabled = false });
        McpScopeStore.Upsert(workspace, new() { Name = "shared", Command = "project", Enabled = false });
        Assert.Equal("project", Assert.Single(McpScopeStore.Effective(workspace, user)).Command);
        Assert.True(McpScopeStore.Remove(workspace, "shared"));
        var effective = Assert.Single(McpScopeStore.Effective(workspace, user));
        Assert.Equal("global", effective.Command);
        Assert.Equal("user", effective.Origin.Kind);
        Assert.False(effective.ReadOnly);
    }

    [Fact]
    public void GlobalSyncHasOneOwnerAcrossWorkspacesAndRespectsInterval()
    {
        var user = _temp.CreateDirectory("user");
        var first = new ImportHistoryStore(user, _temp.CreateDirectory("one"));
        var second = new ImportHistoryStore(user, _temp.CreateDirectory("two"));
        using (var owner = first.BeginGlobalSync(TimeSpan.FromHours(12)))
        {
            Assert.NotNull(owner);
            Assert.Null(second.BeginGlobalSync(TimeSpan.FromHours(12)));
        }
        Assert.Null(second.BeginGlobalSync(TimeSpan.FromHours(12)));
    }

    [Fact]
    public async Task UserRevisionRefreshesBothWorkspaceRuntimesOnce()
    {
        var user = _temp.CreateDirectory("home", ".craft");
        var first = _temp.CreateDirectory("first", ".craft");
        var second = _temp.CreateDirectory("second", ".craft");
        var userConfig = Path.Combine(user, "config.json");
        McpScopeStore.Upsert(userConfig, new() { Name = "imported", Command = "test", Enabled = false });
        AtomicConfigDocument.Update(Path.Combine(user, "imports", "revision.json"), root => root["revision"] = "one");
        foreach (var data in new[] { first, second })
        {
            var monitor = new AppConfigMonitor(new AppConfig { GlobalConfigPath = userConfig });
            var notifications = 0;
            monitor.Changed += (_, _) => notifications++;
            var runtime = new ImportedSetupRuntime(DotCraftPaths.CreateForExecutionHost(Path.GetDirectoryName(data)!, data, user), monitor);
            await runtime.RefreshAsync(default);
            await runtime.RefreshAsync(default);
            Assert.Equal("user", Assert.Single(monitor.Current.McpServers).Origin.Kind);
            Assert.Equal(1, notifications);
        }
    }

    [Fact]
    public async Task UserRevisionRediscoversInstalledAndRemovedUserGlobalPlugins()
    {
        var user = _temp.CreateDirectory("home", ".craft");
        var data = _temp.CreateDirectory("other", ".craft");
        var skills = new SkillsLoader(data);
        var monitor = new AppConfigMonitor(new AppConfig { GlobalConfigPath = Path.Combine(user, "config.json") });
        var runtime = new ImportedSetupRuntime(DotCraftPaths.CreateForExecutionHost(Path.GetDirectoryName(data)!, data, user), monitor, skills);
        var plugin = Path.Combine(user, "plugins", "demo-plugin");
        Directory.CreateDirectory(Path.Combine(plugin, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(plugin, "skills", "demo-skill"));
        File.WriteAllText(Path.Combine(plugin, "skills", "demo-skill", "SKILL.md"), """
            ---
            name: demo-skill
            description: Demo
            ---
            # Demo
            """);
        File.WriteAllText(Path.Combine(plugin, ".craft-plugin", "plugin.json"), """
            {"schemaVersion":1,"id":"demo-plugin","version":"1.0.0","displayName":"Demo","description":"Demo","capabilities":["skill"],"skills":"./skills/"}
            """);

        AtomicConfigDocument.Update(Path.Combine(user, "imports", "revision.json"), root => root["revision"] = "installed");
        await runtime.RefreshAsync(default);
        Assert.Contains(skills.ListSkills(), skill => skill.Name == "demo-skill");

        Directory.Delete(plugin, recursive: true);
        AtomicConfigDocument.Update(Path.Combine(user, "imports", "revision.json"), root => root["revision"] = "removed");
        await runtime.RefreshAsync(default);
        Assert.DoesNotContain(skills.ListSkills(), skill => skill.Name == "demo-skill");
    }

    [Fact]
    public async Task UserRevisionStopsADotnetPluginRemovedByAnotherProcess()
    {
        var user = _temp.CreateDirectory("home", ".craft");
        var data = _temp.CreateDirectory("other", ".craft");
        var dotnet = new RecordingDotnetRuntime("removed-plugin");
        var monitor = new AppConfigMonitor(new AppConfig { GlobalConfigPath = Path.Combine(user, "config.json") });
        var runtime = new ImportedSetupRuntime(
            DotCraftPaths.CreateForExecutionHost(Path.GetDirectoryName(data)!, data, user), monitor, new SkillsLoader(data), dotnet: dotnet);

        AtomicConfigDocument.Update(Path.Combine(user, "imports", "revision.json"), root => root["revision"] = "removed");
        await runtime.RefreshAsync(default);

        Assert.Equal(["quiesce:removed-plugin", "reconcile:removed-plugin"], dotnet.Calls);
    }

    private sealed class RecordingDotnetRuntime(params string[] running) : CorePlugins.IPluginDotnetRuntimeCoordinator
    {
        public List<string> Calls { get; } = [];

        public CorePlugins.PluginRuntimeSnapshot Snapshot { get; } = new(
            1,
            running.Select(id => new CorePlugins.PluginDotnetRuntimeInfo(id, "1.0.0", default, null, [])).ToArray(),
            []);

        public event EventHandler<CorePlugins.PluginRuntimeSnapshotChangedEventArgs>? SnapshotChanged { add { } remove { } }

        public Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<CorePlugins.PluginRuntimeMutationResult> QuiesceForMutationAsync(string pluginId, CancellationToken cancellationToken = default) =>
            Record("quiesce", pluginId);

        public Task<CorePlugins.PluginRuntimeMutationResult> ReconcileAfterMutationAsync(string pluginId, CancellationToken cancellationToken = default) =>
            Record("reconcile", pluginId);

        public Task<CorePlugins.PluginRuntimeMutationResult> TrustAsync(string pluginId, CancellationToken cancellationToken = default) =>
            Record("trust", pluginId);

        public Task<CorePlugins.PluginRuntimeMutationResult> RevokeTrustAsync(string pluginId, CancellationToken cancellationToken = default) =>
            Record("revoke", pluginId);

        private Task<CorePlugins.PluginRuntimeMutationResult> Record(string action, string pluginId)
        {
            Calls.Add($"{action}:{pluginId}");
            return Task.FromResult(new CorePlugins.PluginRuntimeMutationResult(CorePlugins.PluginRuntimeMutationOutcome.Applied, [], []));
        }
    }

    [Fact]
    public void HookAttentionDisappearsAfterTrustingCurrentHash()
    {
        var workspace = _temp.CreateDirectory("workspace");
        var data = _temp.CreateDirectory("workspace", ".craft");
        var user = _temp.CreateDirectory("home", ".craft");
        var hooksPath = Path.Combine(user, "hooks.json");
        File.WriteAllText(hooksPath, "{\"hooks\":{\"SessionStart\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"echo test\"}]}]}}");
        var history = new ImportHistoryStore(user, data);
        history.Save(new ImportCompletedNotification
        {
            ImportId = "import-one", Trigger = "manual", StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow,
            Outcomes = [new ImportOutcome { Source = "claude-code", SourceId = "hooks", Category = "hooks", Scope = "user", TargetPath = hooksPath, Status = "attention" }]
        });
        var service = new SessionImportService(new(workspace, data, new()), new(Path.Combine(user, "config.json"), Path.Combine(data, "config.json")), []);
        Assert.Single(service.ReadAttention(new HashSet<string>()));
        var hooks = new HooksLoader(data, hooksPath).Discover(new AppConfig(), workspace).Hooks;
        var hook = Assert.Single(hooks);
        AtomicConfigDocument.Update(Path.Combine(user, "config.json"), root =>
        {
            var state = AtomicConfigDocument.Object(AtomicConfigDocument.Object(root, "Hooks"), "State");
            state[hook.Key] = new JsonObject { ["TrustedHash"] = hook.CurrentHash };
        });
        Assert.Empty(service.ReadAttention(new HashSet<string>()));
    }
}
