using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionTaskNotificationTests
{
    [Fact]
    public async Task Workflow_RetriesShareOneQueuedInputAndDoNotRepeatConsumedDelivery()
    {
        await using var harness = await CreateHarnessAsync(config => config.Tools.BackgroundTasks.CompletionDelivery = "off");
        var ending = Ending(harness, BackgroundTaskKinds.Workflow, "run_retry", "review");

        var ids = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => harness.Service.NotifyBackgroundTaskEndedAsync(ending)));
        var queued = await harness.Service.GetThreadAsync(harness.ThreadId);
        Assert.Equal(Assert.Single(queued.QueuedInputs).Id, Assert.Single(ids.Distinct()));

        await harness.RunUserTurnAsync("continue");
        var id = await harness.Service.NotifyBackgroundTaskEndedAsync(ending);
        var consumed = await harness.Service.GetThreadAsync(harness.ThreadId);
        Assert.Equal(ids[0], id);
        Assert.Empty(consumed.QueuedInputs);
        var turn = Assert.Single(consumed.Turns);
        Assert.Single(turn.Items, item => item.AsUserMessage?.TriggerRefId == ending.TaskId);
    }

    [Fact]
    public async Task Workflow_WakeFailureKeepsOneInputAndIsRetriedWithoutDuplicateDelivery()
    {
        await using var writer = new FailingQueueWriter();
        await using var harness = await CreateHarnessAsync(rolloutWriter: writer);
        harness.Client.HoldFirstRequest();
        var ending = Ending(harness, BackgroundTaskKinds.Workflow, "run_wake_retry", "review");
        var id = await harness.Service.NotifyBackgroundTaskEndedAsync(ending with { MayWake = false });
        writer.FailNextKind = RolloutKinds.QueuedInputRemoved;

        await Assert.ThrowsAnyAsync<IOException>(() => harness.Service.NotifyBackgroundTaskEndedAsync(ending));
        var failed = await harness.Service.GetThreadAsync(harness.ThreadId);
        Assert.Equal(id, Assert.Single(failed.QueuedInputs).Id);
        Assert.Empty(failed.Turns);

        Assert.Equal(id, await harness.Service.NotifyBackgroundTaskEndedAsync(ending));
        await harness.Client.FirstRequestStarted.WaitAsync(TimeSpan.FromSeconds(10));
        var started = await harness.Service.GetThreadAsync(harness.ThreadId);
        Assert.Empty(started.QueuedInputs);
        Assert.Single(Assert.Single(started.Turns).Items, item => item.AsUserMessage?.TriggerRefId == ending.TaskId);
    }

    [Fact]
    public async Task Workflow_FailedEnqueueIsNotAcknowledgedFromAnUncommittedInput()
    {
        await using var writer = new FailingQueueWriter();
        await using var harness = await CreateHarnessAsync(rolloutWriter: writer);
        var ending = Ending(harness, BackgroundTaskKinds.Workflow, "run_enqueue_retry", "review") with { MayWake = false };
        writer.FailNextKind = RolloutKinds.QueuedInputAdded;

        await Assert.ThrowsAnyAsync<IOException>(() => harness.Service.NotifyBackgroundTaskEndedAsync(ending));
        Assert.Empty((await harness.Service.GetThreadAsync(harness.ThreadId)).QueuedInputs);
        var id = await harness.Service.NotifyBackgroundTaskEndedAsync(ending);
        Assert.Equal(id, Assert.Single((await harness.Service.GetThreadAsync(harness.ThreadId)).QueuedInputs).Id);
    }

    private sealed class FailingQueueWriter : IRolloutWriter, IAsyncDisposable
    {
        private readonly OrderedRolloutWriter _inner = new();
        public string? FailNextKind { get; set; }

        public Task AddBatchAsync(string threadId, string path, IReadOnlyList<ThreadRolloutRecord> records, CancellationToken ct = default)
        {
            if (records.Any(record => record.Kind == FailNextKind))
            {
                FailNextKind = null;
                throw new IOException("Notification queue persistence failed.");
            }
            return _inner.AddBatchAsync(threadId, path, records, ct);
        }

        public Task<RolloutWriteReceipt> FlushAsync(string threadId, CancellationToken ct = default) => _inner.FlushAsync(threadId, ct);
        public Task CloseAsync(string threadId, CancellationToken ct = default) => _inner.CloseAsync(threadId, ct);
        public Task ShutdownAllAsync(CancellationToken ct = default) => _inner.ShutdownAllAsync(ct);
        public async ValueTask DisposeAsync() => await _inner.ShutdownAllAsync();
    }

    [Fact]
    public async Task Workflow_RetriesWhileGuidanceIsPendingDoNotDuplicateIt()
    {
        await using var harness = await CreateHarnessAsync();
        harness.Client.HoldFirstRequest();
        var turnTask = harness.RunUserTurnAsync("work");
        await harness.Client.FirstRequestStarted.WaitAsync(TimeSpan.FromSeconds(10));
        var ending = Ending(harness, BackgroundTaskKinds.Workflow, "run_guidance", "review");

        var id = await harness.Service.NotifyBackgroundTaskEndedAsync(ending);
        Assert.Equal(id, await harness.Service.NotifyBackgroundTaskEndedAsync(ending));
        var pending = await harness.Service.GetThreadAsync(harness.ThreadId);
        Assert.Equal("guidancePending", Assert.Single(pending.QueuedInputs).Status);
        harness.Client.ReleaseFirstRequest();
        await turnTask;

        Assert.Equal(id, await harness.Service.NotifyBackgroundTaskEndedAsync(ending));
        var completed = await harness.Service.GetThreadAsync(harness.ThreadId);
        Assert.Empty(completed.QueuedInputs);
        Assert.Single(Assert.Single(completed.Turns).Items, item => item.AsUserMessage?.TriggerRefId == ending.TaskId);
    }
}
