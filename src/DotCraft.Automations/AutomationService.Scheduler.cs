using Microsoft.Extensions.Logging;

namespace DotCraft.Automations;

public sealed partial class AutomationService
{
    /// <summary>Recovers interrupted attempts and starts the host-owned scheduler.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
        if (_lifetime != null) throw new InvalidOperationException("automation.alreadyStarted");
        await LoadAsync(ct);
        foreach (var definition in _definitions.Values.ToArray())
        {
            var recovered = definition;
            foreach (var run in await _store.RunsAsync(definition.Id, ct))
            {
                if (run.ScheduledAt != null && run.DefinitionVersion == recovered.Version && recovered.NextRunAt <= run.ScheduledAt)
                    recovered = recovered with { NextRunAt = recovered.Schedule.Next(DateTimeOffset.UtcNow, run.ScheduledAt) };
                if (run.Status is "running" or "queued")
                    await _store.SaveRunAsync(run with { Status = "interrupted", CompletedAt = DateTimeOffset.UtcNow,
                        Error = "automation.hostInterrupted", DeliveryStatus = "skipped" }, ct);
                else if (run.DeliveryStatus == "pending")
                    await _store.SaveRunAsync(run with { DeliveryStatus = "failed", DeliveryError = "automation.deliveryInterrupted" }, ct);
            }
            if (recovered.Status == "active" && recovered.Schedule.Kind == "at" && recovered.NextRunAt == null)
            {
                recovered = CompleteOneShot(recovered);
            }
            await _store.SaveAsync(recovered, ct);
            _definitions[recovered.Id] = recovered;
        }
        _lifetime = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_lifetime.Token));
        _acceptingRuns = true;
        }
        finally { _gate.Release(); }
    }
    /// <summary>Stops dispatch and waits until running attempts have persisted their terminal states.</summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        CancellationTokenSource lifetime;
        Task? loop;
        await _gate.WaitAsync(ct);
        try
        {
            if (_lifetime == null) return;
            _acceptingRuns = false;
            lifetime = _lifetime;
            loop = _loop;
        }
        finally { _gate.Release(); }
        await lifetime.CancelAsync();
        if (loop != null) await loop.WaitAsync(ct);
        Task[] running;
        await _gate.WaitAsync(ct);
        try { running = _running.Values.ToArray(); }
        finally { _gate.Release(); }
        await Task.WhenAll(running).WaitAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            lifetime.Dispose();
            _lifetime = null;
            _loop = null;
        }
        finally { _gate.Release(); }
    }
    private async Task LoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(config.PollingInterval > TimeSpan.Zero ? config.PollingInterval : TimeSpan.FromSeconds(5));
        try
        {
            do { await PollAsync(ct); } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
    /// <summary>Checks due definitions; missed occurrences collapse into one attempt.</summary>
    public async Task PollAsync(CancellationToken ct = default)
    {
        foreach (var definition in await ListAsync(ct))
        {
            if (definition.Status != "active" || definition.NextRunAt == null || definition.NextRunAt > DateTimeOffset.UtcNow) continue;
            try { await QueueAsync(definition.Id, false, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogWarning(ex, "Unable to queue automation {Id}", definition.Id); }
        }
        try { await SweepWorktreesAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Automation worktree retention failed"); }
    }
    /// <summary>Queues one manual attempt without changing the periodic schedule or paused state.</summary>
    public Task<AutomationRun> RunAsync(string id, CancellationToken ct = default) => QueueAsync(id, true, ct);
    private async Task<AutomationRun> QueueAsync(string id, bool manual, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await LoadAsync(ct);
            var definition = Get(id);
            if (_client == null || _lifetime == null || !_acceptingRuns) throw new InvalidOperationException("automation.hostOffline");
            if (_running.ContainsKey(id))
            {
                if (manual) throw new InvalidOperationException("automation.alreadyRunning");
                return (await _store.RunsAsync(id, ct)).First();
            }
            if (!manual && (definition.Status != "active" || definition.NextRunAt == null || definition.NextRunAt > DateTimeOffset.UtcNow))
                throw new InvalidOperationException("automation.notDue");
            var run = new AutomationRun { Id = Guid.NewGuid().ToString("N"), AutomationId = id,
                DefinitionVersion = definition.Version, CreatedAt = DateTimeOffset.UtcNow, ScheduledAt = manual ? null : definition.NextRunAt };
            await _store.SaveRunAsync(run, ct);
            if (!manual)
            {
                var advanced = definition with { NextRunAt = definition.Schedule.Next(DateTimeOffset.UtcNow, definition.NextRunAt) };
                await _store.SaveAsync(advanced, ct); _definitions[id] = advanced;
            }
            var token = _lifetime.Token;
            _running[id] = Task.Run(() => ExecuteAsync(definition, run, manual, token), CancellationToken.None);
            return run;
        }
        finally { _gate.Release(); }
    }
}
