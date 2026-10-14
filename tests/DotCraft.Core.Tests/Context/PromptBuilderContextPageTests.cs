using DotCraft.Context;
using DotCraft.Contributions;
using DotCraft.Memory;
using DotCraft.Sessions;
using DotCraft.Skills;
using Xunit;

namespace DotCraft.Tests.Context;

public sealed class PromptBuilderContextPageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PromptPages_" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _workspace;
    private readonly string _craft;

    public PromptBuilderContextPageTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        _craft = Path.Combine(_workspace, ".craft");
        Directory.CreateDirectory(_craft);
    }

    [Fact]
    public void BuildSystemPrompt_NamesTheThreadStoreAndCarriesNoMemoryContent()
    {
        var manager = new ContextPageManager();
        var workspaceStore = new MemoryStore(_craft);
        var scopedStore = MemoryScopes.ForThread(new ThreadConfiguration { MemoryScope = "agent-a" }, workspaceStore, _craft);
        WriteMemory(workspaceStore, "workspace-fact");
        WriteMemory(scopedStore, "scoped-fact");

        var workspacePrompt = CreateBuilder(workspaceStore, manager).BuildSystemPrompt("thread-workspace");
        var scopedPrompt = CreateBuilder(scopedStore, manager).BuildSystemPrompt("thread-scoped");

        Assert.Contains(Path.GetFullPath(workspaceStore.MemoryDirectoryPath), workspacePrompt, StringComparison.Ordinal);
        Assert.Contains(Path.GetFullPath(scopedStore.MemoryDirectoryPath), scopedPrompt, StringComparison.Ordinal);
        foreach (var prompt in new[] { workspacePrompt, scopedPrompt })
        {
            Assert.DoesNotContain("workspace-fact", prompt, StringComparison.Ordinal);
            Assert.DoesNotContain("scoped-fact", prompt, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BuildSystemPrompt_IsIdenticalForEqualConfigurationWhateverTheMemoryHolds()
    {
        var memoryStore = new MemoryStore(_craft);
        WriteMemory(memoryStore, "first-fact");
        var first = CreateBuilder(memoryStore, new ContextPageManager()).BuildSystemPrompt("thread-a");

        WriteMemory(memoryStore, "second-fact");
        File.WriteAllText(Path.Combine(memoryStore.MemoryDirectoryPath, "extra.md"), "---\ndescription: extra\n---\nmore");
        var second = CreateBuilder(memoryStore, new ContextPageManager()).BuildSystemPrompt("thread-b");

        Assert.Equal(first, second);
    }

    [Fact]
    public void BuildSystemPrompt_KeepsAlwaysSkillStableUntilRelease()
    {
        var manager = new ContextPageManager();
        var memoryStore = new MemoryStore(_craft);
        var skillsLoader = new SkillsLoader(_craft);
        WriteAlwaysSkill("description-v1", "body-v1");
        var builder = CreateBuilder(memoryStore, manager, skillsLoader);

        var first = builder.BuildSystemPrompt("thread-a");
        Assert.Contains("body-v1", first, StringComparison.Ordinal);

        WriteAlwaysSkill("description-v2", "body-v2");
        manager.MarkDirty(ContextPageKeys.SkillsWildcard());

        var pinned = builder.BuildSystemPrompt("thread-a");
        Assert.Contains("body-v1", pinned, StringComparison.Ordinal);
        Assert.DoesNotContain("body-v2", pinned, StringComparison.Ordinal);

        var newThread = builder.BuildSystemPrompt("thread-b");
        Assert.Contains("body-v2", newThread, StringComparison.Ordinal);

        manager.ReleaseStablePages("thread-a");
        var refreshed = builder.BuildSystemPrompt("thread-a");
        Assert.Contains("body-v2", refreshed, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSystemPrompt_WithoutContextPageManager_ReloadsImmediately()
    {
        var builder = CreateBuilder(new MemoryStore(_craft), contextPageManager: null);

        File.WriteAllText(Path.Combine(_craft, "USER.md"), "user-v1");
        Assert.Contains("user-v1", builder.BuildSystemPrompt("thread"), StringComparison.Ordinal);

        File.WriteAllText(Path.Combine(_craft, "USER.md"), "user-v2");
        var refreshed = builder.BuildSystemPrompt("thread");

        Assert.Contains("user-v2", refreshed, StringComparison.Ordinal);
        Assert.DoesNotContain("user-v1", refreshed, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSystemPrompt_IncludesThreadSystemPromptContextProviderAsStablePage()
    {
        var manager = new ContextPageManager();
        var memoryStore = new MemoryStore(_craft);
        var provider = new MutableThreadPromptProvider();
        var registry = new ContributionRegistry();
        SystemPromptSectionCatalog.RegisterBuiltIns(registry);
        registry.Add<IThreadSystemPromptContextProvider>(provider);
        var builder = CreateBuilder(memoryStore, manager, contributions: registry);

        provider.Section = "# App Context\n\ncontext-v1";
        var first = builder.BuildSystemPrompt("thread-a");
        Assert.Contains("context-v1", first, StringComparison.Ordinal);

        provider.Section = "# App Context\n\ncontext-v2";
        var pinned = builder.BuildSystemPrompt("thread-a");
        Assert.Contains("context-v1", pinned, StringComparison.Ordinal);
        Assert.DoesNotContain("context-v2", pinned, StringComparison.Ordinal);

        manager.ReleaseStablePage("thread-a", provider.ContextPageKey);
        var refreshed = builder.BuildSystemPrompt("thread-a");
        Assert.Contains("context-v2", refreshed, StringComparison.Ordinal);
        Assert.DoesNotContain("context-v1", refreshed, StringComparison.Ordinal);
    }

    private PromptBuilder CreateBuilder(
        MemoryStore memoryStore,
        IContextPageManager? contextPageManager,
        SkillsLoader? skillsLoader = null,
        IContributionView? contributions = null) =>
        new(
            memoryStore,
            skillsLoader ?? new SkillsLoader(_craft),
            _craft,
            _workspace,
            toolNamesProvider: () => [],
            contextPageManager: contextPageManager,
            contributions: contributions);

    private static void WriteMemory(MemoryStore store, string fact)
    {
        File.WriteAllText(Path.Combine(store.MemoryDirectoryPath, "fact.md"), $"---\ndescription: {fact}\n---\n{fact}");
        File.WriteAllText(store.IndexFilePath, $"- [Fact](fact.md) — {fact}");
    }

    private void WriteAlwaysSkill(string description, string body)
    {
        var skillDir = Path.Combine(_craft, "skills", "always-skill");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(
            Path.Combine(skillDir, "SKILL.md"),
            $"""
            ---
            name: always-skill
            description: "{description}"
            always: true
            ---

            {body}
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class MutableThreadPromptProvider : IThreadSystemPromptContextProvider
    {
        public ContextPageKey ContextPageKey { get; } = new("test", "threadPrompt", "");

        public string? Section { get; set; }

        public string? GetSystemPromptSection(ThreadSystemPromptContext context) => Section;
    }
}
