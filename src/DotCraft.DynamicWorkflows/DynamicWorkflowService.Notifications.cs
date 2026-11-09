using System.Collections.Concurrent;
using DotCraft.Sessions;
using Microsoft.Extensions.Logging;

namespace DotCraft.DynamicWorkflows;

public sealed partial class DynamicWorkflowService
{
    private static readonly TimeSpan CompletionWait = TimeSpan.FromSeconds(5);
    private readonly CancellationTokenSource _notificationStopping = new();
    private readonly ConcurrentDictionary<string, NotificationDelivery> _notifications = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ActiveRun> _retiring = new(StringComparer.Ordinal);
    internal TimeProvider NotificationTimeProvider { get; set; } = TimeProvider.System;

    private sealed class NotificationDelivery(CancellationToken stopping)
    {
        public CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Cancel()
        {
            try { Cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    private Task StartNotificationDelivery(ActiveRun active)
    {
        if (active.State.NotificationStatus != "pending") return Task.CompletedTask;
        var delivery = new NotificationDelivery(_notificationStopping.Token);
        if (!_notifications.TryAdd(active.State.RunId, delivery))
        {
            delivery.Cancellation.Dispose();
            return _notifications.TryGetValue(active.State.RunId, out var current)
                ? current.Completion.Task : Task.CompletedTask;
        }
        _ = DeliverPendingNotificationAsync(active, delivery);
        return delivery.Completion.Task;
    }

    private async Task DeliverPendingNotificationAsync(ActiveRun active, NotificationDelivery delivery)
    {
        var retrySeconds = 1;
        try
        {
            while (!delivery.Cancellation.IsCancellationRequested)
            {
                try
                {
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(delivery.Cancellation.Token);
                    attempt.CancelAfter(CompletionWait);
                    var notifications = _sessionService as IBackgroundTaskNotifications
                        ?? throw new InvalidOperationException("Session service does not deliver background task notifications.");
                    var notification = notifications.NotifyBackgroundTaskEndedAsync(
                        BuildTaskEnding(active.State, store.GetRunDirectory(active.State.RunId), active.Latch), attempt.Token);
                    string? queuedId;
                    try { queuedId = await notification.WaitAsync(attempt.Token).ConfigureAwait(false); }
                    catch
                    {
                        ObserveLateTask(notification, active.State.RunId);
                        throw;
                    }

                    await active.Gate.WaitAsync(attempt.Token).ConfigureAwait(false);
                    try
                    {
                        if (active.State.NotificationStatus != "pending") return;
                        var delivered = active.State with
                        {
                            NotificationStatus = queuedId == null ? "notApplicable" : "queued",
                            NotificationInputId = queuedId
                        };
                        await store.WriteStateAsync(delivered, attempt.Token).ConfigureAwait(false);
                        active.State = delivered;
                        PublishChanged(delivered, "state");
                    }
                    finally { active.Gate.Release(); }
                    return;
                }
                catch (OperationCanceledException) when (delivery.Cancellation.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Workflow notification remains pending for {RunId}.", active.State.RunId);
                }
                await Task.Delay(TimeSpan.FromSeconds(retrySeconds), NotificationTimeProvider, delivery.Cancellation.Token).ConfigureAwait(false);
                retrySeconds = Math.Min(30, retrySeconds * 2);
            }
        }
        catch (OperationCanceledException) when (delivery.Cancellation.IsCancellationRequested) { }
        finally
        {
            _notifications.TryRemove(new KeyValuePair<string, NotificationDelivery>(active.State.RunId, delivery));
            delivery.Cancellation.Dispose();
            delivery.Completion.TrySetResult();
        }
    }

    private void TrackRetirement(ActiveRun active, IEnumerable<Task> tasks, IAsyncDisposable? connection = null)
    {
        _retiring[active.State.RunId] = active;
        active.Retirement = RetireAsync();
        async Task RetireAsync()
        {
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch (Exception ex) { logger?.LogDebug(ex, "Workflow work settled after termination for {RunId}.", active.State.RunId); }
            finally
            {
                try { await Task.WhenAll(active.CleanupTasks).ConfigureAwait(false); }
                catch (Exception ex) { logger?.LogDebug(ex, "Workflow worktree cleanup settled for {RunId}.", active.State.RunId); }
                try { if (connection != null) await connection.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { logger?.LogDebug(ex, "Workflow connection cleanup failed for {RunId}.", active.State.RunId); }
                active.Capacity.Dispose();
                active.Gate.Dispose();
                active.Cancellation.Dispose();
                _retiring.TryRemove(new KeyValuePair<string, ActiveRun>(active.State.RunId, active));
            }
        }
    }

    private void ObserveLateTask(Task task, string runId)
    {
        _ = task.ContinueWith(
            failed => logger?.LogDebug(failed.Exception, "Late workflow operation failed for {RunId}.", runId),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
