using DotCraft.Commands.Core;
using DotCraft.Commands.Custom;
using DotCraft.Context;
using DotCraft.Contributions;
using DotCraft.Memory;
using DotCraft.Skills;
using Xunit;

namespace DotCraft.Tests.Contributions;

/// <summary>The additive slash command contribution point: where a contributed command composes against the built-in
/// handlers and the markdown custom commands, and what revoking one restores.</summary>
public sealed class CodeCommandContributionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CommandContribution_" + Guid.NewGuid().ToString("N")[..8]);

    private readonly string _workspace;
    private readonly string _craft;

    public CodeCommandContributionTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        _craft = Path.Combine(_workspace, ".craft");
        Directory.CreateDirectory(Path.Combine(_craft, "commands"));
    }

    [Fact]
    public async Task ContributedCommand_IsListedExecutedAndKnown()
    {
        var registry = new ContributionRegistry();
        var commands = CreateRegistry(registry);
        registry.Add<ICodeCommand>(new StaticCommand("/triage", "Triage the inbox", "TRIAGE"));

        var listed = Assert.Single(commands.ListCommands(), info => info.Name == "/triage");
        Assert.Equal("Triage the inbox", listed.Description);
        Assert.Equal("custom", listed.Category);
        Assert.Contains("/triage", commands.GetKnownCommands());

        var result = await ExecuteAsync(commands, "/triage now");

        Assert.True(result.Handled);
        Assert.Equal("TRIAGE:now", result.ExpandedPrompt);
    }

    [Fact]
    public async Task Revoking_RemovesTheCommandFromEveryReadingOfTheContributionPoint()
    {
        var registry = new ContributionRegistry();
        var commands = CreateRegistry(registry);
        var handle = registry.Add<ICodeCommand>(new StaticCommand("/triage", "Triage", "TRIAGE"));
        Assert.NotNull(commands.TryResolvePromptExpansion("/triage"));

        handle.Dispose();

        Assert.DoesNotContain(commands.ListCommands(), info => info.Name == "/triage");
        Assert.DoesNotContain("/triage", commands.GetKnownCommands());
        Assert.Null(commands.TryResolvePromptExpansion("/triage"));
        var result = await ExecuteAsync(commands, "/triage");
        Assert.Null(result.ExpandedPrompt);
    }

    [Fact]
    public void Aliases_ResolveToTheSameContribution()
    {
        var registry = new ContributionRegistry();
        var commands = CreateRegistry(registry);
        registry.Add<ICodeCommand>(
            new StaticCommand("triage", "Triage", "TRIAGE") { AliasNames = ["tri", "/tg"] });

        Assert.Equal("TRIAGE:", commands.TryResolvePromptExpansion("/tri"));
        Assert.Equal("TRIAGE:", commands.TryResolvePromptExpansion("/tg"));
        var listed = Assert.Single(commands.ListCommands(), info => info.Name == "/triage");
        Assert.Equal(["/tri", "/tg"], listed.Aliases);
    }

    [Fact]
    public void Ordering_GivesTheNameToTheLowestOrderContribution()
    {
        var registry = new ContributionRegistry();
        var commands = CreateRegistry(registry);
        registry.Add<ICodeCommand>(
            new StaticCommand("/triage", "Late", "LATE"),
            new ContributionOptions(Order: 200));
        registry.Add<ICodeCommand>(
            new StaticCommand("/triage", "Early", "EARLY"),
            new ContributionOptions(Order: 100));

        Assert.Equal("EARLY:", commands.TryResolvePromptExpansion("/triage"));
        var listed = Assert.Single(commands.ListCommands(), info => info.Name == "/triage");
        Assert.Equal("Early", listed.Description);
    }

    [Fact]
    public void ADecliningContribution_LetsTheNextOneAnswer()
    {
        var registry = new ContributionRegistry();
        var commands = CreateRegistry(registry);
        registry.Add<ICodeCommand>(
            new StaticCommand("/triage", "Declines", expansion: null),
            new ContributionOptions(Order: 100));
        registry.Add<ICodeCommand>(
            new StaticCommand("/triage", "Answers", "ANSWER"),
            new ContributionOptions(Order: 200));

        Assert.Equal("ANSWER:", commands.TryResolvePromptExpansion("/triage"));
    }

    [Fact]
    public void AThrowingContribution_IsSkippedWithoutBreakingTheRest()
    {
        var registry = new ContributionRegistry();
        var commands = CreateRegistry(registry);
        registry.Add<ICodeCommand>(new ThrowingCommand(), new ContributionOptions(Order: 100));
        registry.Add<ICodeCommand>(
            new StaticCommand("/triage", "Survivor", "SURVIVOR"),
            new ContributionOptions(Order: 200));

        Assert.Equal("SURVIVOR:", commands.TryResolvePromptExpansion("/triage"));
        // The throwing contribution never describes itself, so it is neither listed nor matchable.
        Assert.Null(commands.TryResolvePromptExpansion("/boom"));
        Assert.DoesNotContain(commands.ListCommands(), info => info.Name == "/boom");
    }

    [Fact]
    public void ThreadScopedCommand_ResolvesOnlyForItsOwnThread()
    {
        var registry = new ContributionRegistry();
        var commands = CreateRegistry(registry);
        registry.Add<ICodeCommand>(
            new StaticCommand("/triage", "Scoped", "SCOPED"),
            ContributionOptions.ForThread("thread-a"));

        Assert.Equal("SCOPED:", commands.TryResolvePromptExpansion("/triage", "thread-a"));
        Assert.Null(commands.TryResolvePromptExpansion("/triage", "thread-b"));
        Assert.Null(commands.TryResolvePromptExpansion("/triage"));
    }

    [Fact]
    public async Task LateRegistration_IsSeenByTheAlreadyBuiltRegistry()
    {
        var registry = new ContributionRegistry();
        var commands = CreateRegistry(registry);
        Assert.Null(commands.TryResolvePromptExpansion("/triage"));

        registry.Add<ICodeCommand>(new StaticCommand("/triage", "Triage", "TRIAGE"));

        var result = await ExecuteAsync(commands, "/triage");
        Assert.Equal("TRIAGE:", result.ExpandedPrompt);
    }

    [Fact]
    public async Task ABuiltInHandler_KeepsItsNameAgainstAContribution()
    {
        var registry = new ContributionRegistry();
        var commands = CreateRegistry(registry);
        registry.Add<ICodeCommand>(new StaticCommand("/help", "Shadow attempt", "SHADOW"));

        var result = await ExecuteAsync(commands, "/help");

        Assert.Null(result.ExpandedPrompt);
        Assert.Single(commands.ListCommands(), info => info.Name == "/help");
        Assert.Equal("builtin", commands.ListCommands().Single(info => info.Name == "/help").Category);
    }

    [Fact]
    public async Task AMarkdownCustomCommand_KeepsItsNameAgainstAContribution()
    {
        File.WriteAllText(
            Path.Combine(_craft, "commands", "triage.md"),
            "---\ndescription: From markdown\n---\nMARKDOWN BODY");
        var registry = new ContributionRegistry();
        var commands = CreateRegistry(registry);
        registry.Add<ICodeCommand>(new StaticCommand("/triage", "From code", "CODE"));

        var result = await ExecuteAsync(commands, "/triage");

        Assert.Equal("MARKDOWN BODY", result.ExpandedPrompt);
        var listed = Assert.Single(commands.ListCommands(), info => info.Name == "/triage");
        Assert.Equal("From markdown", listed.Description);
    }

    [Fact]
    public void TheContributedCommand_ReachesTheSystemPromptAndLeavesNoTraceWhenRevoked()
    {
        var registry = new ContributionRegistry();
        SystemPromptSectionCatalog.RegisterBuiltIns(registry);
        var pages = new ContextPageManager();
        var builder = CreateBuilder(registry, pages);
        var before = builder.BuildSystemPrompt("thread-a");

        var handle = registry.Add<ICodeCommand>(new StaticCommand("/triage", "Triage the inbox", "TRIAGE"));
        pages.ReleaseStablePage(ContextPageKeys.CustomCommandsSummary("*"));
        var withCommand = builder.BuildSystemPrompt("thread-a");

        Assert.Contains("`/triage`: Triage the inbox", withCommand, StringComparison.Ordinal);
        Assert.NotEqual(before, withCommand);

        handle.Dispose();
        pages.ReleaseStablePage(ContextPageKeys.CustomCommandsSummary("*"));

        Assert.Equal(before, builder.BuildSystemPrompt("thread-a"));
    }

    [Fact]
    public void WithoutTheContextPageRelease_TheMemoizedSummarySurvivesTheMutation()
    {
        var registry = new ContributionRegistry();
        SystemPromptSectionCatalog.RegisterBuiltIns(registry);
        var pages = new ContextPageManager();
        var builder = CreateBuilder(registry, pages);
        registry.Add<ICodeCommand>(new StaticCommand("/triage", "Triage", "TRIAGE"));
        var withCommand = builder.BuildSystemPrompt("thread-a");

        registry.Add<ICodeCommand>(new StaticCommand("/sweep", "Sweep", "SWEEP"));

        // The page is pinned for the thread, so the second command only lands once the page is released.
        Assert.Equal(withCommand, builder.BuildSystemPrompt("thread-a"));
        pages.ReleaseStablePage(ContextPageKeys.CustomCommandsSummary("*"));
        Assert.Contains("`/sweep`", builder.BuildSystemPrompt("thread-a"), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private CommandRegistry CreateRegistry(IContributionView contributions) =>
        CommandRegistry.CreateDefault(
            ".craft",
            new CustomCommandLoader(_craft),
            promptCommandProviders: null,
            contributions: contributions);

    private PromptBuilder CreateBuilder(IContributionView contributions, IContextPageManager pages) =>
        new(
            new MemoryStore(_craft),
            new SkillsLoader(_craft),
            _craft,
            _workspace,
            new CustomCommandLoader(_craft),
            toolNamesProvider: () => [],
            contextPageManager: pages,
            contributions: contributions);

    private static Task<CommandResult> ExecuteAsync(CommandRegistry commands, string rawText) =>
        commands.TryExecuteAsync(
            rawText,
            new CommandContext { SessionId = "thread-a", RawText = rawText },
            new NullResponder());

    private sealed class StaticCommand(string name, string description, string? expansion) : ICodeCommand
    {
        public string Name => name;

        public string Description => description;

        public IReadOnlyList<string> Aliases => AliasNames;

        public string[] AliasNames { get; init; } = [];

        public string? Expand(CommandInvocation invocation) =>
            expansion is null ? null : $"{expansion}:{invocation.Arguments}";
    }

    private sealed class ThrowingCommand : ICodeCommand
    {
        public string Name => "/boom";

        public string Description => throw new InvalidOperationException("description failure");

        public string? Expand(CommandInvocation invocation) => "never";
    }

    private sealed class NullResponder : ICommandResponder
    {
        public Task SendTextAsync(string text) => Task.CompletedTask;

        public Task SendMarkdownAsync(string markdown) => Task.CompletedTask;
    }
}
