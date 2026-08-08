using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;

namespace Oratorio.Server.Services;

public sealed class FollowUpDraftService(OratorioDbContext db, IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SubmitFollowUpDraftResponse> SubmitForRunAsync(
        string runId,
        SubmitFollowUpDraftRequest request,
        CancellationToken ct)
    {
        var run = await db.Runs.Include(x => x.Item).Include(x => x.Round).FirstOrDefaultAsync(x => x.RunId == runId, ct)
            ?? throw OratorioApiException.RunNotFound(runId);
        EnsureEligibleRun(run);
        var proposals = ValidateProposals(request);

        var now = clock.UtcNow;
        var draftIds = new List<string>(proposals.Count);
        foreach (var proposal in proposals)
        {
            var draft = new OratorioFollowUpDraft
            {
                ItemId = run.ItemId,
                RoundId = run.RoundId,
                RunId = run.RunId,
                Status = FollowUpDraftStatus.Draft,
                Title = proposal.Title!.Trim(),
                Body = proposal.Body!.Trim(),
                Rationale = EmptyToNull(proposal.Rationale),
                Repository = EmptyToNull(proposal.Repository),
                Assignee = EmptyToNull(proposal.Assignee),
                Branch = EmptyToNull(proposal.Branch),
                LabelsJson = SerializeLabels(proposal.Labels),
                CreatedAt = now,
                UpdatedAt = now
            };
            db.FollowUpDrafts.Add(draft);
            draftIds.Add(draft.DraftId);
        }

        AddTimeline(run.Item!, run.Round, run, TimelineEventKind.CommentAdded, ActorKind.Agent, "DotCraft", "Follow-up draft submitted", $"{draftIds.Count} proposal(s) captured.", now);
        await db.SaveChangesAsync(ct);
        return new SubmitFollowUpDraftResponse(draftIds, draftIds.Count);
    }

    public async Task<ItemDetailResponse> UpdateAsync(string draftId, FollowUpDraftUpdateRequest request, OratorioService items, CancellationToken ct)
    {
        var draft = await LoadDraftAsync(draftId, ct);
        EnsureDraftEditable(draft, "edit");

        if (request.Title is not null)
        {
            Require(request.Title, "title");
            draft.Title = request.Title.Trim();
        }

        if (request.Body is not null)
        {
            Require(request.Body, "body");
            draft.Body = request.Body.Trim();
        }

        if (request.Rationale is not null)
        {
            draft.Rationale = EmptyToNull(request.Rationale);
        }

        if (request.Repository is not null)
        {
            draft.Repository = EmptyToNull(request.Repository);
        }

        if (request.Assignee is not null)
        {
            draft.Assignee = EmptyToNull(request.Assignee);
        }

        if (request.Branch is not null)
        {
            draft.Branch = EmptyToNull(request.Branch);
        }

        if (request.Labels is not null)
        {
            draft.LabelsJson = SerializeLabels(request.Labels);
        }

        var now = clock.UtcNow;
        draft.UpdatedAt = now;
        AddTimeline(draft.Item!, draft.Round, draft.Run, TimelineEventKind.ItemUpdated, ActorKind.Operator, "operator", "Follow-up draft edited", draft.Title, now);
        await db.SaveChangesAsync(ct);
        return await items.GetItemDetailByIdAsync(draft.ItemId, ct);
    }

    public async Task<ItemDetailResponse> DiscardAsync(string draftId, OratorioService items, CancellationToken ct)
    {
        var draft = await LoadDraftAsync(draftId, ct);
        EnsureDraftEditable(draft, "discard");
        var now = clock.UtcNow;
        draft.Status = FollowUpDraftStatus.Discarded;
        draft.ResolvedAt = now;
        draft.UpdatedAt = now;
        AddTimeline(draft.Item!, draft.Round, draft.Run, TimelineEventKind.DecisionRecorded, ActorKind.Operator, "operator", "Follow-up draft discarded", draft.Title, now);
        await db.SaveChangesAsync(ct);
        return await items.GetItemDetailByIdAsync(draft.ItemId, ct);
    }

    public async Task<ItemDetailResponse> CreateLocalTaskAsync(string draftId, OratorioService items, CancellationToken ct)
    {
        var draft = await LoadDraftAsync(draftId, ct);
        EnsureDraftEditable(draft, "create");

        var now = clock.UtcNow;
        var route = ResolveLocalTaskRoute(draft);
        var created = new OratorioItem
        {
            Source = "local",
            ExternalId = await GenerateLocalTaskExternalIdAsync(ct),
            Kind = ItemKind.LocalTask,
            Title = draft.Title.Trim(),
            Description = draft.Body.Trim(),
            Repository = route.Repository,
            Assignee = draft.Assignee,
            Branch = route.Branch,
            HeadSha = route.HeadSha,
            LabelsJson = draft.LabelsJson,
            State = ItemState.Discovered,
            CheckState = CheckState.NotConfigured,
            ParentItemId = draft.ItemId,
            GeneratedFromDraftId = draft.DraftId,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Items.Add(created);

        draft.Status = FollowUpDraftStatus.Created;
        draft.CreatedItem = created;
        draft.CreatedItemId = created.ItemId;
        draft.ResolvedAt = now;
        draft.UpdatedAt = now;
        draft.Item!.UpdatedAt = now;

        AddTimeline(draft.Item, draft.Round, draft.Run, TimelineEventKind.DecisionRecorded, ActorKind.Operator, "operator", "Follow-up local task created", created.Title, now);
        AddTimeline(created, null, null, TimelineEventKind.SourceSynced, ActorKind.Operator, "operator", "Local task created from follow-up draft", $"Generated from {draft.Item.Source} {draft.Item.ExternalId}.", now);
        await db.SaveChangesAsync(ct);
        return await items.GetItemDetailByIdAsync(draft.ItemId, ct);
    }

    private async Task<OratorioFollowUpDraft> LoadDraftAsync(string draftId, CancellationToken ct) =>
        await db.FollowUpDrafts
            .Include(x => x.Item)
            .Include(x => x.Round)
            .Include(x => x.Run)
            .FirstOrDefaultAsync(x => x.DraftId == draftId, ct)
        ?? throw OratorioApiException.Conflict("followUpDraftNotFound", "The requested follow-up draft does not exist.", new Dictionary<string, object?> { ["draftId"] = draftId });

    private static void EnsureEligibleRun(OratorioRun run)
    {
        if (run.RunnerKind != "appServer" || run.Item is null || run.Round is null)
        {
            throw OratorioApiException.Conflict("followUpDraftUnsupportedRun", "Follow-up drafts can only be submitted for Oratorio AppServer runs.");
        }
    }

    private static IReadOnlyList<FollowUpProposalRequest> ValidateProposals(SubmitFollowUpDraftRequest request)
    {
        var proposals = request.Proposals ?? [];
        if (proposals.Count == 0)
        {
            throw OratorioApiException.Validation("proposals must include at least one follow-up draft.", new Dictionary<string, object?> { ["field"] = "proposals" });
        }

        foreach (var proposal in proposals)
        {
            Require(proposal.Title, "title");
            Require(proposal.Body, "body");
        }

        return proposals;
    }

    private static void EnsureDraftEditable(OratorioFollowUpDraft draft, string action)
    {
        if (draft.Status != FollowUpDraftStatus.Draft)
        {
            var actionLabel = action == "create" ? "created" : $"{action}ed";
            throw OratorioApiException.Conflict(
                "invalidFollowUpDraftState",
                $"Only draft follow-up drafts can be {actionLabel}.",
                new Dictionary<string, object?> { ["status"] = draft.Status });
        }

        if (draft.Item is null || draft.Round is null || draft.Run is null)
        {
            throw OratorioApiException.Conflict("invalidFollowUpDraftBinding", "Follow-up draft is missing its item, round, or run binding.");
        }
    }

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

    private static void Require(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw OratorioApiException.Validation($"{field} is required.", new Dictionary<string, object?> { ["field"] = field });
        }
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static FollowUpLocalTaskRoute ResolveLocalTaskRoute(OratorioFollowUpDraft draft)
    {
        var parent = draft.Item;
        var repository = EmptyToNull(draft.Repository) ?? EmptyToNull(parent?.Repository);
        var explicitBranch = EmptyToNull(draft.Branch);
        if (explicitBranch is not null)
        {
            return new FollowUpLocalTaskRoute(repository, explicitBranch, null);
        }

        if (CanInheritPullRequestRoute(parent, repository))
        {
            return new FollowUpLocalTaskRoute(repository, EmptyToNull(parent!.Branch), EmptyToNull(parent.HeadSha));
        }

        return new FollowUpLocalTaskRoute(repository, null, null);
    }

    private static bool CanInheritPullRequestRoute(OratorioItem? parent, string? repository) =>
        parent is not null &&
        parent.Kind == ItemKind.PullRequest &&
        parent.Source is "github" or "gitlab" &&
        !string.IsNullOrWhiteSpace(parent.Branch) &&
        SameRepository(repository, parent.Repository);

    private static bool SameRepository(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? SerializeLabels(IReadOnlyList<string>? labels)
    {
        var normalized = (labels ?? [])
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return normalized.Length == 0 ? null : JsonSerializer.Serialize(normalized, JsonOptions);
    }

    private void AddTimeline(OratorioItem item, OratorioRound? round, OratorioRun? run, TimelineEventKind kind, ActorKind actorKind, string actorName, string title, string? body, DateTimeOffset createdAt)
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

    private sealed record FollowUpLocalTaskRoute(string? Repository, string? Branch, string? HeadSha);
}
