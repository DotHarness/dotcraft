using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.GitLab;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Realtime;
using DotCraft.Oratorio.Sources;

namespace DotCraft.Oratorio.Services;

public sealed class OratorioService(
    OratorioDbContext db,
    IClock clock,
    GitHubWriteService gitHubWriteService,
    GitLabWriteService gitLabWriteService,
    GitHubSourceService gitHubSourceService,
    GitLabSourceService gitLabSourceService,
    ImplementationDraftService implementationDraftService,
    FollowUpDraftService followUpDraftService,
    TaskBoardPlacementService taskBoardPlacement,
    IAppServerRunCoordinator appServerRunCoordinator,
    BoardEventHub boardEvents)
{
    private static readonly RunStatus[] ActiveRunStatuses = [RunStatus.Queued, RunStatus.Dispatching, RunStatus.Running];

    public async Task<ItemListResponse> ListItemsAsync(
        string? state,
        string? source,
        string? kind,
        string? repository,
        string? assignee,
        string? q,
        string? sort,
        bool? includeArchived,
        int? limit,
        string? cursor,
        CancellationToken ct)
    {
        var query = db.Items.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(state))
        {
            var states = state.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseEnum<ItemState>)
                .ToArray();
            query = query.Where(x => states.Contains(x.State));
        }
        else if (includeArchived != true)
        {
            query = query.Where(x => x.State != ItemState.Archived);
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            query = query.Where(x => x.Source == source);
        }

        if (!string.IsNullOrWhiteSpace(kind))
        {
            var parsedKind = ParseEnum<ItemKind>(kind);
            query = query.Where(x => x.Kind == parsedKind);
        }

        if (!string.IsNullOrWhiteSpace(repository))
        {
            var repositories = SourceProjectKey.RepositoryQueryCandidates(repository);
            query = repositories.Count > 0
                ? query.Where(x => x.Repository != null && repositories.Contains(x.Repository))
                : query.Where(x => x.Repository == repository);
        }

        if (!string.IsNullOrWhiteSpace(assignee))
        {
            query = query.Where(x => x.Assignee == assignee);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(x =>
                EF.Functions.Like(x.Title, pattern) ||
                EF.Functions.Like(x.ExternalId, pattern) ||
                (x.Repository != null && EF.Functions.Like(x.Repository, pattern)) ||
                (x.Assignee != null && EF.Functions.Like(x.Assignee, pattern)));
        }

        query = sort switch
        {
            "updatedAsc" => query.OrderBy(x => x.UpdatedAt).ThenBy(x => x.ItemId),
            "updatedDesc" => query.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.ItemId),
            _ => query.OrderBy(x => x.BoardSortOrder).ThenByDescending(x => x.UpdatedAt).ThenBy(x => x.ItemId)
        };

        var take = Math.Clamp(limit ?? 50, 1, 100);
        var offset = ParseOffsetCursor(cursor);
        var page = await query.Skip(offset).Take(take + 1).Select(x => x.ToSummaryDto()).ToListAsync(ct);
        var nextCursor = page.Count > take ? EncodeOffsetCursor(offset + take) : null;
        var items = page.Take(take).ToList();
        return new ItemListResponse(items, nextCursor);
    }

    public async Task<ItemDetailResponse> GetItemDetailAsync(string source, string externalId, CancellationToken ct)
    {
        var item = await GetItemAsync(source, externalId, tracking: false, ct);
        return await BuildItemDetailAsync(item, ct);
    }

    public async Task<ItemDetailResponse> GetItemDetailByIdAsync(string itemId, CancellationToken ct)
    {
        var item = await GetItemByIdAsync(itemId, tracking: false, ct);
        return await BuildItemDetailAsync(item, ct);
    }

    public async Task<ItemDetailResponse> GetTaskDetailAsync(string taskId, CancellationToken ct)
    {
        var item = await ResolveItemByTaskIdAsync(taskId, tracking: false, ct);
        return await BuildItemDetailAsync(item, ct);
    }

    public async Task<TaskReorderResponse> ReorderTasksAsync(TaskReorderRequest request, CancellationToken ct)
    {
        if (request.Updates.Count == 0)
        {
            throw OratorioApiException.Validation("updates must include at least one task.", new Dictionary<string, object?> { ["field"] = "updates" });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var affected = new List<OratorioItem>(request.Updates.Count);
        foreach (var update in request.Updates)
        {
            if (string.IsNullOrWhiteSpace(update.TaskId))
            {
                throw OratorioApiException.Validation("taskId is required.", new Dictionary<string, object?> { ["field"] = "updates.taskId" });
            }

            var item = await ResolveItemByTaskIdAsync(update.TaskId, tracking: true, ct);
            item.BoardSortOrder = update.SortOrder;
            affected.Add(item);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        foreach (var item in affected)
        {
            PublishTaskUpdated(item);
        }

        var affectedIds = affected.Select(x => x.ItemId).ToArray();
        var tasks = await db.Items.AsNoTracking()
            .Where(x => affectedIds.Contains(x.ItemId))
            .OrderBy(x => x.BoardSortOrder)
            .ThenByDescending(x => x.UpdatedAt)
            .Select(x => x.ToSummaryDto())
            .ToListAsync(ct);

        return new TaskReorderResponse(tasks);
    }

    public async Task<ItemDetailResponse> AddCommentByIdAsync(string itemId, CommentRequest request, CancellationToken ct)
    {
        var item = await GetItemByIdAsync(itemId, tracking: false, ct);
        return await AddCommentAsync(item.Source, item.ExternalId, request, ct);
    }

    public async Task<ItemDetailResponse> DispatchByIdAsync(string itemId, DispatchRequest request, CancellationToken ct)
    {
        return await DispatchByIdAsync(itemId, request, RunDispatchTrigger.Manual, ct);
    }

    public async Task<ItemDetailResponse> DispatchByIdAsync(string itemId, DispatchRequest request, RunDispatchTrigger trigger, CancellationToken ct)
    {
        var item = await GetItemByIdAsync(itemId, tracking: false, ct);
        return await DispatchAsync(item.Source, item.ExternalId, request, trigger, ct);
    }

    public async Task<string> DispatchGitHubCommentCommandAsync(
        string commandId,
        string itemId,
        CancellationToken ct)
    {
        var command = await db.GitHubCommentCommands
            .FirstOrDefaultAsync(x => x.CommandId == commandId, ct)
            ?? throw OratorioApiException.Conflict(
                "githubCommentCommandNotFound",
                "The requested GitHub comment command does not exist.",
                new Dictionary<string, object?> { ["commandId"] = commandId });
        if (command.Status == GitHubCommentCommandStatus.Dispatched && !string.IsNullOrWhiteSpace(command.RunId))
        {
            return command.RunId;
        }

        if (command.Status != GitHubCommentCommandStatus.Queued)
        {
            throw OratorioApiException.Conflict(
                "githubCommentCommandNotQueued",
                "Only queued GitHub comment commands can be dispatched.",
                new Dictionary<string, object?>
                {
                    ["commandId"] = commandId,
                    ["status"] = command.Status
                });
        }

        var item = await GetItemByIdAsync(itemId, tracking: true, ct);
        if (item.Source != "github" ||
            item.Kind != ItemKind.PullRequest ||
            !SourceProjectKey.AreEquivalent(item.Repository, command.Repository) ||
            !item.ExternalId.EndsWith($"#{command.PullRequestNumber}", StringComparison.Ordinal))
        {
            throw OratorioApiException.Conflict(
                "githubCommentCommandTargetMismatch",
                "The synchronized pull request does not match the GitHub comment command target.",
                new Dictionary<string, object?>
                {
                    ["commandId"] = commandId,
                    ["itemId"] = itemId
                });
        }

        var activeRun = await db.Runs
            .Where(x => x.ItemId == item.ItemId && ActiveRunStatuses.Contains(x.Status))
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefaultAsync(ct);
        if (activeRun is not null)
        {
            if (activeRun.RunnerKind == "appServer" &&
                activeRun.Purpose == RunPurpose.ReviewAnalysis &&
                !string.IsNullOrWhiteSpace(item.HeadSha) &&
                (string.Equals(activeRun.TargetHeadSha, item.HeadSha, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(activeRun.BaseSha, item.HeadSha, StringComparison.OrdinalIgnoreCase)))
            {
                MarkGitHubCommentCommandDispatched(command, activeRun.RunId, clock.UtcNow);
                await db.SaveChangesAsync(ct);
                return activeRun.RunId;
            }

            throw OratorioApiException.Conflict(
                "activeRunExists",
                "Cannot dispatch a GitHub mention review while another incompatible run is active.",
                new Dictionary<string, object?>
                {
                    ["itemId"] = item.ItemId,
                    ["runId"] = activeRun.RunId
                });
        }

        if (item.State is ItemState.Running or ItemState.Dispatching)
        {
            throw OratorioApiException.Conflict(
                "activeRunExists",
                "Cannot dispatch an item while another run is active.",
                new Dictionary<string, object?> { ["state"] = item.State });
        }

        if (item.State is ItemState.Rejected or ItemState.Archived)
        {
            throw InvalidTransition(item.State, "dispatch");
        }

        await EnsureSourceDetailsCurrentForDispatchAsync(item, ct);

        var now = clock.UtcNow;
        var round = await ResolveRoundForDispatchAsync(item, now, ct);
        var focus = string.IsNullOrWhiteSpace(command.Focus)
            ? null
            : command.Focus.Trim();
        var note = focus is null
            ? $"GitHub @{command.ActorLogin} requested an Oratorio review."
            : $"GitHub @{command.ActorLogin} requested an Oratorio review. Review focus: {focus}";
        var run = await QueueDispatchRunAsync(
            item,
            round,
            new DispatchRequest(
                "appServer",
                note,
                null,
                null,
                "reviewAnalysis",
                DeliveryPolicy.ManualDelivery),
            RunDispatchTrigger.GitHubMentionReview,
            now,
            ct);
        MarkGitHubCommentCommandDispatched(command, run.RunId, now);

        await db.SaveChangesAsync(ct);
        PublishTaskUpdated(item);
        return run.RunId;
    }

    public async Task<ItemDetailResponse> CancelRunByIdAsync(string itemId, CancelRunRequest request, CancellationToken ct)
    {
        var item = await GetItemByIdAsync(itemId, tracking: false, ct);
        return await CancelRunAsync(item.Source, item.ExternalId, request, ct);
    }

    public async Task<ItemDetailResponse> ReReviewPullRequestByIdAsync(string itemId, CancellationToken ct)
    {
        return await ReReviewPullRequestByIdAsync(itemId, RunDispatchTrigger.Manual, ct);
    }

    public async Task<ItemDetailResponse> ReReviewPullRequestByIdAsync(string itemId, RunDispatchTrigger trigger, CancellationToken ct)
    {
        var item = await GetItemByIdAsync(itemId, tracking: false, ct);
        return await ReReviewPullRequestAsync(item.Source, item.ExternalId, trigger, ct);
    }

    public async Task<ItemDetailResponse> ReactivateForImplementationFollowUpByIdAsync(string itemId, string note, CancellationToken ct)
    {
        var item = await GetItemByIdAsync(itemId, tracking: false, ct);
        return await ReactivateForImplementationFollowUpAsync(item.Source, item.ExternalId, note, ct);
    }

    public async Task<ItemDetailResponse> ApproveByIdAsync(string itemId, DecisionRequest request, CancellationToken ct)
    {
        var item = await GetItemByIdAsync(itemId, tracking: false, ct);
        return await ApproveAsync(item.Source, item.ExternalId, request, ct);
    }

    public async Task<ItemDetailResponse> RequestChangesByIdAsync(string itemId, DecisionRequest request, CancellationToken ct)
    {
        var item = await GetItemByIdAsync(itemId, tracking: false, ct);
        return await RequestChangesAsync(item.Source, item.ExternalId, request, ct);
    }

    public async Task<ItemDetailResponse> RejectByIdAsync(string itemId, DecisionRequest request, CancellationToken ct)
    {
        var item = await GetItemByIdAsync(itemId, tracking: false, ct);
        return await RejectAsync(item.Source, item.ExternalId, request, ct);
    }

    public async Task<ItemDetailResponse> ReopenByIdAsync(string itemId, DecisionRequest request, CancellationToken ct)
    {
        var item = await GetItemByIdAsync(itemId, tracking: false, ct);
        return await ReopenAsync(item.Source, item.ExternalId, request, ct);
    }

    public async Task<ItemDetailResponse> RetrySourceWriteAsync(string writeId, CancellationToken ct)
    {
        var write = await db.SourceWriteLogs
            .AsNoTracking()
            .Where(x => x.WriteId == writeId)
            .Select(x => new { x.Source, x.Intent })
            .FirstOrDefaultAsync(ct)
            ?? throw OratorioApiException.Conflict("sourceWriteNotFound", "The requested source write does not exist.", new Dictionary<string, object?> { ["writeId"] = writeId });
        var itemId = ImplementationDraftService.IsImplementationDeliveryIntent(write.Intent)
            ? await implementationDraftService.RetryDeliveryFromSourceWriteAsync(writeId, ct)
            : write.Source == "gitlab"
            ? await gitLabWriteService.RetryAsync(writeId, ct)
            : await gitHubWriteService.RetryAsync(writeId, ct);
        return await GetItemDetailByIdAsync(itemId, ct);
    }

    public async Task<ItemDetailResponse> SyncSourceDetailsByIdAsync(string itemId, CancellationToken ct)
    {
        var item = await GetItemByIdAsync(itemId, tracking: true, ct);
        await HydrateSourceDetailsAsync(item, ct);
        PublishTaskUpdated(item);
        return await BuildItemDetailAsync(item, ct);
    }

    public async Task<ItemDetailResponse> DeliverImplementationDraftAsync(string draftId, CancellationToken ct) =>
        await implementationDraftService.DeliverAsync(draftId, this, ct);

    public async Task<ItemDetailResponse> UpdateFollowUpDraftAsync(string draftId, FollowUpDraftUpdateRequest request, CancellationToken ct) =>
        await followUpDraftService.UpdateAsync(draftId, request, this, ct);

    public async Task<ItemDetailResponse> DiscardFollowUpDraftAsync(string draftId, CancellationToken ct) =>
        await followUpDraftService.DiscardAsync(draftId, this, ct);

    public async Task<ItemDetailResponse> CreateLocalTaskFromFollowUpDraftAsync(string draftId, CancellationToken ct) =>
        await followUpDraftService.CreateLocalTaskAsync(draftId, this, ct);

    private async Task<ItemDetailResponse> BuildItemDetailAsync(OratorioItem item, CancellationToken ct)
    {
        var rounds = await db.Rounds.AsNoTracking()
            .Where(x => x.ItemId == item.ItemId)
            .OrderBy(x => x.RoundNumber)
            .Select(x => x.ToDto())
            .ToListAsync(ct);
        var runs = await db.Runs.AsNoTracking()
            .Where(x => x.ItemId == item.ItemId)
            .OrderBy(x => x.StartedAt ?? x.CompletedAt ?? DateTimeOffset.MinValue)
            .Select(x => x.ToDto())
            .ToListAsync(ct);
        var comments = await db.Comments.AsNoTracking()
            .Where(x => x.ItemId == item.ItemId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.ToDto())
            .ToListAsync(ct);
        var decisions = await db.Decisions.AsNoTracking()
            .Where(x => x.ItemId == item.ItemId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.ToDto())
            .ToListAsync(ct);
        var timeline = await db.TimelineEvents.AsNoTracking()
            .Where(x => x.ItemId == item.ItemId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.ToDto())
            .ToListAsync(ct);
        var sourceWrites = await db.SourceWriteLogs.AsNoTracking()
            .Where(x => x.ItemId == item.ItemId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.ToDto())
            .ToListAsync(ct);
        var reviewDrafts = await db.ReviewDrafts.AsNoTracking()
            .Include(x => x.Comments)
            .Where(x => x.ItemId == item.ItemId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.ToDto())
            .ToListAsync(ct);
        var implementationDrafts = await db.ImplementationDrafts.AsNoTracking()
            .Where(x => x.ItemId == item.ItemId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.ToDto())
            .ToListAsync(ct);
        var followUpDrafts = await db.FollowUpDrafts.AsNoTracking()
            .Where(x => x.ItemId == item.ItemId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.ToDto())
            .ToListAsync(ct);
        var discussionTurns = await db.DiscussionTurns.AsNoTracking()
            .Where(x => x.ItemId == item.ItemId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.ToDto())
            .ToListAsync(ct);
        var sourceSnapshot = await db.SourceSnapshots.AsNoTracking()
            .Where(x => x.ItemId == item.ItemId)
            .OrderByDescending(x => x.SyncedAt)
            .Select(x => x.ToDto())
            .FirstOrDefaultAsync(ct);

        return new ItemDetailResponse(item.ToDto(), rounds, runs, comments, decisions, timeline, sourceWrites, reviewDrafts, implementationDrafts, followUpDrafts, discussionTurns, sourceSnapshot);
    }

    private void PublishTaskUpdated(OratorioItem item) =>
        boardEvents.PublishTaskUpdated(item, clock.UtcNow);

    public async Task<ItemDetailResponse> CreateItemAsync(CreateItemRequest request, CancellationToken ct)
    {
        ValidateRequired(request.Source, "source");
        ValidateRequired(request.ExternalId, "externalId");
        ValidateRequired(request.Title, "title");

        var exists = await db.Items.AnyAsync(x => x.Source == request.Source && x.ExternalId == request.ExternalId, ct);
        if (exists)
        {
            throw OratorioApiException.Conflict("itemAlreadyExists", "An item with this source key already exists.");
        }

        var now = clock.UtcNow;
        var item = new OratorioItem
        {
            Source = request.Source,
            ExternalId = request.ExternalId,
            Kind = request.Kind,
            Title = request.Title.Trim(),
            Description = EmptyToNull(request.Description),
            Repository = EmptyToNull(request.Repository),
            Assignee = EmptyToNull(request.Assignee),
            Branch = EmptyToNull(request.Branch),
            LabelsJson = SerializeLabels(request.Labels),
            State = ItemState.Discovered,
            CheckState = CheckState.NotConfigured,
            CreatedAt = now,
            UpdatedAt = now
        };
        await taskBoardPlacement.AssignNewItemProjectionAsync(item, TaskStatusMapping.Project(item.State), ct);
        db.Items.Add(item);
        AddTimeline(item, null, null, TimelineEventKind.SourceSynced, ActorKind.Source, request.Source, "Item created", "Local validation item created.", now);
        await db.SaveChangesAsync(ct);
        PublishTaskUpdated(item);
        return await GetItemDetailAsync(item.Source, item.ExternalId, ct);
    }

    public async Task<ItemDetailResponse> CreateLocalTaskAsync(CreateLocalTaskRequest request, CancellationToken ct)
    {
        ValidateRequired(request.Title, "title");
        var now = clock.UtcNow;
        var item = new OratorioItem
        {
            Source = "local",
            ExternalId = await GenerateLocalTaskExternalIdAsync(ct),
            Kind = ItemKind.LocalTask,
            Title = request.Title.Trim(),
            Description = EmptyToNull(request.Description),
            Repository = EmptyToNull(request.Repository),
            Assignee = EmptyToNull(request.Assignee),
            Branch = EmptyToNull(request.Branch),
            LabelsJson = SerializeLabels(request.Labels),
            State = ItemState.Discovered,
            CheckState = CheckState.NotConfigured,
            CreatedAt = now,
            UpdatedAt = now
        };
        await taskBoardPlacement.AssignNewItemProjectionAsync(item, TaskStatusMapping.Project(item.State), ct);
        db.Items.Add(item);
        AddTimeline(item, null, null, TimelineEventKind.SourceSynced, ActorKind.Operator, "operator", "Local task created", item.Description, now);
        await db.SaveChangesAsync(ct);
        PublishTaskUpdated(item);
        return await GetItemDetailAsync(item.Source, item.ExternalId, ct);
    }

    public async Task<ItemDetailResponse> UpdateItemByIdAsync(string itemId, UpdateItemRequest request, CancellationToken ct)
    {
        ValidateRequired(request.Title, "title");
        var item = await GetItemByIdAsync(itemId, tracking: true, ct);
        EnsureLocalTask(item, "edit");
        EnsureNotActive(item, "edit");

        var now = clock.UtcNow;
        item.Title = request.Title.Trim();
        item.Description = EmptyToNull(request.Description);
        item.Repository = EmptyToNull(request.Repository);
        item.Assignee = EmptyToNull(request.Assignee);
        item.Branch = EmptyToNull(request.Branch);
        item.LabelsJson = SerializeLabels(request.Labels);
        item.UpdatedAt = now;
        AddTimeline(item, await FindCurrentRoundAsync(item, ct), null, TimelineEventKind.ItemUpdated, ActorKind.Operator, "operator", "Local task updated", item.Title, now);
        await db.SaveChangesAsync(ct);
        PublishTaskUpdated(item);
        return await GetItemDetailByIdAsync(itemId, ct);
    }

    public async Task<ItemDetailResponse> ArchiveByIdAsync(string itemId, CancellationToken ct)
    {
        var item = await GetItemByIdAsync(itemId, tracking: true, ct);
        EnsureNotActive(item, "archive");

        var now = clock.UtcNow;
        item.State = ItemState.Archived;
        item.ArchiveReason = ArchiveReason.Manual;
        item.UpdatedAt = now;
        AddTimeline(item, await FindCurrentRoundAsync(item, ct), null, TimelineEventKind.ItemArchived, ActorKind.Operator, "operator", "Item archived", null, now);
        await db.SaveChangesAsync(ct);
        PublishTaskUpdated(item);
        return await GetItemDetailByIdAsync(itemId, ct);
    }

    public async Task<ItemDetailResponse> AddCommentAsync(string source, string externalId, CommentRequest request, CancellationToken ct)
    {
        ValidateRequired(request.Body, "body");
        var item = await GetItemAsync(source, externalId, tracking: true, ct);
        var now = clock.UtcNow;
        var round = request.RoundNumber is not null
            ? await FindRoundByNumberAsync(item.ItemId, request.RoundNumber.Value, ct)
            : await FindCurrentRoundAsync(item, ct);

        var comment = new OratorioComment
        {
            ItemId = item.ItemId,
            RoundId = round?.RoundId,
            Body = request.Body.Trim(),
            Purpose = CommentPurpose.Feedback,
            CreatedAt = now
        };
        db.Comments.Add(comment);
        item.UpdatedAt = now;
        AddTimeline(item, round, null, TimelineEventKind.CommentAdded, ActorKind.Operator, "operator", "Feedback added", comment.Body, now);
        await db.SaveChangesAsync(ct);
        PublishTaskUpdated(item);
        return await GetItemDetailAsync(source, externalId, ct);
    }

    public async Task<ItemDetailResponse> DispatchAsync(string source, string externalId, DispatchRequest request, CancellationToken ct)
    {
        return await DispatchAsync(source, externalId, request, RunDispatchTrigger.Manual, ct);
    }

    public async Task<ItemDetailResponse> DispatchAsync(string source, string externalId, DispatchRequest request, RunDispatchTrigger trigger, CancellationToken ct)
    {
        var item = await GetItemAsync(source, externalId, tracking: true, ct);
        if (item.State is ItemState.Running or ItemState.Dispatching)
        {
            throw OratorioApiException.Conflict(
                "activeRunExists",
                "Cannot dispatch an item while another run is active.",
                new Dictionary<string, object?> { ["state"] = item.State });
        }

        if (item.State is ItemState.Rejected or ItemState.Archived)
        {
            throw InvalidTransition(item.State, "dispatch");
        }

        await EnsureSourceDetailsCurrentForDispatchAsync(item, ct);

        var now = clock.UtcNow;
        var round = await ResolveRoundForDispatchAsync(item, now, ct);
        await QueueDispatchRunAsync(item, round, request, trigger, now, ct);

        await db.SaveChangesAsync(ct);
        PublishTaskUpdated(item);
        return await GetItemDetailAsync(source, externalId, ct);
    }

    public async Task<ItemDetailResponse> CancelRunAsync(string source, string externalId, CancelRunRequest request, CancellationToken ct)
    {
        var item = await GetItemAsync(source, externalId, tracking: true, ct);
        if (item.State is not (ItemState.Dispatching or ItemState.Running))
        {
            throw RunNotCancellable(item, null);
        }

        if (string.IsNullOrWhiteSpace(item.CurrentRunId))
        {
            throw RunNotCancellable(item, null);
        }

        var run = await db.Runs
            .Include(x => x.Round)
            .Include(x => x.Item)
            .FirstOrDefaultAsync(x => x.RunId == item.CurrentRunId && x.ItemId == item.ItemId, ct)
            ?? throw RunNotCancellable(item, null);
        if (!ActiveRunStatuses.Contains(run.Status))
        {
            throw RunNotCancellable(item, run);
        }

        var now = clock.UtcNow;
        var body = EmptyToNull(request.Body);
        var message = body ?? "Operator cancelled the active run.";

        run.Status = RunStatus.Cancelled;
        run.CompletedAt = now;
        run.Summary = message;
        run.ErrorCode = "operatorCancelled";
        run.ErrorMessage = message;
        run.ProgressPercent = 100;
        run.StatusMessage = message;
        run.LastHeartbeatAt = now;
        run.LeaseOwner = null;
        run.LeaseAcquiredAt = null;
        if (run.WorktreeStatus is WorktreeStatus.Ready or WorktreeStatus.Preparing)
        {
            run.WorktreeStatus = run.WorktreePath is null ? WorktreeStatus.Failed : WorktreeStatus.CleanupPending;
            run.WorktreeCleanupAfterAt = now;
        }

        if (run.Round is not null)
        {
            run.Round.Status = RoundStatus.Cancelled;
            run.Round.Summary = message;
            run.Round.CompletedAt = now;
        }

        item.State = ItemState.Discovered;
        item.CurrentRunId = null;
        item.CheckState = item.Kind == ItemKind.PullRequest ? CheckState.Attention : CheckState.NotConfigured;
        item.UpdatedAt = now;
        AddTimeline(item, run.Round, run, TimelineEventKind.RunCancelled, ActorKind.Operator, "operator", "Run cancelled", body, now);
        AddTimeline(item, run.Round, run, TimelineEventKind.CheckUpdated, ActorKind.System, "oratorio/review", "Check cancelled", "The active Oratorio run was cancelled by the operator.", now);

        await gitHubWriteService.RecordReviewGateRunCancelledAsync(run, ct);
        await db.SaveChangesAsync(ct);
        PublishTaskUpdated(item);
        if (run.RunnerKind == "appServer")
        {
            await appServerRunCoordinator.TryInterruptTurnAsync(run.RunId, ct);
        }

        return await GetItemDetailAsync(source, externalId, ct);
    }

    public async Task<ItemDetailResponse> ReReviewPullRequestAsync(string source, string externalId, CancellationToken ct)
    {
        return await ReReviewPullRequestAsync(source, externalId, RunDispatchTrigger.Manual, ct);
    }

    private async Task<ItemDetailResponse> ReReviewPullRequestAsync(string source, string externalId, RunDispatchTrigger trigger, CancellationToken ct)
    {
        var item = await GetItemAsync(source, externalId, tracking: true, ct);
        if (item.Source is not ("github" or "gitlab") || item.Kind != ItemKind.PullRequest)
        {
            throw OratorioApiException.Conflict(
                "invalidReReviewTarget",
                "Only GitHub pull requests and GitLab merge requests can be re-reviewed.",
                new Dictionary<string, object?> { ["source"] = item.Source, ["kind"] = item.Kind });
        }

        if (item.State is ItemState.Running or ItemState.Dispatching)
        {
            throw OratorioApiException.Conflict(
                "activeRunExists",
                "Cannot re-review an item while another run is active.",
                new Dictionary<string, object?> { ["state"] = item.State });
        }

        if (item.State is ItemState.Rejected or ItemState.Archived)
        {
            throw InvalidTransition(item.State, "reReview");
        }

        await EnsureSourceDetailsCurrentForDispatchAsync(item, ct);
        var currentHeadSha = item.HeadSha;
        if (string.IsNullOrWhiteSpace(currentHeadSha))
        {
            throw OratorioApiException.Conflict(
                item.Source == "gitlab" ? "gitlabHeadShaRequired" : "githubHeadShaRequired",
                "Re-review requires the current review target head SHA.",
                new Dictionary<string, object?> { ["itemId"] = item.ItemId });
        }

        var latestReviewRun = await FindLatestSuccessfulReviewRunAsync(item.ItemId, ct)
            ?? throw OratorioApiException.Conflict(
                "reviewRunRequired",
                "Re-review requires a completed AppServer review run.",
                new Dictionary<string, object?> { ["itemId"] = item.ItemId });
        var previousHeadSha = ExtractAnalyzedHeadSha(latestReviewRun);
        if (string.IsNullOrWhiteSpace(previousHeadSha))
        {
            throw OratorioApiException.Conflict(
                "reviewHeadShaRequired",
                "Re-review could not determine the head SHA analyzed by the previous review run.",
                new Dictionary<string, object?> { ["runId"] = latestReviewRun.RunId });
        }

        if (string.Equals(previousHeadSha, currentHeadSha, StringComparison.OrdinalIgnoreCase))
        {
            throw OratorioApiException.Conflict(
                "noNewPullRequestHead",
                "The review target head has not changed since the latest successful Oratorio review.",
                new Dictionary<string, object?> { ["headSha"] = currentHeadSha });
        }

        var now = clock.UtcNow;
        var note = BuildReReviewDispatchNote(previousHeadSha, currentHeadSha);
        var actorKind = trigger == RunDispatchTrigger.AutoReview ? ActorKind.System : ActorKind.Operator;
        var actorName = trigger == RunDispatchTrigger.AutoReview ? "oratorio/auto-review" : "operator";
        var currentRound = await FindCurrentRoundAsync(item, ct);
        if (currentRound is not null)
        {
            currentRound.Status = RoundStatus.Superseded;
            currentRound.CompletedAt = now;
            db.Decisions.Add(new OratorioDecision
            {
                ItemId = item.ItemId,
                RoundId = currentRound.RoundId,
                Decision = DecisionType.ReReview,
                AuthorName = actorName,
                Body = note,
                CreatedAt = now
            });
            AddTimeline(item, currentRound, null, TimelineEventKind.DecisionRecorded, actorKind, actorName, trigger == RunDispatchTrigger.AutoReview ? "Auto re-review requested" : "Re-review requested", note, now);
        }

        var nextRound = CreateNextRound(item, now);
        await QueueDispatchRunAsync(
            item,
            nextRound,
            new DispatchRequest("appServer", note, null, null, "reviewAnalysis", DeliveryPolicy.ManualDelivery),
            trigger,
            now,
            ct);

        await db.SaveChangesAsync(ct);
        PublishTaskUpdated(item);
        return await GetItemDetailAsync(source, externalId, ct);
    }

    private async Task<ItemDetailResponse> ReactivateForImplementationFollowUpAsync(string source, string externalId, string note, CancellationToken ct)
    {
        var item = await GetItemAsync(source, externalId, tracking: true, ct);
        if (item.Kind is not (ItemKind.Issue or ItemKind.LocalTask))
        {
            throw OratorioApiException.Conflict(
                "implementationUnsupportedItem",
                "Implementation follow-up is only supported for issue and local task items.",
                new Dictionary<string, object?> { ["kind"] = item.Kind });
        }

        if (item.State is ItemState.Running or ItemState.Dispatching)
        {
            throw OratorioApiException.Conflict(
                "activeRunExists",
                "Cannot start an implementation follow-up while another run is active.",
                new Dictionary<string, object?> { ["state"] = item.State });
        }

        if (item.State != ItemState.AwaitingReview)
        {
            throw InvalidTransition(item.State, "implementationFollowUp");
        }

        await EnsureSourceDetailsCurrentForDispatchAsync(item, ct);

        var now = clock.UtcNow;
        var dispatchNote = string.IsNullOrWhiteSpace(note)
            ? "Oratorio queued an implementation follow-up for new review feedback on the generated pull request."
            : note.Trim();
        var currentRound = await FindCurrentRoundAsync(item, ct);
        if (currentRound is not null && currentRound.Status == RoundStatus.AwaitingReview)
        {
            currentRound.Status = RoundStatus.Superseded;
            currentRound.CompletedAt = now;
        }

        var nextRound = CreateNextRound(item, now);
        await QueueDispatchRunAsync(
            item,
            nextRound,
            new DispatchRequest("appServer", dispatchNote, null, null, "implementation", DeliveryPolicy.AutoPr),
            RunDispatchTrigger.AutoFollowUp,
            now,
            ct);

        await db.SaveChangesAsync(ct);
        PublishTaskUpdated(item);
        return await GetItemDetailAsync(source, externalId, ct);
    }

    public Task<ItemDetailResponse> ApproveAsync(string source, string externalId, DecisionRequest request, CancellationToken ct) =>
        RecordDecisionAsync(source, externalId, DecisionType.Approve, request.Body, ct);

    public Task<ItemDetailResponse> RequestChangesAsync(string source, string externalId, DecisionRequest request, CancellationToken ct)
    {
        ValidateRequired(request.Body, "body");
        return RecordDecisionAsync(source, externalId, DecisionType.RequestChanges, request.Body, ct);
    }

    public Task<ItemDetailResponse> RejectAsync(string source, string externalId, DecisionRequest request, CancellationToken ct) =>
        RecordDecisionAsync(source, externalId, DecisionType.Reject, request.Body, ct);

    public async Task<ItemDetailResponse> ReopenAsync(string source, string externalId, DecisionRequest request, CancellationToken ct)
    {
        var item = await GetItemAsync(source, externalId, tracking: true, ct);
        if (item.State is not (ItemState.Approved or ItemState.Rejected or ItemState.Failed or ItemState.Archived))
        {
            throw InvalidTransition(item.State, "reopen");
        }

        var now = clock.UtcNow;
        var round = await FindCurrentRoundAsync(item, ct);
        if (round is not null)
        {
            db.Decisions.Add(new OratorioDecision
            {
                ItemId = item.ItemId,
                RoundId = round.RoundId,
                Decision = DecisionType.Reopen,
                Body = EmptyToNull(request.Body),
                CreatedAt = now
            });
        }

        item.State = ItemState.Discovered;
        item.ArchiveReason = null;
        item.CheckState = item.Kind == ItemKind.PullRequest ? CheckState.Attention : CheckState.NotConfigured;
        item.UpdatedAt = now;
        AddTimeline(item, round, null, TimelineEventKind.ItemReopened, ActorKind.Operator, "operator", "Item reopened", request.Body, now);
        await db.SaveChangesAsync(ct);
        PublishTaskUpdated(item);
        return await GetItemDetailAsync(source, externalId, ct);
    }

    public async Task<RunDto> GetRunAsync(string runId, CancellationToken ct)
    {
        var run = await db.Runs.AsNoTracking().FirstOrDefaultAsync(x => x.RunId == runId, ct)
            ?? throw OratorioApiException.RunNotFound(runId);
        return run.ToDto();
    }

    private async Task<ItemDetailResponse> RecordDecisionAsync(string source, string externalId, DecisionType decision, string? body, CancellationToken ct)
    {
        var item = await GetItemAsync(source, externalId, tracking: true, ct);
        if ((decision is DecisionType.Approve or DecisionType.RequestChanges) && item.State != ItemState.AwaitingReview)
        {
            throw InvalidTransition(item.State, decision.ToString());
        }

        if (decision == DecisionType.Reject && item.State is not (ItemState.AwaitingReview or ItemState.Failed))
        {
            throw InvalidTransition(item.State, "reject");
        }

        if (decision == DecisionType.Approve && await HasUndeliveredImplementationDraftAsync(item.ItemId, ct))
        {
            throw OratorioApiException.Conflict(
                "implementationDraftUndelivered",
                "Cannot approve an implementation handoff until the implementation draft is delivered to a generated pull request.",
                new Dictionary<string, object?> { ["itemId"] = item.ItemId });
        }

        var round = await FindCurrentRoundAsync(item, ct)
            ?? throw OratorioApiException.Conflict("invalidTransition", "The item does not have a current round.");
        var now = clock.UtcNow;
        OratorioComment? linkedComment = null;
        if (decision == DecisionType.RequestChanges)
        {
            linkedComment = new OratorioComment
            {
                ItemId = item.ItemId,
                RoundId = round.RoundId,
                Body = body!.Trim(),
                Purpose = CommentPurpose.Feedback,
                CreatedAt = now
            };
            db.Comments.Add(linkedComment);
            AddTimeline(item, round, null, TimelineEventKind.CommentAdded, ActorKind.Operator, "operator", "Feedback added", linkedComment.Body, now);
        }

        var recordedDecision = new OratorioDecision
        {
            ItemId = item.ItemId,
            RoundId = round.RoundId,
            Decision = decision,
            Comment = linkedComment,
            CommentId = linkedComment?.CommentId,
            Body = EmptyToNull(body),
            CreatedAt = now
        };
        db.Decisions.Add(recordedDecision);

        switch (decision)
        {
            case DecisionType.Approve:
                item.State = ItemState.Approved;
                item.CheckState = CheckState.Passing;
                round.Status = RoundStatus.Approved;
                round.CompletedAt = now;
                AddTimeline(item, round, null, TimelineEventKind.DecisionRecorded, ActorKind.Operator, "operator", "Approved", body, now);
                AddTimeline(item, round, null, TimelineEventKind.CheckUpdated, ActorKind.System, "oratorio/review", "Check passed", "Required review gate is satisfied.", now);
                break;
            case DecisionType.RequestChanges:
                item.State = ItemState.Discovered;
                item.CheckState = CheckState.Attention;
                round.Status = RoundStatus.ChangesRequested;
                round.CompletedAt = now;
                AddTimeline(item, round, null, TimelineEventKind.DecisionRecorded, ActorKind.Operator, "operator", "Changes requested", body, now);
                break;
            case DecisionType.Reject:
                item.State = ItemState.Rejected;
                item.CheckState = CheckState.Failing;
                round.Status = RoundStatus.Rejected;
                round.CompletedAt = now;
                AddTimeline(item, round, null, TimelineEventKind.DecisionRecorded, ActorKind.Operator, "operator", "Rejected", body, now);
                AddTimeline(item, round, null, TimelineEventKind.CheckUpdated, ActorKind.System, "oratorio/review", "Check failed", "The item was rejected.", now);
                break;
        }

        item.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        PublishTaskUpdated(item);
        await gitHubWriteService.RecordDecisionWritesAsync(recordedDecision.DecisionId, ct);
        await gitLabWriteService.RecordDecisionWritesAsync(recordedDecision.DecisionId, ct);
        return await GetItemDetailAsync(source, externalId, ct);
    }

    private async Task<OratorioRound> ResolveRoundForDispatchAsync(OratorioItem item, DateTimeOffset now, CancellationToken ct)
    {
        if (item.CurrentRound == 0 || (item.State == ItemState.Discovered && await CurrentRoundIsClosedAsync(item, ct)))
        {
            return CreateNextRound(item, now);
        }

        var existing = await FindCurrentRoundAsync(item, ct);
        if (existing is not null && existing.Status == RoundStatus.ChangesRequested)
        {
            return CreateNextRound(item, now);
        }

        return existing ?? throw OratorioApiException.Conflict("invalidTransition", "Unable to resolve a dispatch round.");
    }

    private async Task<OratorioRun> QueueDispatchRunAsync(OratorioItem item, OratorioRound round, DispatchRequest request, RunDispatchTrigger trigger, DateTimeOffset now, CancellationToken ct)
    {
        var attempt = await db.Runs.CountAsync(x => x.RoundId == round.RoundId, ct) + 1;
        var runnerKind = NormalizeRunnerKind(request.Mode);
        if (runnerKind is not ("mock" or "appServer"))
        {
            throw OratorioApiException.Validation(
                "Dispatch mode must be either mock or appServer.",
                new Dictionary<string, object?> { ["mode"] = runnerKind });
        }

        var run = new OratorioRun
        {
            ItemId = item.ItemId,
            RoundId = round.RoundId,
            Attempt = attempt,
            Status = RunStatus.Queued,
            RunnerKind = runnerKind,
            StartedAt = now,
            ProgressPercent = 0,
            StatusMessage = runnerKind == "mock" ? "Queued for mock runner." : "Queued for DotCraft AppServer.",
            LastHeartbeatAt = now,
            MockOutcome = runnerKind == "mock" ? request.MockOutcome ?? MockOutcome.Success : MockOutcome.Success,
            MockDurationSeconds = runnerKind == "mock" ? Math.Clamp(request.MockDurationSeconds ?? 8, 1, 120) : 8,
            Purpose = NormalizeRunPurpose(request.WorkMode),
            DispatchTrigger = trigger,
            TargetHeadSha = item.Kind == ItemKind.PullRequest ? item.HeadSha : null,
            DeliveryPolicy = request.DeliveryPolicy ?? DeliveryPolicy.ManualDelivery
        };
        EnsureSupportedRunPurpose(item, run.Purpose);
        if (run.Purpose == RunPurpose.Implementation && runnerKind != "appServer")
        {
            throw OratorioApiException.Validation(
                "Implementation dispatch requires appServer mode.",
                new Dictionary<string, object?> { ["mode"] = runnerKind });
        }

        db.Runs.Add(run);
        item.State = ItemState.Dispatching;
        item.CheckState = CheckState.Pending;
        item.CurrentRunId = run.RunId;
        item.UpdatedAt = now;
        round.Status = RoundStatus.Running;
        AddTimeline(item, round, run, TimelineEventKind.RunQueued, ActorKind.System, "Dispatcher", runnerKind == "mock" ? "Run queued" : "DotCraft run queued", request.Note, now);
        AddTimeline(item, round, run, TimelineEventKind.CheckUpdated, ActorKind.System, "oratorio/review", "Check pending", "A review round is running.", now);
        await gitHubWriteService.RecordReviewGateStartedAsync(item, round, run, ct);
        return run;
    }

    private static void MarkGitHubCommentCommandDispatched(
        OratorioGitHubCommentCommand command,
        string runId,
        DateTimeOffset now)
    {
        command.Status = GitHubCommentCommandStatus.Dispatched;
        command.RunId = runId;
        command.NextAttemptAt = null;
        command.ErrorCode = null;
        command.ErrorMessage = null;
        command.UpdatedAt = now;
        command.CompletedAt = now;
    }

    private OratorioRound CreateNextRound(OratorioItem item, DateTimeOffset now)
    {
        item.CurrentRound += 1;
        var round = new OratorioRound
        {
            ItemId = item.ItemId,
            RoundNumber = item.CurrentRound,
            Status = RoundStatus.Open,
            CreatedAt = now
        };
        db.Rounds.Add(round);
        AddTimeline(item, round, null, TimelineEventKind.RoundCreated, ActorKind.System, "Oratorio", $"Round {round.RoundNumber} created", null, now);
        return round;
    }

    private async Task EnsureSourceDetailsCurrentForDispatchAsync(OratorioItem item, CancellationToken ct)
    {
        if (item.Source is not ("github" or "gitlab") || item.SourceDetailsStatus == SourceDetailsStatus.Current)
        {
            return;
        }

        await HydrateSourceDetailsAsync(item, ct);
        if (item.SourceDetailsStatus != SourceDetailsStatus.Current)
        {
            throw OratorioApiException.Conflict(
                "sourceDetailsRequired",
                "Source details must sync successfully before dispatch.",
                new Dictionary<string, object?> { ["itemId"] = item.ItemId, ["source"] = item.Source, ["sourceDetailsStatus"] = item.SourceDetailsStatus });
        }
    }

    private async Task HydrateSourceDetailsAsync(OratorioItem item, CancellationToken ct)
    {
        if (item.Source == "github")
        {
            await gitHubSourceService.HydrateItemDetailsAsync(item, ct);
            return;
        }

        if (item.Source == "gitlab")
        {
            await gitLabSourceService.HydrateItemDetailsAsync(item, ct);
            return;
        }

        throw OratorioApiException.Conflict(
            "unsupportedSource",
            "Source detail sync is not supported for this source.",
            new Dictionary<string, object?> { ["source"] = item.Source });
    }

    private async Task<OratorioRun?> FindLatestSuccessfulReviewRunAsync(string itemId, CancellationToken ct) =>
        await db.Runs.AsNoTracking()
            .Where(x =>
                x.ItemId == itemId &&
                x.RunnerKind == "appServer" &&
                x.Purpose == RunPurpose.ReviewAnalysis &&
                x.Status == RunStatus.Succeeded)
            .OrderByDescending(x => x.CompletedAt ?? x.StartedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefaultAsync(ct);

    private static string? ExtractAnalyzedHeadSha(OratorioRun run)
    {
        if (!string.IsNullOrWhiteSpace(run.TargetHeadSha))
        {
            return run.TargetHeadSha;
        }

        if (!string.IsNullOrWhiteSpace(run.BaseSha))
        {
            return run.BaseSha;
        }

        if (string.IsNullOrWhiteSpace(run.PromptContextJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(run.PromptContextJson);
            var root = document.RootElement;
            return ExtractNestedString(root, "item", "headSha")
                ?? ExtractNestedString(root, "Item", "HeadSha")
                ?? ExtractNestedString(root, "workspace", "headSha")
                ?? ExtractNestedString(root, "Workspace", "HeadSha");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractNestedString(JsonElement root, string objectName, string propertyName)
    {
        if (root.TryGetProperty(objectName, out var obj) &&
            obj.ValueKind == JsonValueKind.Object &&
            obj.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        return null;
    }

    private static string BuildReReviewDispatchNote(string previousHeadSha, string currentHeadSha) =>
        $"Operator requested a fresh review because the review target head changed from {previousHeadSha} to {currentHeadSha} after the previous Oratorio review. Re-review the latest head; focus on new changes since the previous review when useful, while still inspecting enough context to submit high-confidence review suggestions.";

    private async Task<bool> CurrentRoundIsClosedAsync(OratorioItem item, CancellationToken ct)
    {
        var current = await FindCurrentRoundAsync(item, ct);
        return current is null || current.Status is RoundStatus.Approved or RoundStatus.ChangesRequested or RoundStatus.Superseded or RoundStatus.Rejected or RoundStatus.Cancelled or RoundStatus.Failed;
    }

    private async Task<OratorioItem> GetItemAsync(string source, string externalId, bool tracking, CancellationToken ct)
    {
        IQueryable<OratorioItem> query = db.Items;
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(x => x.Source == source && x.ExternalId == externalId, ct)
            ?? throw OratorioApiException.ItemNotFound(source, externalId);
    }

    private async Task<OratorioItem> GetItemByIdAsync(string itemId, bool tracking, CancellationToken ct)
    {
        IQueryable<OratorioItem> query = db.Items;
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(x => x.ItemId == itemId, ct)
            ?? throw OratorioApiException.Conflict("itemNotFound", "The requested item does not exist.", new Dictionary<string, object?> { ["itemId"] = itemId });
    }

    private async Task<OratorioItem> ResolveItemByTaskIdAsync(string taskId, bool tracking, CancellationToken ct)
    {
        var normalized = taskId.Trim();
        IQueryable<OratorioItem> query = db.Items;
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        if (LooksLikeShortId(normalized))
        {
            var shortId = normalized.ToUpperInvariant();
            var matches = await query.Where(x => x.ShortId == shortId).Take(2).ToListAsync(ct);
            if (matches.Count == 1)
            {
                return matches[0];
            }

            if (matches.Count > 1)
            {
                throw OratorioApiException.Conflict("ambiguousTaskId", "The requested task short id matches multiple workspaces.", new Dictionary<string, object?> { ["taskId"] = taskId });
            }
        }

        return await query.FirstOrDefaultAsync(x => x.ItemId == normalized, ct)
            ?? throw TaskNotFound(taskId);
    }

    private async Task<OratorioRound?> FindCurrentRoundAsync(OratorioItem item, CancellationToken ct)
    {
        if (item.CurrentRound == 0)
        {
            return null;
        }

        return await db.Rounds.FirstOrDefaultAsync(x => x.ItemId == item.ItemId && x.RoundNumber == item.CurrentRound, ct);
    }

    private async Task<bool> HasUndeliveredImplementationDraftAsync(string itemId, CancellationToken ct) =>
        await db.ImplementationDrafts.AnyAsync(x =>
            x.ItemId == itemId &&
            x.DeliveryPolicy == DeliveryPolicy.ManualDelivery &&
            x.Status == ImplementationDraftStatus.Draft,
            ct);

    private async Task<OratorioRound> FindRoundByNumberAsync(string itemId, int roundNumber, CancellationToken ct) =>
        await db.Rounds.FirstOrDefaultAsync(x => x.ItemId == itemId && x.RoundNumber == roundNumber, ct)
        ?? throw OratorioApiException.Validation("The requested round does not exist.", new Dictionary<string, object?> { ["roundNumber"] = roundNumber });

    private static TEnum ParseEnum<TEnum>(string value) where TEnum : struct =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw OratorioApiException.Validation($"Invalid {typeof(TEnum).Name} value.", new Dictionary<string, object?> { ["value"] = value });

    private static void ValidateRequired(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw OratorioApiException.Validation($"{field} is required.", new Dictionary<string, object?> { ["field"] = field });
        }
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool LooksLikeShortId(string value)
    {
        var separator = value.LastIndexOf('-');
        return separator > 0 &&
            separator < value.Length - 1 &&
            value[..separator].All(char.IsAsciiLetterOrDigit) &&
            value[(separator + 1)..].All(char.IsAsciiDigit);
    }

    private static int ParseOffsetCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return 0;
        }

        if (int.TryParse(cursor, out var offset) && offset >= 0)
        {
            return offset;
        }

        throw OratorioApiException.Validation("Invalid cursor value.", new Dictionary<string, object?> { ["cursor"] = cursor });
    }

    private static string EncodeOffsetCursor(int offset) => offset.ToString(CultureInfo.InvariantCulture);

    private static OratorioApiException TaskNotFound(string taskId) =>
        new(
            StatusCodes.Status404NotFound,
            "taskNotFound",
            "The requested task does not exist.",
            new Dictionary<string, object?> { ["taskId"] = taskId });

    private async Task<string> GenerateLocalTaskExternalIdAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = $"task:{Guid.NewGuid():n}"[..17];
            if (!await db.Items.AnyAsync(x => x.Source == "local" && x.ExternalId == candidate, ct))
            {
                return candidate;
            }
        }

        return $"task:{Guid.NewGuid():n}";
    }

    private static string? SerializeLabels(IReadOnlyList<string>? labels)
    {
        var normalized = (labels ?? [])
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return normalized.Length == 0 ? null : JsonSerializer.Serialize(normalized);
    }

    private static void EnsureLocalTask(OratorioItem item, string action)
    {
        if (item.Source != "local" || item.Kind != ItemKind.LocalTask)
        {
            throw OratorioApiException.Conflict(
                "invalidTransition",
                $"Cannot {action} an item that is not a local task.",
                new Dictionary<string, object?> { ["source"] = item.Source, ["kind"] = item.Kind });
        }
    }

    private static void EnsureNotActive(OratorioItem item, string action)
    {
        if (item.State is ItemState.Dispatching or ItemState.Running)
        {
            throw OratorioApiException.Conflict(
                "invalidTransition",
                $"Cannot {action} an item while a run is active.",
                new Dictionary<string, object?> { ["state"] = item.State });
        }
    }

    private static string NormalizeRunnerKind(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return "mock";
        }

        return mode.Trim().Equals("appServer", StringComparison.OrdinalIgnoreCase)
            ? "appServer"
            : mode.Trim().Equals("mock", StringComparison.OrdinalIgnoreCase)
                ? "mock"
                : mode.Trim();
    }

    private static RunPurpose NormalizeRunPurpose(string? workMode)
    {
        if (string.IsNullOrWhiteSpace(workMode))
        {
            return RunPurpose.ReviewAnalysis;
        }

        return workMode.Trim().Equals("implementation", StringComparison.OrdinalIgnoreCase)
            ? RunPurpose.Implementation
            : workMode.Trim().Equals("review", StringComparison.OrdinalIgnoreCase) ||
                workMode.Trim().Equals("reviewAnalysis", StringComparison.OrdinalIgnoreCase)
                    ? RunPurpose.ReviewAnalysis
                    : throw OratorioApiException.Validation("Dispatch workMode must be reviewAnalysis or implementation.", new Dictionary<string, object?> { ["workMode"] = workMode });
    }

    private static void EnsureSupportedRunPurpose(OratorioItem item, RunPurpose purpose)
    {
        if (purpose != RunPurpose.Implementation)
        {
            return;
        }

        if (item.Kind is not (ItemKind.Issue or ItemKind.LocalTask))
        {
            throw OratorioApiException.Conflict(
                "implementationUnsupportedItem",
                "Implementation runs are only supported for GitHub/GitLab issues and Oratorio local tasks.",
                new Dictionary<string, object?> { ["source"] = item.Source, ["kind"] = item.Kind });
        }

        if (item.Kind == ItemKind.Issue && item.Source is not ("github" or "gitlab"))
        {
            throw OratorioApiException.Conflict(
                "implementationUnsupportedItem",
                "Implementation issue runs require a GitHub or GitLab issue source item.",
                new Dictionary<string, object?> { ["source"] = item.Source, ["kind"] = item.Kind });
        }
    }

    private static OratorioApiException InvalidTransition(ItemState state, string action) =>
        OratorioApiException.Conflict(
            "invalidTransition",
            $"Cannot {action} an item from its current state.",
            new Dictionary<string, object?> { ["state"] = state });

    private static OratorioApiException RunNotCancellable(OratorioItem item, OratorioRun? run) =>
        OratorioApiException.Conflict(
            "runNotCancellable",
            "Only dispatching or running items with an active run can be cancelled.",
            new Dictionary<string, object?>
            {
                ["state"] = item.State,
                ["runStatus"] = run?.Status,
                ["currentRunId"] = item.CurrentRunId
            });

    private void AddTimeline(
        OratorioItem item,
        OratorioRound? round,
        OratorioRun? run,
        TimelineEventKind kind,
        ActorKind actorKind,
        string actorName,
        string title,
        string? body,
        DateTimeOffset createdAt)
    {
        db.TimelineEvents.Add(new OratorioTimelineEvent
        {
            ItemId = item.ItemId,
            RoundId = round?.RoundId,
            RunId = run?.RunId,
            Kind = kind,
            ActorKind = actorKind,
            ActorName = actorName,
            Title = title,
            Body = body,
            CreatedAt = createdAt
        });
    }
}
