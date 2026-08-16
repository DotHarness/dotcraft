using DotCraft.Persistence;
using DotCraft.Tracing;
using DotCraft.TraceViewer.Analysis;
using DotCraft.TraceViewer.ViewModels;
using Xunit;

namespace DotCraft.TraceViewer.Tests;

public sealed class ReviewOperationIsolationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "dotcraft-trace-viewer-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Session_switch_discards_delayed_analysis_result()
    {
        var workspace = CreateWorkspace("workspace", "thread-1", "thread-2");
        var analyst = new DelayedAnalystService();
        var reviewStore = new TraceReviewStore(Path.Combine(_root, "reviews"));
        using var viewModel = CreateViewModel(analyst, reviewStore);
        await viewModel.OpenWorkspaceAsync(workspace);
        viewModel.OpenSession(viewModel.Sessions.Single(item => item.SessionKey == "thread-1"));
        await WaitUntilAsync(() => !viewModel.IsBusy);

        var analysis = viewModel.AnalyzeTraceAsync();
        var snapshot = await analyst.Started.Task;
        viewModel.OpenSession(viewModel.Sessions.Single(item => item.SessionKey == "thread-2"));
        analyst.Complete(CreateReview(snapshot));
        await analysis;

        Assert.False(viewModel.HasReview);
        Assert.Null(reviewStore.Load(workspace, "thread-1"));
        Assert.Null(reviewStore.Load(workspace, "thread-2"));
    }

    [Fact]
    public async Task Workspace_switch_waits_for_analysis_and_discards_its_result()
    {
        var workspaceA = CreateWorkspace("workspace-a", "thread-1");
        var workspaceB = CreateWorkspace("workspace-b", "thread-2");
        var analyst = new DelayedAnalystService();
        var reviewStore = new TraceReviewStore(Path.Combine(_root, "reviews"));
        using var viewModel = CreateViewModel(analyst, reviewStore);
        await viewModel.OpenWorkspaceAsync(workspaceA);
        viewModel.OpenSession(Assert.Single(viewModel.Sessions));
        await WaitUntilAsync(() => !viewModel.IsBusy);

        var analysis = viewModel.AnalyzeTraceAsync();
        var snapshot = await analyst.Started.Task;
        var workspaceSwitch = viewModel.OpenWorkspaceAsync(workspaceB);
        await Task.Yield();
        Assert.False(workspaceSwitch.IsCompleted);

        analyst.Complete(CreateReview(snapshot));
        await Task.WhenAll(analysis, workspaceSwitch);

        Assert.Equal(Path.GetFullPath(workspaceB), viewModel.WorkspacePath);
        Assert.Null(reviewStore.Load(workspaceA, "thread-1"));
        Assert.Null(reviewStore.Load(workspaceB, "thread-1"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private MainViewModel CreateViewModel(
        ITraceAnalystService analyst,
        TraceReviewStore reviewStore) => new(
        new TraceViewerSettingsStore(Path.Combine(_root, "settings.json")),
        analyst,
        reviewStore);

    private string CreateWorkspace(string name, params string[] sessionKeys)
    {
        var workspace = Path.Combine(_root, name);
        var dataPath = Path.Combine(workspace, ".craft");
        Directory.CreateDirectory(workspace);
        using var database = new WorkspaceStateDatabase(dataPath);
        var writer = new TraceStore(database, 5000, synchronousPersist: true);
        foreach (var sessionKey in sessionKeys)
        {
            writer.Record(new TraceEvent
            {
                Id = $"{sessionKey}-request",
                Type = TraceEventType.Request,
                SessionKey = sessionKey,
                Content = $"Review {sessionKey}",
            });
        }

        return workspace;
    }

    private static TraceReview CreateReview(TraceSnapshot snapshot) => new(
        1,
        snapshot.SessionKey,
        snapshot.Revision,
        DateTimeOffset.UtcNow,
        "test-model",
        "Review completed.",
        [],
        "analyst-thread");

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class DelayedAnalystService : ITraceAnalystService
    {
        private readonly TaskCompletionSource<TraceReview> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<TraceSnapshot> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TraceReview> AnalyzeAsync(
            TraceSnapshot snapshot,
            string dataPath,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult(snapshot);
            return _completion.Task;
        }

        public Task<string> AskAsync(
            TraceSnapshot snapshot,
            string dataPath,
            TraceReview review,
            string question,
            string? attachment,
            IProgress<string>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Complete(TraceReview review) => _completion.TrySetResult(review);

        public void CommitEvidence(TraceSnapshot snapshot) { }

        public void Cancel() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
