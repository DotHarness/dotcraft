using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;
using Oratorio.Server.DotCraft;
using Oratorio.Server.Realtime;

namespace Oratorio.Server.Services;

public sealed class AutoReviewDispatchService(
    OratorioDbContext db,
    OratorioService oratorio,
    IDotCraftWorkspaceResolver workspaceResolver,
    IOptionsMonitor<OratorioAutomationOptions> options,
    IClock clock,
    BoardEventHub boardEvents,
    ILogger<AutoReviewDispatchService> logger)
{
    private static readonly RunStatus[] ActiveRunStatuses = [RunStatus.Queued, RunStatus.Dispatching, RunStatus.Running];
    private static readonly RunStatus[] ExistingRunStatuses = [RunStatus.Queued, RunStatus.Dispatching, RunStatus.Running, RunStatus.Succeeded];

    public async Task DispatchEligibleAsync(CancellationToken ct)
    {
        var repositories = NormalizeRepositories(options.CurrentValue.AutoReviewRepositories);
        var now = clock.UtcNow;
        await MarkDisabledRepositoriesAsync(repositories, now, ct);
        foreach (var repository in repositories)
        {
            var state = await FindRepositoryStateAsync(repository.ConfigKey, ct);
            if (state is null)
            {
                state = new OratorioAutoReviewRepositoryState
                {
                    Repository = repository.ConfigKey,
                    Enabled = true,
                    InitializedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.AutoReviewRepositoryStates.Add(state);
                await BaselineRepositoryAsync(repository, now, ct);
                await db.SaveChangesAsync(ct);
                continue;
            }

            if (!state.Enabled)
            {
                state.Enabled = true;
                state.InitializedAt = now;
                state.UpdatedAt = now;
                await BaselineRepositoryAsync(repository, now, ct);
                await db.SaveChangesAsync(ct);
                continue;
            }

            await ScanRepositoryAsync(repository, now, ct);
        }
    }

    private async Task MarkDisabledRepositoriesAsync(IReadOnlyList<AutoReviewProject> enabledRepositories, DateTimeOffset now, CancellationToken ct)
    {
        var enabled = enabledRepositories.Select(x => x.ConfigKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var states = await db.AutoReviewRepositoryStates.Where(x => x.Enabled).ToListAsync(ct);
        foreach (var state in states)
        {
            if (enabled.Contains(state.Repository))
            {
                continue;
            }

            state.Enabled = false;
            state.UpdatedAt = now;
        }

        if (states.Any(x => !x.Enabled))
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task BaselineRepositoryAsync(AutoReviewProject repository, DateTimeOffset now, CancellationToken ct)
    {
        var items = await QueryRepositoryPullRequests(repository)
            .Where(x => x.SourceState == SourceState.Open && !x.IsDraft)
            .ToListAsync(ct);

        foreach (var item in items)
        {
            var headSha = NullIfWhiteSpace(item.HeadSha);
            var state = UpsertItemState(item.ItemId, repository.ConfigKey, headSha, headSha, null, null, null, null, now);
            ClearItemError(state);
        }
    }

    private async Task ScanRepositoryAsync(AutoReviewProject repository, DateTimeOffset now, CancellationToken ct)
    {
        var items = await QueryRepositoryPullRequests(repository)
            .OrderBy(x => x.UpdatedAt)
            .Take(200)
            .ToListAsync(ct);

        foreach (var item in items)
        {
            await ScanPullRequestAsync(item, repository, now, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task ScanPullRequestAsync(OratorioItem item, AutoReviewProject repository, DateTimeOffset now, CancellationToken ct)
    {
        var headSha = NullIfWhiteSpace(item.HeadSha);
        var state = UpsertItemState(item.ItemId, repository.ConfigKey, headSha, null, null, null, null, null, now);

        if (!IsEligiblePullRequest(item))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(headSha))
        {
            RecordItemError(state, item, "headShaRequired", $"Auto Review requires the current {ReviewTargetName(repository.Source)} head SHA.", now);
            return;
        }

        if (await HasActiveReviewRunAsync(item.ItemId, ct))
        {
            state.LastObservedHeadSha = headSha;
            state.UpdatedAt = now;
            return;
        }

        try
        {
            _ = workspaceResolver.ResolveWorkspacePath(item.Repository);
        }
        catch (DotCraftWorkspaceResolutionException ex)
        {
            RecordItemError(state, item, ex.Code, ex.Message, now);
            return;
        }

        if (await HasReviewRunForHeadAsync(item.ItemId, headSha, ct))
        {
            state.LastObservedHeadSha = headSha;
            state.LastQueuedHeadSha = headSha;
            state.LastErrorCode = null;
            state.LastErrorMessage = null;
            state.LastErrorAt = null;
            state.UpdatedAt = now;
            return;
        }

        if (string.Equals(state.LastQueuedHeadSha, headSha, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var latestSuccessfulRun = await LatestSuccessfulReviewRunAsync(item.ItemId, ct);
            ItemDetailResponse detail;
            if (latestSuccessfulRun is not null && !HeadMatches(latestSuccessfulRun, headSha) && item.CurrentRound > 0)
            {
                detail = await oratorio.ReReviewPullRequestByIdAsync(item.ItemId, RunDispatchTrigger.AutoReview, ct);
            }
            else if (latestSuccessfulRun is null || !HeadMatches(latestSuccessfulRun, headSha))
            {
                detail = await oratorio.DispatchByIdAsync(
                    item.ItemId,
                    new DispatchRequest(
                        "appServer",
                        "Oratorio auto review queued this pull request for the latest head.",
                        null,
                        null,
                        "reviewAnalysis",
                        DeliveryPolicy.ManualDelivery),
                    RunDispatchTrigger.AutoReview,
                    ct);
            }
            else
            {
                state.LastObservedHeadSha = headSha;
                state.LastQueuedHeadSha = headSha;
                state.UpdatedAt = now;
                return;
            }

            var latestRun = detail.Runs
                .OrderByDescending(x => x.StartedAt ?? x.CompletedAt ?? DateTimeOffset.MinValue)
                .FirstOrDefault(x => x.RunnerKind == "appServer" && x.Purpose == RunPurpose.ReviewAnalysis);
            state.LastObservedHeadSha = headSha;
            state.LastQueuedHeadSha = headSha;
            state.LastQueuedRunId = latestRun?.RunId;
            state.LastErrorCode = null;
            state.LastErrorMessage = null;
            state.LastErrorAt = null;
            state.UpdatedAt = now;
        }
        catch (OratorioApiException ex) when (ex.Code is "activeRunExists" or "invalidTransition" or "sourceDetailsRequired")
        {
            logger.LogDebug("Skipped auto review for {ItemId}: {Code}.", item.ItemId, ex.Code);
            RecordItemError(state, item, ex.Code, ex.Message, now);
        }
    }

    private IQueryable<OratorioItem> QueryRepositoryPullRequests(AutoReviewProject repository)
    {
        var normalized = repository.Repository.ToLowerInvariant();
        var source = repository.Source.ToLowerInvariant();
        return db.Items.AsNoTracking()
            .Where(x =>
                x.Source == source &&
                x.Kind == ItemKind.PullRequest &&
                x.Repository != null &&
                x.Repository.ToLower() == normalized);
    }

    private static bool IsEligiblePullRequest(OratorioItem item) =>
        (item.Source == "github" || item.Source == "gitlab") &&
        item.Kind == ItemKind.PullRequest &&
        !item.IsDraft &&
        item.SourceState == SourceState.Open &&
        item.State is not (ItemState.Archived or ItemState.Rejected);

    private async Task<bool> HasActiveReviewRunAsync(string itemId, CancellationToken ct) =>
        await db.Runs.AsNoTracking().AnyAsync(x =>
            x.ItemId == itemId &&
            x.RunnerKind == "appServer" &&
            x.Purpose == RunPurpose.ReviewAnalysis &&
            ActiveRunStatuses.Contains(x.Status),
            ct);

    private async Task<bool> HasReviewRunForHeadAsync(string itemId, string headSha, CancellationToken ct) =>
        await db.Runs.AsNoTracking().AnyAsync(x =>
            x.ItemId == itemId &&
            x.RunnerKind == "appServer" &&
            x.Purpose == RunPurpose.ReviewAnalysis &&
            ExistingRunStatuses.Contains(x.Status) &&
            ((x.TargetHeadSha != null && x.TargetHeadSha == headSha) ||
             (x.BaseSha != null && x.BaseSha == headSha)),
            ct);

    private async Task<OratorioRun?> LatestSuccessfulReviewRunAsync(string itemId, CancellationToken ct) =>
        await db.Runs.AsNoTracking()
            .Where(x =>
                x.ItemId == itemId &&
                x.RunnerKind == "appServer" &&
                x.Purpose == RunPurpose.ReviewAnalysis &&
                x.Status == RunStatus.Succeeded)
            .OrderByDescending(x => x.CompletedAt ?? x.StartedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefaultAsync(ct);

    private static bool HeadMatches(OratorioRun run, string headSha) =>
        string.Equals(run.TargetHeadSha, headSha, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(run.BaseSha, headSha, StringComparison.OrdinalIgnoreCase);

    private OratorioAutoReviewItemState UpsertItemState(
        string itemId,
        string repository,
        string? lastObservedHeadSha,
        string? lastQueuedHeadSha,
        string? lastQueuedRunId,
        string? lastErrorCode,
        string? lastErrorMessage,
        DateTimeOffset? lastErrorAt,
        DateTimeOffset now)
    {
        var state = db.AutoReviewItemStates.Local.FirstOrDefault(x => x.ItemId == itemId)
            ?? db.AutoReviewItemStates.FirstOrDefault(x => x.ItemId == itemId);
        if (state is null)
        {
            state = new OratorioAutoReviewItemState
            {
                ItemId = itemId,
                Repository = repository,
                CreatedAt = now
            };
            db.AutoReviewItemStates.Add(state);
        }

        state.Repository = repository;
        state.LastObservedHeadSha = lastObservedHeadSha ?? state.LastObservedHeadSha;
        state.LastQueuedHeadSha = lastQueuedHeadSha ?? state.LastQueuedHeadSha;
        state.LastQueuedRunId = lastQueuedRunId ?? state.LastQueuedRunId;
        if (lastErrorCode is not null || lastErrorMessage is not null || lastErrorAt is not null)
        {
            state.LastErrorCode = lastErrorCode;
            state.LastErrorMessage = lastErrorMessage;
            state.LastErrorAt = lastErrorAt;
        }
        state.UpdatedAt = now;
        return state;
    }

    private static void ClearItemError(OratorioAutoReviewItemState state)
    {
        state.LastErrorCode = null;
        state.LastErrorMessage = null;
        state.LastErrorAt = null;
    }

    private void RecordItemError(OratorioAutoReviewItemState state, OratorioItem item, string code, string message, DateTimeOffset now)
    {
        var changed = !string.Equals(state.LastErrorCode, code, StringComparison.Ordinal) ||
            !string.Equals(state.LastErrorMessage, message, StringComparison.Ordinal);
        state.LastErrorCode = code;
        state.LastErrorMessage = message;
        state.LastErrorAt = now;
        state.UpdatedAt = now;
        if (!changed)
        {
            return;
        }

        db.TimelineEvents.Add(new OratorioTimelineEvent
        {
            ItemId = item.ItemId,
            Kind = TimelineEventKind.CheckUpdated,
            ActorKind = ActorKind.System,
            ActorName = "oratorio/auto-review",
            Title = "Auto review skipped",
            Body = message,
            MetadataJson = JsonSerializer.Serialize(new { code, item.HeadSha }),
            CreatedAt = now
        });
        boardEvents.PublishTaskUpdated(item, now);
    }

    private async Task<OratorioAutoReviewRepositoryState?> FindRepositoryStateAsync(string repository, CancellationToken ct)
    {
        var normalized = repository.ToLowerInvariant();
        return await db.AutoReviewRepositoryStates.FirstOrDefaultAsync(x => x.Repository.ToLower() == normalized, ct);
    }

    private static IReadOnlyList<AutoReviewProject> NormalizeRepositories(IEnumerable<string> repositories) =>
        repositories
            .Select(ParseAutoReviewProject)
            .Where(x => x is not null)
            .Select(x => x!)
            .GroupBy(x => x.ConfigKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Replace('\\', '/').Trim('/');

    private static AutoReviewProject? ParseAutoReviewProject(string value)
    {
        var normalized = NullIfWhiteSpace(value);
        if (normalized is null)
        {
            return null;
        }

        if (Oratorio.Server.Sources.SourceProjectKey.TryParse(normalized, out var key))
        {
            var itemRepository = string.Equals(key.Provider, "github", StringComparison.OrdinalIgnoreCase)
                ? key.ProjectPath
                : key.Key;
            return new AutoReviewProject(key.Key, key.Provider, itemRepository);
        }

        var repository = Oratorio.Server.Sources.SourceProjectKey.NormalizeGitHubRepository(normalized);
        return repository is null ? null : new AutoReviewProject(repository, "github", repository);
    }

    private static string ReviewTargetName(string source) =>
        string.Equals(source, "gitlab", StringComparison.OrdinalIgnoreCase) ? "merge request" : "pull request";

    private sealed record AutoReviewProject(string ConfigKey, string Source, string Repository);
}
