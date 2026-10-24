using DotCraft.Sdk;
using DotCraft.Protocol.AppServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.Integrations;

namespace DotCraft.Oratorio.Services;

public sealed class DiscussionSessionRunner(
    IServiceScopeFactory scopeFactory,
    DiscussionThreadSessions sessions,
    IOptionsMonitor<DotCraftOptions> options,
    IClock clock,
    IHostApplicationLifetime lifetime,
    ILogger<DiscussionSessionRunner> logger)
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(250);

    public void Start(DiscussionThreadSession session) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await RunAsync(session, lifetime.ApplicationStopping);
            }
            catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Agent Discussion session for thread {ThreadId} failed.", session.ThreadId);
                await FailWaitingAsync(session, "discussionTurnFailed", ex.Message, null);
            }
            finally
            {
                await CloseAsync(session);
            }
        });

    private async Task RunAsync(DiscussionThreadSession session, CancellationToken ct)
    {
        var wait = new BackgroundWorkWait(BackgroundWorkWait.PollInterval(options.CurrentValue));
        DateTimeOffset? settleStartedAt = null;
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await using var events = session.Client.ReadEventsAsync(readCts.Token).GetAsyncEnumerator(readCts.Token);
        Task<bool>? nextEvent = null;
        var check = Task.Delay(CheckInterval, ct);
        try
        {
            while (true)
            {
                nextEvent ??= events.MoveNextAsync().AsTask();
                var timer = wait.Timer;
                var completed = timer is null
                    ? await Task.WhenAny(nextEvent, check)
                    : await Task.WhenAny(nextEvent, check, timer);
                ct.ThrowIfCancellationRequested();
                if (completed == nextEvent)
                {
                    if (!await nextEvent)
                    {
                        nextEvent = null;
                        if (wait.Timer is not null)
                        {
                            await FailWaitingAsync(session, "discussionReplyMissing", "The agent completed without submitting a discussion reply.", settleStartedAt ?? clock.UtcNow);
                        }

                        await FailWaitingAsync(session, "discussionTurnDisconnected", "The AppServer disconnected before the Agent Discussion Turn completed.", null);
                        return;
                    }

                    var runEvent = events.Current;
                    nextEvent = null;
                    var otherThread = !string.IsNullOrWhiteSpace(runEvent.ThreadId) &&
                        !string.Equals(runEvent.ThreadId, session.ThreadId, StringComparison.Ordinal);
                    switch (otherThread ? null : runEvent.Type)
                    {
                        case "turn/started":
                            session.TurnStarted(runEvent.TurnId);
                            wait.Reset();
                            settleStartedAt = null;
                            break;
                        case "turn/completed":
                            session.TurnEnded(runEvent.TurnId);
                            if (session.ActiveTurnId is null && await PruneAsync(session) > 0)
                            {
                                settleStartedAt = await ScheduleWaitAsync(session, wait, ct);
                            }

                            break;
                        case "turn/failed":
                            session.TurnEnded(runEvent.TurnId);
                            await FailWaitingAsync(
                                session,
                                "discussionTurnFailed",
                                (runEvent as DotCraftRunEvent<TurnNotification>)?.Params is { } failed
                                    ? failed.Error ?? failed.Turn.Error ?? "The Agent Discussion Turn failed."
                                    : "The Agent Discussion Turn failed.",
                                null);
                            break;
                        case "turn/cancelled":
                            session.TurnEnded(runEvent.TurnId);
                            await FailWaitingAsync(session, "discussionTurnCancelled", "The Agent Discussion Turn was cancelled.", null);
                            break;
                    }
                }
                else if (completed == check)
                {
                    check = Task.Delay(CheckInterval, ct);
                }
                else if (wait.Settling)
                {
                    await FailWaitingAsync(session, "discussionReplyMissing", "The agent completed without submitting a discussion reply.", settleStartedAt);
                    wait.Reset();
                    settleStartedAt = session.ActiveTurnId is null && await PruneAsync(session) > 0
                        ? await ScheduleWaitAsync(session, wait, ct)
                        : null;
                }
                else
                {
                    settleStartedAt = await ScheduleWaitAsync(session, wait, ct);
                }

                await FailExpiredAsync(session);
                if (await PruneAsync(session) == 0 && await TryCloseAsync(session))
                {
                    return;
                }
            }
        }
        finally
        {
            if (nextEvent is not null)
            {
                await readCts.CancelAsync();
                try
                {
                    await nextEvent;
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
    }

    private async Task<DateTimeOffset?> ScheduleWaitAsync(DiscussionThreadSession session, BackgroundWorkWait wait, CancellationToken ct)
    {
        var now = clock.UtcNow;
        return await wait.ScheduleAsync(session.Client, session.ThreadId, ct) ? null : now;
    }

    private async Task<int> PruneAsync(DiscussionThreadSession session)
    {
        if (session.Waiting.IsEmpty)
        {
            return 0;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var ids = session.Waiting.Keys.ToArray();
        var running = await db.DiscussionTurns.AsNoTracking()
            .Where(x => ids.Contains(x.DiscussionTurnId) && x.Status == DiscussionTurnStatus.Running)
            .Select(x => x.DiscussionTurnId)
            .ToListAsync();
        foreach (var id in ids.Except(running, StringComparer.Ordinal))
        {
            session.Waiting.TryRemove(id, out _);
        }

        return session.Waiting.Count;
    }

    private async Task FailExpiredAsync(DiscussionThreadSession session)
    {
        var expiredBefore = clock.UtcNow - options.CurrentValue.RunTimeout;
        foreach (var (id, deliveredAt) in session.Waiting.ToArray())
        {
            if (deliveredAt <= expiredBefore)
            {
                await FailAsync(session, id, "discussionTurnTimedOut", "The Agent Discussion Turn timed out.");
            }
        }
    }

    private async Task FailWaitingAsync(DiscussionThreadSession session, string code, string message, DateTimeOffset? deliveredBefore)
    {
        foreach (var (id, deliveredAt) in session.Waiting.ToArray())
        {
            if (deliveredBefore is null || deliveredAt <= deliveredBefore)
            {
                await FailAsync(session, id, code, message);
            }
        }
    }

    private async Task FailAsync(DiscussionThreadSession session, string discussionTurnId, string code, string message)
    {
        using var scope = scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DiscussionTurnService>()
            .FailAsync(discussionTurnId, code, message, CancellationToken.None);
        session.Waiting.TryRemove(discussionTurnId, out _);
    }

    private async Task<bool> TryCloseAsync(DiscussionThreadSession session)
    {
        await session.DeliveryGate.WaitAsync();
        try
        {
            if (!session.Waiting.IsEmpty || session.ActiveTurnId is not null)
            {
                return false;
            }

            session.Closed = true;
            sessions.Remove(session);
            return true;
        }
        finally
        {
            session.DeliveryGate.Release();
        }
    }

    private async Task CloseAsync(DiscussionThreadSession session)
    {
        await session.DeliveryGate.WaitAsync();
        try
        {
            session.Closed = true;
            sessions.Remove(session);
        }
        finally
        {
            session.DeliveryGate.Release();
        }

        await session.Client.DisposeAsync();
    }
}
