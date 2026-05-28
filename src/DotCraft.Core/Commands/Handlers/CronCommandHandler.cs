using System.Text;
using DotCraft.Commands.Core;
using DotCraft.Cron;
using DotCraft.Localization;

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
            await responder.SendTextAsync(Strings.CronUnavailable);
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
                await responder.SendTextAsync(Strings.CronUsage);
                break;
        }
        
        return CommandResult.HandledResult();
    }
    
    private static async Task HandleListAsync(CronService cronService, ICommandResponder responder)
    {
        var jobs = cronService.ListJobs(includeDisabled: true);
        if (jobs.Count == 0)
        {
            await responder.SendTextAsync(Strings.NoCronJobs);
            return;
        }
        
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(Strings.CommandCronListTitle, jobs.Count));
        foreach (var job in jobs)
        {
            var status = job.Enabled ? Strings.CronEnabled : Strings.CronDisabled;
            var schedDesc = job.Schedule.Kind switch
            {
                "at" when job.Schedule.AtMs.HasValue =>
                    $"{Strings.CronExecuteOnce} {DateTimeOffset.FromUnixTimeMilliseconds(job.Schedule.AtMs.Value):u}",
                "every" when job.Schedule.EveryMs.HasValue =>
                    $"{Strings.CronEvery} {TimeSpan.FromMilliseconds(job.Schedule.EveryMs.Value)}",
                _ => job.Schedule.Kind
            };
            var next = job.State.NextRunAtMs.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(job.State.NextRunAtMs.Value).ToString("u")
                : "-";
            sb.AppendLine($"[{job.Id}] {job.Name} ({status})");
            sb.AppendLine($"  {Strings.CronColSchedule}: {schedDesc}");
            sb.AppendLine($"  {Strings.CronColNextRun}: {next}");
        }
        
        await responder.SendTextAsync(sb.ToString().TrimEnd());
    }
    
    private static async Task HandleRemoveAsync(CronService cronService, string[] args, ICommandResponder responder)
    {
        if (args.Length < 2)
        {
            await responder.SendTextAsync(Strings.CronRemoveUsage);
            return;
        }
        
        var jobId = args[1];
        if (cronService.RemoveJob(jobId))
            await responder.SendTextAsync($"{Strings.CronJobDeleted} '{jobId}' {Strings.CronJobDeletedSuffix}");
        else
            await responder.SendTextAsync($"{Strings.CronJobNotFound} '{jobId}'.");
    }
}
