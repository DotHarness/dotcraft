using DotCraft.Cron;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Handles the <c>cron/*</c> wire methods (spec Section 16): listing, removing, enabling, and
/// running scheduled jobs. Extracted from <see cref="AppServerRequestHandler"/> as part of the
/// Core architecture refactor (M3).
/// </summary>
internal sealed class CronRequestHandler(
    CronService? cronService,
    Action<CronJobWireInfo, bool>? broadcastCronStateChanged) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(AppServerMethods.CronList, HandleCronListAsync);
        table.Map(AppServerMethods.CronRemove, HandleCronRemoveAsync);
        table.Map(AppServerMethods.CronEnable, HandleCronEnableAsync);
        table.Map(AppServerMethods.CronRun, HandleCronRunAsync);
    }

    private Task<object?> HandleCronListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (cronService == null) throw AppServerErrors.MethodNotFound(AppServerMethods.CronList);
        var p = AppServerParams.Get<CronListParams>(msg);
        var jobs = cronService.ListJobs(includeDisabled: p.IncludeDisabled);
        return Task.FromResult<object?>(new CronListResult
        {
            Jobs = jobs.Select(MapCronJob).ToList()
        });
    }

    private Task<object?> HandleCronRemoveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (cronService == null) throw AppServerErrors.MethodNotFound(AppServerMethods.CronRemove);
        var p = AppServerParams.Get<CronRemoveParams>(msg);
        if (string.IsNullOrWhiteSpace(p.JobId))
            throw AppServerErrors.InvalidParams("'jobId' is required.");
        var removed = cronService.RemoveJob(p.JobId);
        if (!removed) throw AppServerErrors.CronJobNotFound(p.JobId);
        broadcastCronStateChanged?.Invoke(new CronJobWireInfo { Id = p.JobId }, true);
        return Task.FromResult<object?>(new CronRemoveResult { Removed = true });
    }

    private Task<object?> HandleCronEnableAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (cronService == null) throw AppServerErrors.MethodNotFound(AppServerMethods.CronEnable);
        var p = AppServerParams.Get<CronEnableParams>(msg);
        if (string.IsNullOrWhiteSpace(p.JobId))
            throw AppServerErrors.InvalidParams("'jobId' is required.");
        var job = cronService.EnableJob(p.JobId, p.Enabled);
        if (job == null) throw AppServerErrors.CronJobNotFound(p.JobId);
        broadcastCronStateChanged?.Invoke(MapCronJob(job), false);
        return Task.FromResult<object?>(new CronEnableResult { Job = MapCronJob(job) });
    }

    private Task<object?> HandleCronRunAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (cronService == null) throw AppServerErrors.MethodNotFound(AppServerMethods.CronRun);
        var p = AppServerParams.Get<CronRunParams>(msg);
        if (string.IsNullOrWhiteSpace(p.JobId))
            throw AppServerErrors.InvalidParams("'jobId' is required.");
        var job = cronService.RunJobNow(p.JobId);
        if (job == null) throw AppServerErrors.CronJobNotFound(p.JobId);
        return Task.FromResult<object?>(new CronRunResult
        {
            Queued = true,
            Job = MapCronJob(job)
        });
    }

    private static CronJobWireInfo MapCronJob(CronJob job) => CronJobWireMapping.ToWire(job);
}
