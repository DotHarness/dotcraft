using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;
using Oratorio.Server.GitHub;
using Oratorio.Server.GitLab;
using Oratorio.Server.Realtime;
using Oratorio.Server.Services;

namespace Oratorio.Server.Sources;

/// <summary>
/// Persists provider-level source sync schedules and enqueues due incremental sync work.
/// </summary>
public sealed class SourceSyncSchedulerService(
    OratorioDbContext db,
    SourceProviderService sourceProviders,
    GitHubSyncCoordinator gitHubSync,
    IOptionsMonitor<GitHubOptions> gitHubOptions,
    GitLabSyncCoordinator gitLabSync,
    IClock clock,
    BoardEventHub boardEvents,
    ILogger<SourceSyncSchedulerService> logger)
{
    public const int DefaultIntervalSeconds = 300;
    public const int MinIntervalSeconds = 60;
    public const int MaxIntervalSeconds = 86400;

    private static readonly string[] KnownProviders = ["github", "gitlab"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Gets all provider schedules, creating default disabled rows when necessary.
    /// </summary>
    public async Task<SourceSyncSchedulesResponse> GetSchedulesAsync(CancellationToken ct)
    {
        await EnsureDefaultSchedulesAsync(ct);
        var schedules = await db.SourceSyncSchedules
            .AsNoTracking()
            .OrderBy(x => x.Provider)
            .ToArrayAsync(ct);
        var dtos = new List<SourceSyncScheduleDto>();
        foreach (var schedule in schedules.Where(x => KnownProviders.Contains(x.Provider, StringComparer.OrdinalIgnoreCase)))
        {
            dtos.Add(await ToDtoAsync(schedule, ct));
        }

        return new SourceSyncSchedulesResponse(clock.UtcNow, dtos);
    }

    /// <summary>
    /// Gets a single provider schedule, creating its default disabled row when necessary.
    /// </summary>
    public async Task<SourceSyncScheduleDto> GetScheduleAsync(string provider, CancellationToken ct)
    {
        var normalized = NormalizeProvider(provider);
        var schedule = await GetOrCreateScheduleAsync(normalized, ct);
        return await ToDtoAsync(schedule, ct);
    }

    /// <summary>
    /// Updates a provider schedule. Enabled schedules first run at now plus the configured interval.
    /// </summary>
    public async Task<SourceSyncScheduleDto> UpdateScheduleAsync(
        string provider,
        SourceSyncScheduleUpdateRequest request,
        CancellationToken ct)
    {
        var normalized = NormalizeProvider(provider);
        var schedule = await GetOrCreateScheduleAsync(normalized, ct);
        var interval = request.IntervalSeconds ?? schedule.IntervalSeconds;
        ValidateInterval(interval);

        var now = clock.UtcNow;
        if (request.Enabled)
        {
            var status = await sourceProviders.GetStatusAsync(normalized, ct);
            if (!status.ReadCapability.Available)
            {
                throw new OratorioApiException(
                    StatusCodes.Status400BadRequest,
                    "sourceSyncScheduleUnavailable",
                    status.ReadCapability.Reason ?? $"{status.DisplayName} read sync must be configured before scheduled sync can be enabled.",
                    new Dictionary<string, object?> { ["provider"] = normalized });
            }

            var reschedule = !schedule.Enabled ||
                schedule.IntervalSeconds != interval ||
                schedule.NextRunAt is null;
            schedule.Enabled = true;
            schedule.IntervalSeconds = interval;
            if (reschedule)
            {
                schedule.NextRunAt = now.AddSeconds(interval);
            }
        }
        else
        {
            schedule.Enabled = false;
            schedule.IntervalSeconds = interval;
            schedule.NextRunAt = null;
        }

        schedule.LastErrorCode = null;
        schedule.LastErrorMessage = null;
        schedule.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        var dto = await ToDtoAsync(schedule, ct);
        PublishSchedule(dto, now);
        return dto;
    }

    /// <summary>
    /// Processes all due enabled schedules once, coalescing active sync work and missed intervals.
    /// </summary>
    public async Task<int> ProcessDueSchedulesAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var due = await db.SourceSyncSchedules
            .Where(x => x.Enabled && x.NextRunAt != null && x.NextRunAt <= now)
            .OrderBy(x => x.NextRunAt)
            .ToArrayAsync(ct);
        var processed = 0;

        foreach (var schedule in due)
        {
            try
            {
                await ProcessDueScheduleAsync(schedule, now, ct);
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Scheduled source sync failed for provider {Provider}.", schedule.Provider);
                await RecordFailureAsync(
                    schedule,
                    now,
                    "sourceSyncScheduleFailed",
                    ex.Message,
                    ct);
                processed++;
            }
        }

        return processed;
    }

    private async Task ProcessDueScheduleAsync(OratorioSourceSyncSchedule schedule, DateTimeOffset now, CancellationToken ct)
    {
        var status = await sourceProviders.GetStatusAsync(schedule.Provider, ct);
        if (!status.ReadCapability.Available)
        {
            await RecordFailureAsync(
                schedule,
                now,
                "sourceSyncScheduleUnavailable",
                status.ReadCapability.Reason ?? $"{status.DisplayName} read sync is not available.",
                ct);
            return;
        }

        var active = await sourceProviders.GetActiveSyncJobAsync(schedule.Provider, ct);
        if (active is not null)
        {
            MarkScheduled(schedule, now, active.JobId);
            await db.SaveChangesAsync(ct);
            PublishSchedule(await ToDtoAsync(schedule, ct), now);
            return;
        }

        var job = await EnqueueScheduledJobAsync(schedule.Provider, ct);
        MarkScheduled(schedule, now, job.JobId);
        await db.SaveChangesAsync(ct);
        PublishSchedule(await ToDtoAsync(schedule, ct), now);
    }

    private async Task<SourceSyncJobDto> EnqueueScheduledJobAsync(string provider, CancellationToken ct)
    {
        if (provider == "github")
        {
            var job = await gitHubSync.EnqueueAsync(GitHubSyncTrigger.Scheduled, GitHubSyncMode.Incremental, null, ct);
            return SourceSyncMapper.FromGitHub(job, gitHubOptions.CurrentValue.Endpoint);
        }

        if (provider == "gitlab")
        {
            return await gitLabSync.EnqueueAsync(SourceSyncTrigger.Scheduled, SourceSyncMode.Incremental, null, ct);
        }

        throw UnknownProvider(provider);
    }

    private void MarkScheduled(OratorioSourceSyncSchedule schedule, DateTimeOffset now, string jobId)
    {
        schedule.LastScheduledAt = now;
        schedule.LastJobId = jobId;
        schedule.LastErrorCode = null;
        schedule.LastErrorMessage = null;
        schedule.NextRunAt = now.AddSeconds(CoerceInterval(schedule.IntervalSeconds));
        schedule.UpdatedAt = now;
    }

    private async Task RecordFailureAsync(
        OratorioSourceSyncSchedule schedule,
        DateTimeOffset now,
        string code,
        string message,
        CancellationToken ct)
    {
        schedule.LastErrorCode = code;
        schedule.LastErrorMessage = message;
        schedule.NextRunAt = now.AddSeconds(CoerceInterval(schedule.IntervalSeconds));
        schedule.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        PublishSchedule(await ToDtoAsync(schedule, ct), now);
    }

    private async Task EnsureDefaultSchedulesAsync(CancellationToken ct)
    {
        foreach (var provider in KnownProviders)
        {
            await GetOrCreateScheduleAsync(provider, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<OratorioSourceSyncSchedule> GetOrCreateScheduleAsync(string provider, CancellationToken ct)
    {
        var normalized = NormalizeProvider(provider);
        var existing = await db.SourceSyncSchedules.FirstOrDefaultAsync(x => x.Provider == normalized, ct);
        if (existing is not null)
        {
            return existing;
        }

        var now = clock.UtcNow;
        var schedule = new OratorioSourceSyncSchedule
        {
            Provider = normalized,
            Enabled = false,
            IntervalSeconds = DefaultIntervalSeconds,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.SourceSyncSchedules.Add(schedule);
        await db.SaveChangesAsync(ct);
        return schedule;
    }

    private async Task<SourceSyncScheduleDto> ToDtoAsync(OratorioSourceSyncSchedule schedule, CancellationToken ct)
    {
        var status = await sourceProviders.GetStatusAsync(schedule.Provider, ct);
        var lastJob = await ResolveLastJobAsync(schedule.Provider, schedule.LastJobId, ct);
        return new SourceSyncScheduleDto(
            schedule.Provider,
            schedule.Enabled,
            schedule.IntervalSeconds,
            schedule.NextRunAt,
            schedule.LastScheduledAt,
            schedule.LastJobId,
            lastJob.Status,
            lastJob.CompletedAt,
            schedule.LastErrorCode,
            schedule.LastErrorMessage,
            status.ReadCapability.Available,
            status.ReadCapability.Available ? null : status.ReadCapability.Reason,
            schedule.UpdatedAt);
    }

    private async Task<(SourceSyncStatus? Status, DateTimeOffset? CompletedAt)> ResolveLastJobAsync(
        string provider,
        string? jobId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return (null, null);
        }

        if (provider == "github")
        {
            var job = await db.GitHubSyncJobs
                .AsNoTracking()
                .Where(x => x.JobId == jobId)
                .Select(x => new { x.Status, x.CompletedAt })
                .FirstOrDefaultAsync(ct);
            return job is null ? (null, null) : (MapGitHubStatus(job.Status), job.CompletedAt);
        }

        if (provider == "gitlab")
        {
            var job = await db.GitLabSyncJobs
                .AsNoTracking()
                .Where(x => x.JobId == jobId)
                .Select(x => new { x.Status, x.CompletedAt })
                .FirstOrDefaultAsync(ct);
            return job is null ? (null, null) : (job.Status, job.CompletedAt);
        }

        throw UnknownProvider(provider);
    }

    private void PublishSchedule(SourceSyncScheduleDto dto, DateTimeOffset timestamp)
    {
        boardEvents.PublishSourceSync(new SourceSyncEvent(
            SourceSyncEvent.SourceSyncScheduleUpdatedType,
            JsonSerializer.SerializeToElement(dto, JsonOptions),
            timestamp));
    }

    private static SourceSyncStatus MapGitHubStatus(GitHubSyncStatus status) =>
        status switch
        {
            GitHubSyncStatus.Queued => SourceSyncStatus.Queued,
            GitHubSyncStatus.Running => SourceSyncStatus.Running,
            GitHubSyncStatus.Succeeded => SourceSyncStatus.Succeeded,
            GitHubSyncStatus.PartialFailed => SourceSyncStatus.PartialFailed,
            GitHubSyncStatus.Failed => SourceSyncStatus.Failed,
            _ => SourceSyncStatus.Failed
        };

    private static int CoerceInterval(int intervalSeconds) =>
        intervalSeconds is < MinIntervalSeconds or > MaxIntervalSeconds
            ? DefaultIntervalSeconds
            : intervalSeconds;

    private static void ValidateInterval(int intervalSeconds)
    {
        if (intervalSeconds is >= MinIntervalSeconds and <= MaxIntervalSeconds)
        {
            return;
        }

        throw OratorioApiException.Validation(
            $"intervalSeconds must be between {MinIntervalSeconds} and {MaxIntervalSeconds}.",
            new Dictionary<string, object?>
            {
                ["field"] = "intervalSeconds",
                ["min"] = MinIntervalSeconds,
                ["max"] = MaxIntervalSeconds
            });
    }

    private static string NormalizeProvider(string provider)
    {
        var normalized = string.IsNullOrWhiteSpace(provider) ? "" : provider.Trim().ToLowerInvariant();
        if (!KnownProviders.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            throw UnknownProvider(normalized);
        }

        return normalized;
    }

    private static OratorioApiException UnknownProvider(string provider) =>
        new(
            StatusCodes.Status404NotFound,
            "sourceProviderNotFound",
            $"Source provider '{provider}' is not configured.",
            new Dictionary<string, object?> { ["provider"] = provider });
}
