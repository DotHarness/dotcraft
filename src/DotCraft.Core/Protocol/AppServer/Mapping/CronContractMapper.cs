using DotCraft.Cron;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

/// <summary>Maps Cron runtime state to the stable AppServer contract at the protocol boundary.</summary>
public static class CronContractMapper
{
    public static Contract.CronJobWireInfo ToContract(CronJob job) => new()
    {
        Id = job.Id,
        Name = job.Name,
        Schedule = new Contract.CronScheduleWireInfo
        {
            Kind = job.Schedule.Kind,
            EveryMs = job.Schedule.EveryMs,
            AtMs = job.Schedule.AtMs,
            InitialDelayMs = job.Schedule.InitialDelayMs,
            DailyHour = job.Schedule.DailyHour,
            DailyMinute = job.Schedule.DailyMinute,
            Tz = job.Schedule.Tz
        },
        Enabled = job.Enabled,
        CreatedAtMs = job.CreatedAtMs,
        DeleteAfterRun = job.DeleteAfterRun,
        State = new Contract.CronJobStateWireInfo
        {
            NextRunAtMs = job.State.NextRunAtMs,
            LastRunAtMs = job.State.LastRunAtMs,
            LastStatus = job.State.LastStatus,
            LastError = job.State.LastError,
            LastThreadId = job.State.LastThreadId,
            LastResult = job.State.LastResult
        }
    };
}
