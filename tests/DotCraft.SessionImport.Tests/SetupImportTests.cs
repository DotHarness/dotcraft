using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Protocol.AppServer;

namespace DotCraft.SessionImport.Tests;

public sealed class SetupImportTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _home;
    private readonly string _workspace;
    private readonly string _user;
    private readonly string _data;
    private readonly Dictionary<string, string> _roots;

    public SetupImportTests()
    {
        _home = _temp.CreateDirectory("home");
        _workspace = _temp.CreateDirectory("repo");
        _user = _temp.CreateDirectory("home", ".craft");
        _data = _temp.CreateDirectory("repo", ".craft");
        _roots = new() { ["claude-code"] = Path.Combine(_home, ".claude"), ["codex"] = Path.Combine(_home, ".codex"), ["cursor"] = Path.Combine(_home, ".cursor") };
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task ImportsBothScopesWithoutChangingSourceAndKeepsCommandsNative()
    {
        Write(_roots["claude-code"], "CLAUDE.md", "Claude Code uses CLAUDE.md; myclaude remains.");
        Write(_workspace, "CLAUDE.md", "claude-code project");
        Write(_roots["claude-code"], "commands/frontend/review.md", "---\ndescription: Claude review\n---\nReview $ARGUMENTS and $1 with Claude.");
        var service = Service();
        var before = await Candidates(service, "claude-code");
        Assert.False(File.Exists(Path.Combine(_user, "AGENTS.md")));
        var completed = await Run(service, "claude-code", before);
        Assert.Equal(3, completed.Outcomes.Count);
        Assert.All(completed.Outcomes, item => Assert.Equal("imported", item.Status));
        Assert.Equal("DotCraft uses AGENTS.md; myclaude remains.", File.ReadAllText(Path.Combine(_user, "AGENTS.md")));
        Assert.Equal("DotCraft project", File.ReadAllText(Path.Combine(_workspace, "AGENTS.md")));
        Assert.StartsWith("Claude Code", File.ReadAllText(Path.Combine(_roots["claude-code"], "CLAUDE.md")));
        var command = new CustomCommandLoader(_data, Path.Combine(_user, "commands")).TryResolve("/frontend:review file.cs");
        Assert.Equal("Review file.cs and file.cs with DotCraft.", command!.ExpandedPrompt);
        Assert.All(await Candidates(service, "claude-code"), item => Assert.Equal("current", item.State));
        Assert.Equal(3, Assert.Single(service.ReadHistory()).Outcomes.Count);
    }

    [Fact]
    public async Task BrandRewritesLeaveCodeAndPathsIntact()
    {
        const string code = "```\nclaude --version\n```";
        Write(_roots["claude-code"], "CLAUDE.md", $"Claude Code runs `claude mcp add` and ~/.claude/hooks/check.sh from @scope/claude-code. Ask Claude.\n{code}");
        var service = Service();
        await Run(service, "claude-code", await Candidates(service, "claude-code"));
        Assert.Equal($"DotCraft runs `claude mcp add` and ~/.claude/hooks/check.sh from @scope/claude-code. Ask DotCraft.\n{code}",
            File.ReadAllText(Path.Combine(_user, "AGENTS.md")));
    }

    [Fact]
    public async Task SourceChangesAfterDetectionRequireNewSelection()
    {
        Write(_roots["cursor"], "commands/review.md", "Cursor reviews");
        var service = Service();
        var detected = await Candidates(service, "cursor");
        Write(_roots["cursor"], "commands/review.md", "Cursor edits");
        var completed = await Run(service, "cursor", detected);
        Assert.Equal("import_source_changed", Assert.Single(completed.Outcomes).ErrorCode.Value);
        Assert.False(File.Exists(Path.Combine(_user, "commands", "review.md")));
    }

    [Theory]
    [InlineData("claude-code", "commands/policy.md", "---\nallowed-tools: Bash\n---\nRun it.")]
    [InlineData("cursor", "commands/dynamic.md", "Execute !`git status`")]
    public async Task UnsupportedCommandsAreNotImported(string source, string path, string text)
    {
        Write(_roots[source], path, text);
        var item = Assert.Single(await Candidates(Service(), source));
        Assert.Equal("unsupported", item.State);
        Assert.False(Directory.Exists(Path.Combine(_user, "commands")));
    }

    [Fact]
    public async Task SharedSkillsAndExistingInstructionsAreNotDuplicated()
    {
        Write(_roots["codex"], "skills/review/SKILL.md", "---\nname: review\ndescription: Review\n---\nCodex review.");
        Write(_home, ".agents/skills/review/SKILL.md", "shared");
        Write(_roots["codex"], "AGENTS.md", "Codex instruction");
        Write(_user, "AGENTS.override.md", "local override");
        Assert.All(await Candidates(Service(), "codex"), item => Assert.Equal("current", item.State));
        Assert.False(Directory.Exists(Path.Combine(_user, "skills")));
    }

    [Fact]
    public async Task McpMergePreservesOtherConfigAndEnvironmentReferences()
    {
        Write(_user, "config.json", "{\"Model\":\"unchanged\",\"McpServers\":{\"existing\":{\"Command\":\"original\"}}}");
        Write(_roots["codex"], "config.toml", "[mcp_servers.example]\ncommand = 'test-server'\nargs = ['--stdio']\nenv_vars = ['DOTCRAFT_TEST_MISSING_ENV_8453']\n[mcp_servers.example.env]\nAUTH_KEY = 'secret-for-fixture-only'\n[mcp_servers.existing]\ncommand = 'replacement'\n");
        var service = Service();
        var detected = await Candidates(service, "codex");
        Assert.DoesNotContain("secret-for-fixture-only", JsonSerializer.Serialize(detected));
        var completed = await Run(service, "codex", detected.Where(i => i.State == "new").ToArray());
        Assert.DoesNotContain("secret-for-fixture-only", JsonSerializer.Serialize(service.ReadHistory()));
        Assert.Equal("attention", Assert.Single(completed.Outcomes).Status);
        Assert.Single(service.ReadAttention(new HashSet<string>()));
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(_user, "config.json")))!;
        Assert.Equal("unchanged", config["Model"]!.GetValue<string>());
        Assert.Equal("original", config["McpServers"]!["existing"]!["Command"]!.GetValue<string>());
        Assert.Equal("DOTCRAFT_TEST_MISSING_ENV_8453", config["McpServers"]!["example"]!["EnvVars"]![0]!.GetValue<string>());
        Assert.False(File.Exists(Path.Combine(_data, "config.json")));
    }

    [Fact]
    public async Task InvalidDestinationIsNeverReplacedWithEmptyConfiguration()
    {
        Write(_roots["cursor"], "mcp.json", "{\"mcpServers\":{\"example\":{\"command\":\"server\"}}}");
        var service = Service();
        var detected = await Candidates(service, "cursor");
        Write(_user, "config.json", "invalid-json");
        await Assert.ThrowsAnyAsync<JsonException>(() => Run(service, "cursor", detected));
        Assert.Equal("invalid-json", File.ReadAllText(Path.Combine(_user, "config.json")));
    }

    [Fact]
    public async Task HooksRequireTrustAndNeverOverwriteExistingHooks()
    {
        Write(_roots["cursor"], "hooks.json", "{\"hooks\":{\"sessionStart\":[{\"command\":\"echo Cursor\",\"timeoutSec\":12}]}}");
        var service = Service();
        var completed = await Run(service, "cursor", await Candidates(service, "cursor"));
        Assert.Equal("import_hooks_need_trust", Assert.Single(completed.Outcomes).ErrorCode.Value);
        var hooks = File.ReadAllText(Path.Combine(_user, "hooks.json"));
        Assert.Contains("SessionStart", hooks);
        Assert.DoesNotContain("TrustedHash", File.ReadAllText(Path.Combine(_user, "config.json")));
        Write(_roots["cursor"], "hooks.json", "{\"hooks\":{\"sessionStart\":[{\"command\":\"new command\"}]}}");
        Assert.Equal("current", Assert.Single(await Candidates(service, "cursor")).State);
        Assert.Equal(hooks, File.ReadAllText(Path.Combine(_user, "hooks.json")));
    }

    [Fact]
    public async Task PluginCommandsRemainOwnedAndDisappearWhenPluginDisabled()
    {
        var plugin = _temp.CreateDirectory("external-plugin");
        Write(plugin, ".claude-plugin/plugin.json", "{\"name\":\"review\",\"version\":\"1.0.0\",\"commands\":\"./commands\"}");
        Write(plugin, "commands/check.md", "Check $ARGUMENTS with Claude");
        Write(_roots["claude-code"], "settings.json", "{\"enabledPlugins\":{\"review@market\":true}}");
        Write(_roots["claude-code"], "plugins/installed_plugins.json", JsonSerializer.Serialize(new
        {
            plugins = new Dictionary<string, object> { ["review@market"] = new[] { new { scope = "user", installPath = plugin } } }
        }));
        var service = Service();
        var completed = await Run(service, "claude-code", await Candidates(service, "claude-code"));
        Assert.Equal("imported", Assert.Single(completed.Outcomes).Status);
        var loader = new CustomCommandLoader(_data, Path.Combine(_user, "commands"));
        Assert.Equal("Check code with DotCraft", loader.TryResolve("/claude-code.review.market:check code")!.ExpandedPrompt);
        AtomicConfigDocument.Update(Path.Combine(_user, "config.json"), root => root["Plugins"] = JsonNode.Parse("{\"DisabledPlugins\":[\"claude-code.review.market\"]}"));
        Assert.Null(loader.TryResolve("/claude-code.review.market:check code"));
        Assert.True(File.Exists(Path.Combine(plugin, ".claude-plugin", "plugin.json")));
    }

    [Fact]
    public async Task SelectionIsSavedAndDeletingDestinationAllowsReimport()
    {
        Write(_roots["cursor"], "commands/check.md", "Cursor check");
        var service = Service();
        await Run(service, "cursor", await Candidates(service, "cursor"));
        Assert.Contains("commands", service.GetSettings().Selection.User);
        service.UpdateSettings(false, null);
        Assert.Contains("commands", service.GetSettings().Selection.User);
        File.Delete(Path.Combine(_user, "commands", "check.md"));
        Assert.Equal("new", Assert.Single(await Candidates(service, "cursor")).State);
    }

    private SessionImportService Service()
    {
        var service = new SessionImportService(new(_workspace, _data, new()),
            new(Path.Combine(_user, "config.json"), Path.Combine(_data, "config.json")),
            _roots.Keys.Select(source => new FakeImportSource(source)), setup: new(new(_workspace, _data, _user, _home, _roots)));
        service.SetSessionService(new FakeSessionService());
        return service;
    }

    private static async Task<ImportCandidate[]> Candidates(SessionImportService service, string source) =>
        (await service.DetectAsync([source])).Single().Items.ToArray();

    private static async Task<ImportCompletedNotification> Run(SessionImportService service, string source, IReadOnlyList<ImportCandidate> items)
    {
        var completion = new TaskCompletionSource<ImportCompletedNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Complete(ImportCompletedNotification result) => completion.TrySetResult(result);
        service.Completed += Complete;
        try
        {
            service.Run([source], new ImportSelection
            {
                User = items.Where(i => i.Scope == "user").Select(i => i.Category).Distinct().ToArray(),
                Workspace = items.Where(i => i.Scope == "workspace").Select(i => i.Category).Distinct().ToArray()
            }, items.Select(i => new ImportItemReference { Source = i.Source, SourceId = i.SourceId, Fingerprint = i.Fingerprint }).ToArray());
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { service.Completed -= Complete; }
    }

    private static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
