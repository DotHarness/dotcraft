using System.Text;
using DotCraft.Commands.Core;
using DotCraft.Cron;
using DotCraft.Text;

namespace DotCraft.Commands.Handlers;

/// <summary>
/// Handles /cron command to manage scheduled tasks.
/// </summary>
public sealed class CronCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string[] Commands => ["/cron"];
    
    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        if (context.CronService == null)
        {
            await responder.SendTextAsync(FallbackText.CronUnavailable);
            return CommandResult.HandledResult();
        }
        
        var args = context.Arguments;
        var subCmd = args.Length > 0 ? args[0] : "list";
        
        switch (subCmd)
        {
            case "list":
                await HandleListAsync(context.CronService, responder);
                break;
            case "remove":
                await HandleRemoveAsync(context.CronService, args, responder);
                break;
            default:
                await responder.SendTextAsync(FallbackText.CronUsage);
                break;
        }
        
        return CommandResult.HandledResult();
    }
    
    private static async Task HandleListAsync(CronService cronService, ICommandResponder responder)
    {
        var jobs = cronService.ListJobs(includeDisabled: true);
        if (jobs.Count == 0)
        {
            await responder.SendTextAsync(FallbackText.NoCronJobs);
            return;
        }
        
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(FallbackText.CommandCronListTitle, jobs.Count));
        foreach (var job in jobs)
        {
            var status = job.Enabled ? FallbackText.CronEnabled : FallbackText.CronDisabled;
            var schedDesc = job.Schedule.Kind switch
            {
                "at" when job.Schedule.AtMs.HasValue =>
                    $"{FallbackText.CronExecuteOnce} {DateTimeOffset.FromUnixTimeMilliseconds(job.Schedule.AtMs.Value):u}",
                "every" when job.Schedule.EveryMs.HasValue =>
                    $"{FallbackText.CronEvery} {TimeSpan.FromMilliseconds(job.Schedule.EveryMs.Value)}",
                _ => job.Schedule.Kind
            };
            var next = job.State.NextRunAtMs.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(job.State.NextRunAtMs.Value).ToString("u")
                : "-";
            sb.AppendLine($"[{job.Id}] {job.Name} ({status})");
            sb.AppendLine($"  {FallbackText.CronColSchedule}: {schedDesc}");
            sb.AppendLine($"  {FallbackText.CronColNextRun}: {next}");
        }
        
        await responder.SendTextAsync(sb.ToString().TrimEnd());
    }
    
    private static async Task HandleRemoveAsync(CronService cronService, string[] args, ICommandResponder responder)
    {
        if (args.Length < 2)
        {
            await responder.SendTextAsync(FallbackText.CronRemoveUsage);
            return;
        }
        
        var jobId = args[1];
        if (cronService.RemoveJob(jobId))
            await responder.SendTextAsync($"{FallbackText.CronJobDeleted} '{jobId}' {FallbackText.CronJobDeletedSuffix}");
        else
            await responder.SendTextAsync($"{FallbackText.CronJobNotFound} '{jobId}'.");
    }
}
