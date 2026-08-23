using DotCraft.Context;
using DotCraft.Contributions;
using DotCraft.Memory;
using DotCraft.Skills;
using Xunit;

namespace DotCraft.Tests.Context;

public sealed class ChatContextProviderContributionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ChatContribution_" + Guid.NewGuid().ToString("N")[..8]);

    private readonly string _workspace;
    private readonly string _craft;

    public ChatContextProviderContributionTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        _craft = Path.Combine(_workspace, ".craft");
        Directory.CreateDirectory(_craft);
    }

    [Fact]
    public void ChatContextProviders_ContributeToThePromptInRegistrationOrder()
    {
        var registry = CreateRegistry();
        var builder = CreateBuilder(registry);
        var withoutProviders = builder.BuildSystemPrompt("thread-a");

        registry.Add<IChatContextProvider>(new StubChatContextProvider("MARKER-FIRST", "line-one"));
        registry.Add<IChatContextProvider>(new StubChatContextProvider("MARKER-SECOND", "line-two"));

        var prompt = builder.BuildSystemPrompt("thread-a");

        Assert.DoesNotContain("MARKER-FIRST", withoutProviders, StringComparison.Ordinal);
        Assert.True(
            prompt.IndexOf("MARKER-FIRST", StringComparison.Ordinal)
            < prompt.IndexOf("MARKER-SECOND", StringComparison.Ordinal));
    }

    [Fact]
    public void ChatContextProvider_WithoutActiveContext_IsOmitted()
    {
        var registry = CreateRegistry();
        var builder = CreateBuilder(registry);
        var withoutProviders = builder.BuildSystemPrompt("thread-a");

        var handle = registry.Add<IChatContextProvider>(new StubChatContextProvider(section: null, line: null));

        Assert.Equal(withoutProviders, builder.BuildSystemPrompt("thread-a"));

        handle.Dispose();
        Assert.Equal(withoutProviders, builder.BuildSystemPrompt("thread-a"));
    }

    [Fact]
    public void ChatContextProviders_ContributeRuntimeReminderLines()
    {
        var block = RuntimeContextBuilder.BuildBlock(
            workspacePath: Directory.GetCurrentDirectory(),
            chatContextProviders:
            [
                new StubChatContextProvider("section", "line-one"),
                new StubChatContextProvider("section", "line-two")
            ]);

        Assert.Contains("## Additional Runtime Context", block, StringComparison.Ordinal);
        Assert.Contains("line-one", block, StringComparison.Ordinal);
        Assert.Contains("line-two", block, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeReminder_WithoutChatContextProviders_HasNoAdditionalContextSection()
    {
        var block = RuntimeContextBuilder.BuildBlock(workspacePath: Directory.GetCurrentDirectory());

        Assert.DoesNotContain("## Additional Runtime Context", block, StringComparison.Ordinal);
    }

    [Fact]
    public void ThreadPromptProviders_HonorPlacementAndRequireAThread()
    {
        var registry = CreateRegistry();
        var builder = CreateBuilder(registry);
        registry.Add<IThreadSystemPromptContextProvider>(
            new StubThreadPromptProvider("base", ThreadPromptPlacement.BaseInstructions, "MARKER-BASE"));
        registry.Add<IThreadSystemPromptContextProvider>(
            new StubThreadPromptProvider("item", ThreadPromptPlacement.ThreadContextItem, "MARKER-ITEM"));

        var withThread = builder.BuildSystemPrompt("thread-a");
        var withoutThread = builder.BuildSystemPrompt();

        Assert.Contains("MARKER-BASE", withThread, StringComparison.Ordinal);
        // Connection-bound sections are delivered as history items, never in the cached prefix.
        Assert.DoesNotContain("MARKER-ITEM", withThread, StringComparison.Ordinal);
        Assert.DoesNotContain("MARKER-BASE", withoutThread, StringComparison.Ordinal);
    }

    [Fact]
    public void ThreadPromptProviders_ContributedForOneThreadDoNotLeakToAnother()
    {
        var registry = CreateRegistry();
        var builder = CreateBuilder(registry);
        registry.Add<IThreadSystemPromptContextProvider>(
            new StubThreadPromptProvider("scoped", ThreadPromptPlacement.BaseInstructions, "MARKER-SCOPED"),
            ContributionOptions.ForThread("thread-a"));

        Assert.Contains("MARKER-SCOPED", builder.BuildSystemPrompt("thread-a"), StringComparison.Ordinal);
        Assert.DoesNotContain("MARKER-SCOPED", builder.BuildSystemPrompt("thread-b"), StringComparison.Ordinal);
    }

    private static ContributionRegistry CreateRegistry()
    {
        var registry = new ContributionRegistry();
        SystemPromptSectionCatalog.RegisterBuiltIns(registry);
        return registry;
    }

    private PromptBuilder CreateBuilder(IContributionView contributions) =>
        new(
            new MemoryStore(_craft),
            new SkillsLoader(_craft),
            _craft,
            _workspace,
            toolNamesProvider: () => [],
            contributions: contributions);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class StubChatContextProvider(string? section, string? line) : IChatContextProvider
    {
        public string? GetSystemPromptSection() => section;

        public IEnumerable<string> GetRuntimeContextLines() =>
            line is null ? [] : [line];
    }

    private sealed class StubThreadPromptProvider(
        string name,
        ThreadPromptPlacement placement,
        string section) : IThreadSystemPromptContextProvider
    {
        public ContextPageKey ContextPageKey { get; } = new("test", name, "");

        public ThreadPromptPlacement Placement => placement;

        public string GetSystemPromptSection(ThreadSystemPromptContext context) => section;
    }
}
