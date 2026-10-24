using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.Integrations;

namespace DotCraft.Oratorio.Services;

public sealed class DiscussionTurnWorker(
    IServiceScopeFactory scopeFactory,
    DiscussionThreadSessions sessions,
    IOptionsMonitor<DotCraftOptions> options,
    IClock clock,
    ILogger<DiscussionTurnWorker> logger) : BackgroundService
{
    private static readonly RunStatus[] TerminalRunStatuses = [RunStatus.Succeeded, RunStatus.Failed, RunStatus.Cancelled, RunStatus.TimedOut];
    private readonly ConcurrentDictionary<string, string> _deliveringItems = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
                await StartDeliveriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Agent Discussion Turn worker tick failed.");
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<DiscussionTurnService>();
        var expiredBefore = clock.UtcNow - options.CurrentValue.RunTimeout;
        var turns = await db.DiscussionTurns.AsNoTracking()
            .Where(x => x.Status == DiscussionTurnStatus.Running || x.Status == DiscussionTurnStatus.Pending)
            .Select(x => new { x.DiscussionTurnId, x.ItemId, x.Status, x.RunId, x.CreatedAt, x.StartedAt })
            .ToListAsync(ct);
        var runIds = turns.Where(x => x.RunId != null).Select(x => x.RunId!).Distinct().ToArray();
        var endedRuns = await db.Runs.AsNoTracking()
            .Where(x => runIds.Contains(x.RunId) && TerminalRunStatuses.Contains(x.Status))
            .Select(x => x.RunId)
            .ToListAsync(ct);

        foreach (var turn in turns.Where(x => !_deliveringItems.ContainsKey(x.ItemId)))
        {
            if (turn is { Status: DiscussionTurnStatus.Running, RunId: not null } && endedRuns.Contains(turn.RunId) && turn.StartedAt > expiredBefore)
            {
                if (_deliveringItems.TryAdd(turn.ItemId, turn.DiscussionTurnId))
                {
                    _ = Task.Run(() => AdoptAsync(turn.ItemId, turn.DiscussionTurnId, ct), ct);
                }

                continue;
            }

            var (code, message) = turn switch
            {
                { Status: DiscussionTurnStatus.Pending } when turn.CreatedAt <= expiredBefore =>
                    ("discussionDeliveryTimedOut", "The Agent Discussion Turn could not be delivered in time."),
                { Status: DiscussionTurnStatus.Running, RunId: not null } when turn.StartedAt <= expiredBefore =>
                    ("discussionTurnTimedOut", "The Agent Discussion Turn timed out."),
                { Status: DiscussionTurnStatus.Running, RunId: null } when !sessions.Holds(turn.DiscussionTurnId) =>
                    ("discussionTurnInterrupted", "Oratorio stopped following the Agent Discussion Turn before it was answered."),
                _ => (null, null)
            };
            if (code is not null)
            {
                await service.FailAsync(turn.DiscussionTurnId, code, message!, ct);
            }
        }
    }

    private async Task StartDeliveriesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var pending = await db.DiscussionTurns.AsNoTracking()
            .Where(x => x.Status == DiscussionTurnStatus.Pending)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new { x.DiscussionTurnId, x.ItemId })
            .Take(50)
            .ToListAsync(ct);

        foreach (var turn in pending.DistinctBy(x => x.ItemId))
        {
            if (_deliveringItems.TryAdd(turn.ItemId, turn.DiscussionTurnId))
            {
                _ = Task.Run(() => DeliverAsync(turn.ItemId, turn.DiscussionTurnId, ct), ct);
            }
        }
    }

    private async Task AdoptAsync(string itemId, string discussionTurnId, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<DiscussionTurnDelivery>().AdoptFromEndedRunAsync(discussionTurnId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Agent Discussion Turn {DiscussionTurnId} could not be followed after its run ended.", discussionTurnId);
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<DiscussionTurnService>().FailAsync(
                discussionTurnId,
                "discussionRunEnded",
                "The run carrying this question ended before it was answered.",
                CancellationToken.None);
        }
        finally
        {
            _deliveringItems.TryRemove(itemId, out _);
        }
    }

    private async Task DeliverAsync(string itemId, string discussionTurnId, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<DiscussionTurnDelivery>().DeliverAsync(discussionTurnId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Agent Discussion Turn {DiscussionTurnId} delivery failed.", discussionTurnId);
        }
        finally
        {
            _deliveringItems.TryRemove(itemId, out _);
        }
    }
}
