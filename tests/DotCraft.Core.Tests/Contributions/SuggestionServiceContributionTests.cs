using DotCraft.Contributions;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Contributions;

public sealed class SuggestionServiceContributionTests
{
    private static readonly CommitMessageSuggestionRequest CommitRequest = new()
    {
        ThreadId = "thread-1",
        Paths = ["src/a.cs"]
    };

    [Fact]
    public async Task AReplacementRegisteredAfterCapture_IsObservedByTheCapturedForwarder()
    {
        var registry = new ContributionRegistry();
        var builtIn = new StubCommitService("built-in");
        Assert.Equal(
            "built-in",
            (await new ContributedCommitMessageSuggestService(registry, builtIn).SuggestAsync(CommitRequest)).Message);
        SuggestionServiceCatalog.RegisterBuiltIn(registry, builtIn);

        var captured = new ContributedCommitMessageSuggestService(registry, builtIn);
        registry.Add<ICommitMessageSuggester>(
            new StubCommitService("thread"),
            new ContributionOptions(
                ContributionScope.Thread,
                ThreadId: "thread-1",
                ReplaceTarget: SuggestionServiceNames.CommitMessageSuggest));
        Assert.Equal("built-in", (await captured.SuggestAsync(CommitRequest)).Message);

        var handle = registry.Add<ICommitMessageSuggester>(
            new StubCommitService("plugin"),
            new ContributionOptions(ReplaceTarget: SuggestionServiceNames.CommitMessageSuggest));

        Assert.Equal("plugin", (await captured.SuggestAsync(CommitRequest)).Message);

        handle.Dispose();
        Assert.Equal("built-in", (await captured.SuggestAsync(CommitRequest)).Message);
    }

    [Fact]
    public void TheBuiltInHandles_OnlyRemoveTheContribution()
    {
        var registry = new ContributionRegistry();
        var commit = new StubCommitService("built-in");
        SuggestionServiceCatalog.RegisterBuiltIn(registry, commit).Dispose();

        Assert.Empty(registry.Resolve<ICommitMessageSuggester>());
        Assert.False(commit.Disposed);
    }

    private sealed class StubCommitService(string message) : ICommitMessageSuggester, IDisposable
    {
        public bool Disposed { get; private set; }

        public Task<CommitMessageSuggestionResult> SuggestAsync(
            CommitMessageSuggestionRequest parameters,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CommitMessageSuggestionResult(message));

        public void Dispose() => Disposed = true;
    }
}
