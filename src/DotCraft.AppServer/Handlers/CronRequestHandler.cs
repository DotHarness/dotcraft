using DotCraft.Cron;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

/// <summary>
/// Handles the <c>cron/*</c> wire methods (spec Section 16): listing, removing, enabling, and
/// running scheduled jobs.
/// </summary>
internal sealed class CronRequestHandler(
    CronService? cronService,
    Action<Contract.CronJobWireInfo, bool>? broadcastCronStateChanged) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Protocol.AppServer.AppServerRpc.CronList, HandleCronListAsync);
        table.Map(Protocol.AppServer.AppServerRpc.CronRemove, HandleCronRemoveAsync);
        table.Map(Protocol.AppServer.AppServerRpc.CronEnable, HandleCronEnableAsync);
        table.Map(Protocol.AppServer.AppServerRpc.CronRun, HandleCronRunAsync);
    }

    private Task<AppServerTypedResult<Contract.CronListResult>> HandleCronListAsync(
        AppServerTypedRequest<Contract.CronListParams> request,
        CancellationToken ct)
    {
        if (cronService == null) throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.CronList);
        _ = ct;
        var includeDisabled = request.Params.IncludeDisabled.IsSet && request.Params.IncludeDisabled.Value;
        var jobs = cronService.ListJobs(includeDisabled);
        return Task.FromResult(AppServerTypedResult<Contract.CronListResult>.FromResult(new Contract.CronListResult
        {
            Jobs = jobs.Select(MapCronJob).ToList()
        }));
    }

    private Task<AppServerTypedResult<Contract.CronRemoveResult>> HandleCronRemoveAsync(
        AppServerTypedRequest<Contract.CronRemoveParams> request,
        CancellationToken ct)
    {
        if (cronService == null) throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.CronRemove);
        _ = ct;
        var jobId = request.Params.JobId.IsSet ? request.Params.JobId.Value : null;
        if (string.IsNullOrWhiteSpace(jobId))
            throw AppServerErrors.InvalidParams("'jobId' is required.");
        var removed = cronService.RemoveJob(jobId);
        if (!removed) throw AppServerErrors.CronJobNotFound(jobId);
        broadcastCronStateChanged?.Invoke(new Contract.CronJobWireInfo { Id = jobId }, true);
        return Task.FromResult(AppServerTypedResult<Contract.CronRemoveResult>.FromResult(
            new Contract.CronRemoveResult { Removed = true }));
    }

    private Task<AppServerTypedResult<Contract.CronEnableResult>> HandleCronEnableAsync(
        AppServerTypedRequest<Contract.CronEnableParams> request,
        CancellationToken ct)
    {
        if (cronService == null) throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.CronEnable);
        _ = ct;
        var jobId = request.Params.JobId.IsSet ? request.Params.JobId.Value : null;
        if (string.IsNullOrWhiteSpace(jobId))
            throw AppServerErrors.InvalidParams("'jobId' is required.");
        var enabled = request.Params.Enabled.IsSet && request.Params.Enabled.Value;
        var job = cronService.EnableJob(jobId, enabled);
        if (job == null) throw AppServerErrors.CronJobNotFound(jobId);
        broadcastCronStateChanged?.Invoke(MapCronJob(job), false);
        return Task.FromResult(AppServerTypedResult<Contract.CronEnableResult>.FromResult(
            new Contract.CronEnableResult { Job = MapCronJob(job) }));
    }

    private Task<AppServerTypedResult<Contract.CronRunResult>> HandleCronRunAsync(
        AppServerTypedRequest<Contract.CronRunParams> request,
        CancellationToken ct)
    {
        if (cronService == null) throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.CronRun);
        _ = ct;
        var jobId = request.Params.JobId.IsSet ? request.Params.JobId.Value : null;
        if (string.IsNullOrWhiteSpace(jobId))
            throw AppServerErrors.InvalidParams("'jobId' is required.");
        var job = cronService.RunJobNow(jobId);
        if (job == null) throw AppServerErrors.CronJobNotFound(jobId);
        return Task.FromResult(AppServerTypedResult<Contract.CronRunResult>.FromResult(new Contract.CronRunResult
        {
            Queued = true,
            Job = MapCronJob(job)
        }));
    }

    private static Contract.CronJobWireInfo MapCronJob(CronJob job) => CronContractMapper.ToContract(job);
}
