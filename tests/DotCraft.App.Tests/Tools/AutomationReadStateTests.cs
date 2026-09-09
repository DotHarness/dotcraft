using DotCraft.Automations;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class AutomationReadStateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "automation_read_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private static AutomationRun Run(string id = "run") => new() { Id = id, AutomationId = "task", Status = "running", CreatedAt = DateTimeOffset.UtcNow };

    [Fact]
    public async Task ReceiptsSurviveRestartAndExecutionWritesButNewResultsAreUnread()
    {
        var store = new AutomationStore(_root);
        var run = Run();
        await store.SaveRunAsync(run, default);
        Assert.Null(Assert.Single(await store.RunsAsync("task", default)).ReadAt);
        await store.SetRunsReadAsync("task", ["run"], true, default);
        var read = Assert.Single(await new AutomationStore(_root).RunsAsync("task", default)).ReadAt;
        Assert.NotNull(read);
        await store.SaveRunAsync(run with { DeliveryStatus = "pending" }, default);
        Assert.Equal(read, Assert.Single(await store.RunsAsync("task", default)).ReadAt);
        var completed = run with { Status = "succeeded", CompletedAt = DateTimeOffset.UtcNow };
        await store.SaveRunAsync(completed, default);
        Assert.Null(Assert.Single(await store.RunsAsync("task", default)).ReadAt);
        await store.SetRunsReadAsync("task", ["run"], true, default);
        await store.SaveRunAsync(completed with { DeliveryStatus = "sent" }, default);
        Assert.NotNull(Assert.Single(await store.RunsAsync("task", default)).ReadAt);
        await store.SetRunsReadAsync("task", ["run"], false, default);
        Assert.Null(Assert.Single(await new AutomationStore(_root).RunsAsync("task", default)).ReadAt);
    }

    [Fact]
    public async Task InvalidBatchDoesNotPartiallyMarkRead()
    {
        var store = new AutomationStore(_root);
        await store.SaveRunAsync(Run(), default);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetRunsReadAsync("task", ["run", "missing"], true, default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetRunsReadAsync("task", ["run", "run"], true, default));
        Assert.Null(Assert.Single(await store.RunsAsync("task", default)).ReadAt);
    }

    [Fact]
    public async Task ConcurrentReadingUpdatesPreserveBothReceipts()
    {
        var store = new AutomationStore(_root);
        await store.SaveRunAsync(Run("first"), default);
        await store.SaveRunAsync(Run("second"), default);
        await Task.WhenAll(store.SetRunsReadAsync("task", ["first"], true, default), store.SetRunsReadAsync("task", ["second"], true, default));
        Assert.All(await store.RunsAsync("task", default), run => Assert.NotNull(run.ReadAt));
    }
}
