using DotCraft.Contributions;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Contributions;

/// <summary>The Tier-B auxiliary generator contribution points, and the late binding that makes a replacement
/// visible to a consumer that captured the service before the plugin registered.</summary>
public sealed class SuggestionServiceContributionTests
{
    private static readonly CommitMessageSuggestionRequest CommitRequest = new()
    {
        ThreadId = "thread-1",
        Paths = ["src/a.cs"]
    };

    [Fact]
    public async Task WithoutAnyRegistration_TheForwarderUsesTheBuiltIn()
    {
        var registry = new ContributionRegistry();
        var builtIn = new StubCommitService("built-in");
        var forwarder = new ContributedCommitMessageSuggestService(registry, builtIn);

        var result = await forwarder.SuggestAsync(CommitRequest);

        Assert.Equal("built-in", result.Message);
    }

    [Fact]
    public async Task TheRegisteredBuiltIn_IsTheEffectiveGenerator()
    {
        var registry = new ContributionRegistry();
        var builtIn = new StubCommitService("built-in");
        SuggestionServiceCatalog.RegisterBuiltIns(registry, builtIn, welcomeSuggestions: null);
        var forwarder = new ContributedCommitMessageSuggestService(registry, builtIn);

        var result = await forwarder.SuggestAsync(CommitRequest);

        Assert.Equal("built-in", result.Message);
    }

    [Fact]
    public async Task AReplacementRegisteredAfterCapture_IsObservedByTheCapturedForwarder()
    {
        var registry = new ContributionRegistry();
        var builtIn = new StubCommitService("built-in");
        SuggestionServiceCatalog.RegisterBuiltIns(registry, builtIn, welcomeSuggestions: null);

        // A connection captures the forwarder here, before any plugin has activated.
        var captured = new ContributedCommitMessageSuggestService(registry, builtIn);
        Assert.Equal("built-in", (await captured.SuggestAsync(CommitRequest)).Message);

        var handle = registry.Add<ICommitMessageSuggester>(
            new StubCommitService("plugin"),
            new ContributionOptions(ReplaceTarget: SuggestionServiceNames.CommitMessageSuggest));

        Assert.Equal("plugin", (await captured.SuggestAsync(CommitRequest)).Message);

        handle.Dispose();
        Assert.Equal("built-in", (await captured.SuggestAsync(CommitRequest)).Message);
    }

    [Fact]
    public async Task AThreadScopedReplacement_IsNotHonored()
    {
        // Both generators are invoked without a thread in hand, so the contribution point is addressed at workspace scope.
        var registry = new ContributionRegistry();
        var builtIn = new StubCommitService("built-in");
        SuggestionServiceCatalog.RegisterBuiltIns(registry, builtIn, welcomeSuggestions: null);
        var captured = new ContributedCommitMessageSuggestService(registry, builtIn);
        registry.Add<ICommitMessageSuggester>(
            new StubCommitService("thread"),
            new ContributionOptions(
                ContributionScope.Thread,
                ThreadId: "thread-1",
                ReplaceTarget: SuggestionServiceNames.CommitMessageSuggest));

        Assert.Equal("built-in", (await captured.SuggestAsync(CommitRequest)).Message);
    }

    [Fact]
    public async Task AWelcomeReplacement_TakesOverEveryMemberOfTheContract()
    {
        var registry = new ContributionRegistry();
        var builtIn = new StubWelcomeService("built-in");
        SuggestionServiceCatalog.RegisterBuiltIns(registry, commitMessageSuggest: null, builtIn);
        var captured = new ContributedWelcomeSuggestionService(registry, builtIn);
        var replacement = new StubWelcomeService("plugin");

        var handle = registry.Add<IWelcomeSuggester>(
            replacement,
            new ContributionOptions(ReplaceTarget: SuggestionServiceNames.WelcomeSuggestions));
        var snapshot = await captured.SuggestAsync(new WelcomeSuggestionRequest());
        captured.ScheduleRefresh("F:/ws", "thread-1");
        captured.ClearWorkspaceCache("F:/ws");

        Assert.Equal("plugin", snapshot.Source);
        Assert.Equal(1, replacement.Refreshes);
        Assert.Equal(1, replacement.Clears);
        Assert.Equal(0, builtIn.Refreshes);

        handle.Dispose();
        captured.ScheduleRefresh("F:/ws");
        Assert.Equal(1, builtIn.Refreshes);
    }

    [Fact]
    public void TheBuiltInHandles_OnlyRemoveTheContribution()
    {
        var registry = new ContributionRegistry();
        var welcome = new StubWelcomeService("built-in");
        var handles = SuggestionServiceCatalog.RegisterBuiltIns(
            registry,
            new StubCommitService("built-in"),
            welcome);

        foreach (var handle in handles)
            handle.Dispose();

        Assert.Empty(registry.Resolve<IWelcomeSuggester>());
        Assert.False(welcome.Disposed);
    }

    private sealed class StubCommitService(string message) : ICommitMessageSuggester
    {
        public Task<CommitMessageSuggestionResult> SuggestAsync(
            CommitMessageSuggestionRequest parameters,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CommitMessageSuggestionResult(message));
    }

    private sealed class StubWelcomeService(string source) : IWelcomeSuggester, IDisposable
    {
        public int Refreshes { get; private set; }

        public int Clears { get; private set; }

        public bool Disposed { get; private set; }

        public Task<WelcomeSuggestionSnapshot> SuggestAsync(
            WelcomeSuggestionRequest parameters,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WelcomeSuggestionSnapshot { Source = source });

        public void ScheduleRefresh(string workspacePath, string? triggerThreadId = null) => Refreshes++;

        public void ClearWorkspaceCache(string workspacePath) => Clears++;

        public void Dispose() => Disposed = true;
    }
}
