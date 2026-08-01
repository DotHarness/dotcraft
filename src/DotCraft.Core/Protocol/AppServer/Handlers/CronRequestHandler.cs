using DotCraft.Cron;
using DotCraft.Heartbeat;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Handles the <c>cron/*</c> wire methods (spec Section 16): listing, removing, enabling, and
/// running scheduled jobs.
/// </summary>
internal sealed class CronRequestHandler(
    CronService? cronService,
    HeartbeatService? heartbeatService,
    Action<CronJobWireInfo, bool>? broadcastCronStateChanged) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.CronList, HandleCronListAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.CronRemove, HandleCronRemoveAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.CronEnable, HandleCronEnableAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.CronRun, HandleCronRunAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.HeartbeatTrigger, HandleHeartbeatTriggerAsync);
    }

    private Task<object?> HandleCronListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (cronService == null) throw AppServerErrors.MethodNotFound(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.CronList);
        var p = AppServerParams.Get<CronListParams>(msg);
        var jobs = cronService.ListJobs(includeDisabled: p.IncludeDisabled);
        return Task.FromResult<object?>(new CronListResult
        {
            Jobs = jobs.Select(MapCronJob).ToList()
        });
    }

    private Task<object?> HandleCronRemoveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (cronService == null) throw AppServerErrors.MethodNotFound(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.CronRemove);
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
        if (cronService == null) throw AppServerErrors.MethodNotFound(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.CronEnable);
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
        if (cronService == null) throw AppServerErrors.MethodNotFound(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.CronRun);
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

    private async Task<object?> HandleHeartbeatTriggerAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        _ = ct;
        if (heartbeatService == null)
            throw AppServerErrors.MethodNotFound(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.HeartbeatTrigger);

        try
        {
            var result = await heartbeatService.TriggerNowAsync();
            return new HeartbeatTriggerResult { Result = result };
        }
        catch (Exception ex)
        {
            return new HeartbeatTriggerResult { Error = ex.Message };
        }
    }

    private static CronJobWireInfo MapCronJob(CronJob job) => CronJobWireMapping.ToWire(job);
}
