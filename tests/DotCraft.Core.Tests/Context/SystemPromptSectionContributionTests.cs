using DotCraft.Context;
using DotCraft.Contributions;
using DotCraft.Memory;
using DotCraft.Skills;
using Xunit;

namespace DotCraft.Tests.Context;

public sealed class SystemPromptSectionContributionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "PromptContribution_" + Guid.NewGuid().ToString("N")[..8]);

    private readonly string _workspace;
    private readonly string _craft;

    public SystemPromptSectionContributionTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        _craft = Path.Combine(_workspace, ".craft");
        Directory.CreateDirectory(_craft);
    }

    [Fact]
    public void Sections_AreOrderedByAscendingOrderAndInterleaveWithBuiltIns()
    {
        var registry = CreateRegistry();
        // Straddle the built-in identity section, which is registered at order 100.
        registry.Add<ISystemPromptSection>(new MarkerSection("before", "MARKER-BEFORE"), new ContributionOptions(Order: 50));
        registry.Add<ISystemPromptSection>(new MarkerSection("after", "MARKER-AFTER"), new ContributionOptions(Order: 150));
        registry.Add<ISystemPromptSection>(new MarkerSection("last", "MARKER-LAST"), new ContributionOptions(Order: 5000));
        registry.Add<ISystemPromptSection>(new MarkerSection("identity-swap", "MARKER-IDENTITY"),
            new ContributionOptions(Order: 900, ReplaceTarget: SystemPromptSectionNames.Identity));

        var prompt = CreateBuilder(registry).BuildSystemPrompt("thread-a");

        AssertOrder(prompt, "MARKER-BEFORE", "MARKER-AFTER", "MARKER-IDENTITY", "MARKER-LAST");
    }

    [Fact]
    public void Sections_WithEqualOrder_KeepRegistrationOrder()
    {
        var registry = CreateRegistry();
        registry.Add<ISystemPromptSection>(new MarkerSection("one", "MARKER-ONE"), new ContributionOptions(Order: 50));
        registry.Add<ISystemPromptSection>(new MarkerSection("two", "MARKER-TWO"), new ContributionOptions(Order: 50));

        var prompt = CreateBuilder(registry).BuildSystemPrompt();

        AssertOrder(prompt, "MARKER-ONE", "MARKER-TWO");
    }

    [Fact]
    public void Replacement_ShadowsTheNamedDefaultAndRestoresItOnDispose()
    {
        var registry = CreateRegistry();
        var builder = CreateBuilder(registry);
        var withDefault = builder.BuildSystemPrompt("thread-a");

        var handle = registry.Add<ISystemPromptSection>(
            new MarkerSection("identity-swap", "MARKER-IDENTITY"),
            new ContributionOptions(Order: 100, ReplaceTarget: SystemPromptSectionNames.Identity));

        var replaced = builder.BuildSystemPrompt("thread-a");
        Assert.Contains("MARKER-IDENTITY", replaced, StringComparison.Ordinal);
        Assert.NotEqual(withDefault, replaced);

        handle.Dispose();

        Assert.Equal(withDefault, builder.BuildSystemPrompt("thread-a"));
    }

    [Fact]
    public void Replacement_ProducingNoContent_SuppressesTheTargetSection()
    {
        var registry = CreateRegistry();
        var builder = CreateBuilder(registry);
        var withDefault = builder.BuildSystemPrompt("thread-a");

        registry.Add<ISystemPromptSection>(
            new MarkerSection("identity-suppressor", content: null),
            new ContributionOptions(Order: 100, ReplaceTarget: SystemPromptSectionNames.Identity));

        var suppressed = builder.BuildSystemPrompt("thread-a");

        // Identity is the first section, so suppressing it leaves exactly the remaining tail.
        Assert.True(suppressed.Length < withDefault.Length);
        Assert.EndsWith(suppressed, withDefault, StringComparison.Ordinal);
    }

    [Fact]
    public void ThreadScopedSection_AppliesToThatThreadOnly()
    {
        var registry = CreateRegistry();
        var builder = CreateBuilder(registry);
        registry.Add<ISystemPromptSection>(
            new MarkerSection("scoped", "MARKER-SCOPED"),
            ContributionOptions.ForThread("thread-a", order: 50));

        Assert.Contains("MARKER-SCOPED", builder.BuildSystemPrompt("thread-a"), StringComparison.Ordinal);
        Assert.DoesNotContain("MARKER-SCOPED", builder.BuildSystemPrompt("thread-b"), StringComparison.Ordinal);
        Assert.DoesNotContain("MARKER-SCOPED", builder.BuildSystemPrompt(), StringComparison.Ordinal);
    }

    [Fact]
    public void Assembler_ReceivesTheDefaultAssemblyAndOnlyTheLastOneRuns()
    {
        var registry = CreateRegistry();
        var builder = CreateBuilder(registry);
        var withDefault = builder.BuildSystemPrompt("thread-a");

        var inner = new RecordingAssembler("INNER");
        var outer = new RecordingAssembler("OUTER");
        registry.Add<ISystemPromptAssembler>(inner, new ContributionOptions(Order: 10));
        registry.Add<ISystemPromptAssembler>(outer, new ContributionOptions(Order: 20));

        var prompt = builder.BuildSystemPrompt("thread-a");

        Assert.Null(inner.Received);
        Assert.Equal(withDefault, outer.Received);
        Assert.Equal("OUTER:" + withDefault, prompt);
    }

    [Fact]
    public void Assembler_ThreadScopedReplacementTakesOverThatThreadOnly()
    {
        var registry = CreateRegistry();
        var builder = CreateBuilder(registry);

        registry.Add<ISystemPromptAssembler>(
            new RecordingAssembler("WORKSPACE"),
            new ContributionOptions() { TargetName = "assembler" });
        var handle = registry.Add<ISystemPromptAssembler>(
            new RecordingAssembler("THREAD"),
            new ContributionOptions(ContributionScope.Thread, "thread-a", ReplaceTarget: "assembler"));

        Assert.StartsWith("THREAD:", builder.BuildSystemPrompt("thread-a"), StringComparison.Ordinal);
        Assert.StartsWith("WORKSPACE:", builder.BuildSystemPrompt("thread-b"), StringComparison.Ordinal);

        handle.Dispose();

        Assert.StartsWith("WORKSPACE:", builder.BuildSystemPrompt("thread-a"), StringComparison.Ordinal);
    }

    [Fact]
    public void FailingAssembler_FallsBackToTheDefaultAssembly()
    {
        var registry = CreateRegistry();
        var builder = CreateBuilder(registry);
        var withDefault = builder.BuildSystemPrompt("thread-a");

        registry.Add<ISystemPromptAssembler>(new ThrowingAssembler());

        Assert.Equal(withDefault, builder.BuildSystemPrompt("thread-a"));
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

    private static void AssertOrder(string prompt, params string[] markers)
    {
        var previous = -1;
        foreach (var marker in markers)
        {
            var index = prompt.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(index >= 0, $"Marker '{marker}' is missing from the prompt.");
            Assert.True(index > previous, $"Marker '{marker}' appeared out of order.");
            previous = index;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class MarkerSection(string name, string? content) : ISystemPromptSection
    {
        public string Name => name;

        public string? GetContent(SystemPromptSectionContext context) => content;
    }

    private sealed class RecordingAssembler(string tag) : ISystemPromptAssembler
    {
        public string? Received { get; private set; }

        public string Assemble(string prompt, SystemPromptSectionContext context)
        {
            Received = prompt;
            return $"{tag}:{prompt}";
        }
    }

    private sealed class ThrowingAssembler : ISystemPromptAssembler
    {
        public string Assemble(string prompt, SystemPromptSectionContext context) =>
            throw new InvalidOperationException("assembler failure");
    }
}
