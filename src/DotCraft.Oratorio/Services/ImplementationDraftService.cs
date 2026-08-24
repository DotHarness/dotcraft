using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.GitLab;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Sources;

namespace DotCraft.Oratorio.Services;

public sealed class ImplementationDraftService(
    OratorioDbContext db,
    IGitDeliveryClient git,
    IGitHubApiClient gitHub,
    IGitLabApiClient gitLab,
    IOptionsMonitor<GitHubOptions> gitHubOptions,
    IOptionsMonitor<GitLabOptions> gitLabOptions,
    IOptionsMonitor<DotCraftOptions> dotCraftOptions,
    IGitHubCredentialResolver gitHubCredentials,
    IGitLabCredentialResolver gitLabCredentials,
    IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SubmitImplementationDraftResponse> SubmitForRunAsync(
        string runId,
        SubmitImplementationDraftRequest request,
        CancellationToken ct)
    {
        var run = await db.Runs.Include(x => x.Item).Include(x => x.Round).FirstOrDefaultAsync(x => x.RunId == runId, ct)
            ?? throw OratorioApiException.RunNotFound(runId);
        EnsureImplementationRun(run);
        ValidateRequest(request);

        var now = clock.UtcNow;
        var draft = new OratorioImplementationDraft
        {
            ItemId = run.ItemId,
            RoundId = run.RoundId,
            RunId = run.RunId,
            Status = ImplementationDraftStatus.Draft,
            DeliveryPolicy = run.DeliveryPolicy,
            Summary = request.Summary!.Trim(),
            TestsJson = SerializeList(request.Tests),
            RisksJson = SerializeList(request.Risks),
            ChangedFilesJson = SerializeList(request.ChangedFiles),
            ProposedCommitMessage = request.ProposedCommitMessage!.Trim(),
            ProposedPrTitle = request.ProposedPrTitle!.Trim(),
            ProposedPrBody = request.ProposedPrBody!.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ImplementationDrafts.Add(draft);
        AddTimeline(run.Item!, run.Round, run, TimelineEventKind.CommentAdded, ActorKind.Agent, "DotCraft", "Implementation draft submitted", draft.Summary, now);
        await db.SaveChangesAsync(ct);

        return new SubmitImplementationDraftResponse(draft.DraftId, draft.DeliveryPolicy);
    }

    public async Task<bool> RunHasDraftAsync(string runId, CancellationToken ct) =>
        await db.ImplementationDrafts.AnyAsync(x => x.RunId == runId, ct);

    public async Task<ItemDetailResponse> DeliverAsync(string draftId, OratorioService items, CancellationToken ct)
    {
        var draft = await LoadDraftAsync(draftId, ct);
        await DeliverLoadedDraftAsync(draft, ct);
        return await items.GetItemDetailByIdAsync(draft.ItemId, ct);
    }

    public async Task<string> RetryDeliveryFromSourceWriteAsync(string writeId, CancellationToken ct)
    {
        var write = await db.SourceWriteLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.WriteId == writeId, ct)
            ?? throw OratorioApiException.Conflict("sourceWriteNotFound", "The requested source write does not exist.", new Dictionary<string, object?> { ["writeId"] = writeId });
        if (!IsImplementationDeliveryIntent(write.Intent))
        {
            throw OratorioApiException.Conflict("unsupportedSourceWriteRetry", "This source write is not an implementation delivery write.", new Dictionary<string, object?> { ["intent"] = write.Intent });
        }

        if (write.Status != SourceWriteStatus.Failed)
        {
            throw OratorioApiException.Conflict("invalidTransition", "Only failed source writes can be retried.", new Dictionary<string, object?> { ["status"] = write.Status });
        }

        var draft = await db.ImplementationDrafts
            .Include(x => x.Item)
            .Include(x => x.Round)
            .Include(x => x.Run)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(x => x.ItemId == write.ItemId && x.RoundId == write.RoundId, ct)
            ?? throw OratorioApiException.Conflict("implementationDraftNotFound", "The source write is not linked to an implementation draft.", new Dictionary<string, object?> { ["writeId"] = writeId });

        await DeliverLoadedDraftAsync(draft, ct);
        return draft.ItemId;
    }

    public async Task<string> CompleteRunDeliveryAsync(string runId, string agentSummary, CancellationToken ct)
    {
        var draft = await db.ImplementationDrafts
            .Include(x => x.Item)
            .Include(x => x.Round)
            .Include(x => x.Run)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(x => x.RunId == runId, ct);
        if (draft is null)
        {
            throw OratorioApiException.Conflict("implementationDraftMissing", "Implementation run completed without a valid implementation draft.");
        }

        if (draft.DeliveryPolicy == DeliveryPolicy.AutoPr)
        {
            await DeliverLoadedDraftAsync(draft, ct);
        }

        return BuildHandoffSummary(draft, agentSummary);
    }

    private async Task DeliverLoadedDraftAsync(OratorioImplementationDraft draft, CancellationToken ct)
    {
        EnsureDraftCanDeliver(draft);
        var item = draft.Item!;
        var run = draft.Run!;
        var round = draft.Round!;
        var now = clock.UtcNow;
        draft.ErrorCode = null;
        draft.ErrorMessage = null;

        try
        {
            var route = ResolveDeliveryRoute(item, run);
            EnsureDeliveryCredentials(route);

            var branchName = ResolveBranchName(run, item, draft);
            var commitMessage = RenderCommitMessage(draft, item);
            var prTitle = RenderPrTitle(draft, item);
            var prBody = RenderPrBody(draft, item);
            var sourceActor = SourceActor(route);

            var commit = await EnsureCommitAsync(item, draft, round, run, route, branchName, commitMessage, ct);
            branchName = commit.BranchName;
            var commitSha = commit.CommitSha;
            draft.BranchName = branchName;
            draft.CommitSha = commitSha;
            draft.UpdatedAt = clock.UtcNow;

            var push = await EnsureBranchPushedAsync(item, draft, round, run, route, branchName, commitSha, sourceActor, ct);
            branchName = push.BranchName;
            commitSha = push.CommitSha;
            draft.BranchName = branchName;
            draft.CommitSha = commitSha;
            draft.UpdatedAt = clock.UtcNow;

            var reviewTarget = await CreateReviewTargetAsync(item, draft, round, run, route, branchName, commitSha, prTitle, prBody, ct);
            draft.Status = ImplementationDraftStatus.Delivered;
            draft.BranchName = branchName;
            draft.CommitSha = commitSha;
            draft.PullRequestItemId = reviewTarget.Item.ItemId;
            draft.PullRequestUrl = reviewTarget.Url;
            draft.SourceWrite = reviewTarget.Write;
            draft.SourceWriteId = reviewTarget.Write.WriteId;
            draft.DeliveredAt = clock.UtcNow;
            draft.UpdatedAt = draft.DeliveredAt.Value;
            AddTimeline(item, round, run, TimelineEventKind.SourceWriteSucceeded, ActorKind.Source, sourceActor, $"Generated {ReviewTargetName(route)} created", reviewTarget.Url, clock.UtcNow);
            AddTimeline(reviewTarget.Item, null, null, TimelineEventKind.SourceSynced, ActorKind.System, "Oratorio", $"Generated {ReviewTargetName(route)} linked", $"Generated from {item.Source} {item.ExternalId}.", clock.UtcNow);
            await db.SaveChangesAsync(ct);
        }
        catch (OratorioApiException ex)
        {
            MarkDraftFailed(draft, ex.Code, ex.Message);
            await db.SaveChangesAsync(ct);
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException)
        {
            MarkDraftFailed(draft, "deliveryFailed", ex.Message);
            await db.SaveChangesAsync(ct);
            throw OratorioApiException.Conflict("deliveryFailed", ex.Message);
        }
    }

    private async Task<OratorioImplementationDraft> LoadDraftAsync(string draftId, CancellationToken ct) =>
        await db.ImplementationDrafts
            .Include(x => x.Item)
            .Include(x => x.Round)
            .Include(x => x.Run)
            .FirstOrDefaultAsync(x => x.DraftId == draftId, ct)
        ?? throw OratorioApiException.Conflict("implementationDraftNotFound", "The requested implementation draft does not exist.", new Dictionary<string, object?> { ["draftId"] = draftId });

    private static void EnsureImplementationRun(OratorioRun run)
    {
        if (run.Purpose != RunPurpose.Implementation)
        {
            throw OratorioApiException.Conflict("implementationDraftUnsupportedRun", "Implementation drafts can only be submitted for implementation runs.");
        }

        if (run.Item is null || run.Round is null || run.Item.Kind is not (ItemKind.Issue or ItemKind.LocalTask) || (run.Item.Kind == ItemKind.Issue && run.Item.Source is not ("github" or "gitlab")))
        {
            throw OratorioApiException.Conflict("implementationUnsupportedItem", "Implementation drafts are only supported for GitHub/GitLab issue and local task runs.");
        }
    }

    private static void ValidateRequest(SubmitImplementationDraftRequest request)
    {
        Require(request.Summary, "summary");
        Require(request.ProposedCommitMessage, "proposedCommitMessage");
        Require(request.ProposedPrTitle, "proposedPrTitle");
        Require(request.ProposedPrBody, "proposedPrBody");
    }

    private static void EnsureDraftCanDeliver(OratorioImplementationDraft draft)
    {
        if (draft.Status == ImplementationDraftStatus.DeliveryFailed)
        {
            return;
        }

        if (draft.Status != ImplementationDraftStatus.Draft)
        {
            throw OratorioApiException.Conflict("invalidImplementationDraftState", "Only draft implementation drafts can be delivered.", new Dictionary<string, object?> { ["status"] = draft.Status });
        }

        if (draft.Item is null || draft.Round is null || draft.Run is null)
        {
            throw OratorioApiException.Conflict("invalidImplementationDraftBinding", "Implementation draft is missing its run binding.");
        }
    }

    private DeliveryRoute ResolveDeliveryRoute(OratorioItem item, OratorioRun run)
    {
        if (item.Kind == ItemKind.Issue)
        {
            if (TryResolveExplicitRoute(item.Source, item.Repository, out var issueRoute))
            {
                return issueRoute;
            }

            throw OratorioApiException.Conflict(
                item.Source == "gitlab" ? "invalidGitLabTarget" : "missingRepository",
                item.Source == "gitlab"
                    ? "Implementation delivery requires a configured GitLab project for this issue."
                    : "Implementation delivery requires a repository.");
        }

        if (item.Kind == ItemKind.LocalTask)
        {
            if (TryResolveLocalTaskRoute(item.Repository, run, out var localRoute))
            {
                item.Repository = StoredRepository(localRoute);
                return localRoute;
            }

            throw OratorioApiException.Conflict(
                "missingRepository",
                "Implementation delivery requires exactly one target repository for this local task. Edit the task repository, configure a single GitHub/GitLab project, or add a repository workspace mapping that matches the run workspace.");
        }

        throw OratorioApiException.Conflict("implementationUnsupportedItem", "Implementation delivery only supports issue and local task runs.");
    }

    private bool TryResolveExplicitRoute(string provider, string? repository, out DeliveryRoute route)
    {
        route = DeliveryRoute.Empty;
        if (SourceProjectKey.TryParse(repository, out var key))
        {
            if (!string.Equals(key.Provider, provider, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return TryCreateRoute(key, out route);
        }

        if (provider == "github" && GitHubRepositoryRef.TryParse(NormalizeRepository(repository) ?? "", out var gitHubRepository))
        {
            return TryCreateRoute(SourceProjectKey.FromGitHubRepository(gitHubRepository.FullName, gitHubOptions.CurrentValue.Endpoint), out route);
        }

        if (provider == "gitlab" && SourceProjectKey.TryNormalizeForProvider("gitlab", repository, gitLabOptions.CurrentValue.Endpoint, out var gitLabKey))
        {
            return TryCreateRoute(gitLabKey, out route);
        }

        return false;
    }

    private bool TryResolveLocalTaskRoute(string? repository, OratorioRun run, out DeliveryRoute route)
    {
        route = DeliveryRoute.Empty;
        if (!string.IsNullOrWhiteSpace(repository))
        {
            var explicitRoutes = CandidateRoutes(repository).DistinctBy(x => x.Project.Key, StringComparer.OrdinalIgnoreCase).ToArray();
            if (explicitRoutes.Length == 1)
            {
                route = explicitRoutes[0];
                return true;
            }
        }

        var workspaceRoutes = InferRoutesFromWorkspace(run.BaseWorkspacePath).DistinctBy(x => x.Project.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        if (workspaceRoutes.Length == 1)
        {
            route = workspaceRoutes[0];
            return true;
        }

        var configuredRoutes = ConfiguredRoutes().DistinctBy(x => x.Project.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        if (configuredRoutes.Length == 1)
        {
            route = configuredRoutes[0];
            return true;
        }

        return false;
    }

    private IEnumerable<DeliveryRoute> InferRoutesFromWorkspace(string? workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            yield break;
        }

        foreach (var mapping in dotCraftOptions.CurrentValue.RepositoryWorkspaceRoutes.Where(mapping => SamePath(mapping.WorkspacePath, workspacePath)))
        {
            foreach (var route in CandidateRoutes(mapping.Project))
            {
                yield return route;
            }
        }
    }

    private IEnumerable<DeliveryRoute> ConfiguredRoutes()
    {
        foreach (var repository in gitHubOptions.CurrentValue.Repositories)
        {
            if (SourceProjectKey.TryNormalizeForProvider("github", repository, gitHubOptions.CurrentValue.Endpoint, out var key) &&
                TryCreateRoute(key, out var route))
            {
                yield return route;
            }
        }

        foreach (var project in gitLabOptions.CurrentValue.Projects)
        {
            if (SourceProjectKey.TryNormalizeForProvider("gitlab", project, gitLabOptions.CurrentValue.Endpoint, out var key) &&
                TryCreateRoute(key, out var route))
            {
                yield return route;
            }
        }
    }

    private IEnumerable<DeliveryRoute> CandidateRoutes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        if (SourceProjectKey.TryParse(value, out var parsed) && TryCreateRoute(parsed, out var parsedRoute))
        {
            yield return parsedRoute;
            yield break;
        }

        if (SourceProjectKey.TryNormalizeForProvider("gitlab", value, gitLabOptions.CurrentValue.Endpoint, out var gitLabKey) &&
            TryCreateRoute(gitLabKey, out var gitLabRoute) &&
            IsConfiguredGitLabProject(gitLabKey.ProjectPath))
        {
            yield return gitLabRoute;
            yield break;
        }

        if (SourceProjectKey.TryNormalizeForProvider("github", value, gitHubOptions.CurrentValue.Endpoint, out var gitHubKey) &&
            TryCreateRoute(gitHubKey, out var gitHubRoute))
        {
            yield return gitHubRoute;
        }
    }

    private static bool TryCreateRoute(SourceProjectKey key, out DeliveryRoute route)
    {
        route = DeliveryRoute.Empty;
        if (string.Equals(key.Provider, "github", StringComparison.OrdinalIgnoreCase) &&
            GitHubRepositoryRef.TryParse(key.ProjectPath, out var repository))
        {
            route = new DeliveryRoute("github", key, repository, null);
            return true;
        }

        if (string.Equals(key.Provider, "gitlab", StringComparison.OrdinalIgnoreCase) &&
            GitLabProjectRef.TryParse(key.ProjectPath, out var project))
        {
            route = new DeliveryRoute("gitlab", key, null, project);
            return true;
        }

        return false;
    }

    private void EnsureDeliveryCredentials(DeliveryRoute route)
    {
        if (route.Provider == "github")
        {
            if (!gitHubCredentials.Resolve(gitHubOptions.CurrentValue).CanWrite)
            {
                throw OratorioApiException.Conflict("missingCredentials", "GitHub App write credentials are required for Auto PR delivery.");
            }

            return;
        }

        var current = gitLabOptions.CurrentValue;
        if (!current.Enabled)
        {
            throw OratorioApiException.Conflict("gitlabDisabled", "GitLab provider is disabled.");
        }

        if (!current.WritesEnabled)
        {
            throw OratorioApiException.Conflict("gitlabWritesDisabled", "GitLab writes are disabled.");
        }

        if (!IsConfiguredGitLabProject(route.Project.ProjectPath))
        {
            throw OratorioApiException.Conflict("gitlabProjectNotConfigured", "The target GitLab project is not configured for Oratorio writes.");
        }

        var project = route.GitLabProject ?? new GitLabProjectRef(route.Project.ProjectPath);
        if (!gitLabCredentials.ResolveProject(current, project).HasToken)
        {
            throw OratorioApiException.Conflict("missingCredentials", $"GitLab project profile token is required for Auto MR delivery to {project.ProjectPath}.");
        }
    }

    private static string RenderBranchName(OratorioRun run, OratorioItem item) =>
        string.IsNullOrWhiteSpace(run.WorktreeBranch)
            ? $"oratorio/implementation/{ShortId(item.ItemId)}"
            : run.WorktreeBranch!;

    private static string ResolveBranchName(OratorioRun run, OratorioItem item, OratorioImplementationDraft draft) =>
        string.IsNullOrWhiteSpace(draft.BranchName) ? RenderBranchName(run, item) : draft.BranchName!;

    private static string RenderCommitMessage(OratorioImplementationDraft draft, OratorioItem item) =>
        FirstNonEmpty(draft.ProposedCommitMessage, $"Implement {item.Title}");

    private static string RenderPrTitle(OratorioImplementationDraft draft, OratorioItem item) =>
        FirstNonEmpty(draft.ProposedPrTitle, item.Title);

    private static string RenderPrBody(OratorioImplementationDraft draft, OratorioItem item)
    {
        var body = FirstNonEmpty(draft.ProposedPrBody, draft.Summary);
        if (item.Source is "github" or "gitlab" &&
            item.Kind == ItemKind.Issue &&
            TryResolveNumber(item.ExternalId, out var number) &&
            !body.Contains($"#{number}", StringComparison.Ordinal))
        {
            body += $"\n\nRefs #{number}";
        }
        else if (item.Kind == ItemKind.LocalTask && !body.Contains(item.ItemId, StringComparison.OrdinalIgnoreCase))
        {
            body += $"\n\nGenerated by Oratorio from local task {item.ShortId ?? ShortId(item.ItemId)}.";
        }

        return body;
    }

    private async Task<CommitDeliveryStep> EnsureCommitAsync(
        OratorioItem item,
        OratorioImplementationDraft draft,
        OratorioRound round,
        OratorioRun run,
        DeliveryRoute route,
        string branchName,
        string commitMessage,
        CancellationToken ct)
    {
        var existing = await FindDeliveryWriteAsync(draft, SourceWriteKind.LocalCommit, "implementationCommit", SourceWriteStatus.Succeeded, ct);
        if (existing is not null)
        {
            return new CommitDeliveryStep(
                ReadOptionalString(existing.RequestJson, "branchName") ?? branchName,
                ReadOptionalString(existing.ResponseJson, "commitSha") ?? existing.ExternalId ?? draft.CommitSha ?? "");
        }

        EnsureReadyWorktree(run);
        var diffFiles = await git.GetChangedFilesAsync(run.WorktreePath!, ct);
        if (diffFiles.Count == 0)
        {
            throw OratorioApiException.Conflict("emptyDiff", "Implementation draft cannot be delivered because the managed worktree has no diff.");
        }

        var request = new ImplementationCommitRequest(branchName, commitMessage, diffFiles);
        var write = await GetOrCreateDeliveryWriteAsync(item, draft, round, route, SourceWriteKind.LocalCommit, "implementationCommit", request, ct);
        AddTimeline(item, round, run, TimelineEventKind.SourceWriteQueued, ActorKind.System, "Oratorio", "Implementation commit queued", commitMessage, clock.UtcNow);
        var commitSha = await git.CommitAllAsync(run.WorktreePath!, commitMessage, ct);
        MarkWriteSucceeded(write, commitSha, null, JsonSerializer.Serialize(new { commitSha }, JsonOptions), clock.UtcNow);
        AddTimeline(item, round, run, TimelineEventKind.SourceWriteSucceeded, ActorKind.System, "Oratorio", "Implementation commit created", commitSha, clock.UtcNow);
        return new CommitDeliveryStep(branchName, commitSha);
    }

    private async Task<PushDeliveryStep> EnsureBranchPushedAsync(
        OratorioItem item,
        OratorioImplementationDraft draft,
        OratorioRound round,
        OratorioRun run,
        DeliveryRoute route,
        string branchName,
        string commitSha,
        string sourceActor,
        CancellationToken ct)
    {
        var existing = await FindDeliveryWriteAsync(draft, SourceWriteKind.BranchPush, "implementationBranchPush", SourceWriteStatus.Succeeded, ct);
        if (existing is not null)
        {
            return new PushDeliveryStep(
                ReadOptionalString(existing.RequestJson, "branchName") ?? existing.ExternalId ?? branchName,
                ReadOptionalString(existing.RequestJson, "commitSha") ?? commitSha);
        }

        EnsureReadyWorktree(run);
        var request = new BranchPushRequest(branchName, commitSha);
        var write = await GetOrCreateDeliveryWriteAsync(item, draft, round, route, SourceWriteKind.BranchPush, "implementationBranchPush", request, ct);
        AddTimeline(item, round, run, TimelineEventKind.SourceWriteQueued, ActorKind.Source, sourceActor, "Branch push queued", branchName, clock.UtcNow);
        await git.PushBranchAsync(run.WorktreePath!, route.Project, branchName, ct);
        MarkWriteSucceeded(write, branchName, null, JsonSerializer.Serialize(new { branchName, commitSha }, JsonOptions), clock.UtcNow);
        AddTimeline(item, round, run, TimelineEventKind.SourceWriteSucceeded, ActorKind.Source, sourceActor, "Branch pushed", branchName, clock.UtcNow);
        return new PushDeliveryStep(branchName, commitSha);
    }

    private async Task<DeliveryReviewTarget> CreateReviewTargetAsync(
        OratorioItem item,
        OratorioImplementationDraft draft,
        OratorioRound round,
        OratorioRun run,
        DeliveryRoute route,
        string branchName,
        string commitSha,
        string prTitle,
        string prBody,
        CancellationToken ct)
    {
        var existingGeneratedPr = await FindExistingOpenGeneratedPrAsync(item, route, ct);
        if (existingGeneratedPr is not null)
        {
            return await UpdateExistingReviewTargetAsync(item, draft, round, route, existingGeneratedPr, branchName, commitSha, ct);
        }

        var baseBranch = await ResolveBaseBranchAsync(item, route, ct);
        if (route.Provider == "github")
        {
            var existingWrite = await FindDeliveryWriteAsync(draft, SourceWriteKind.PullRequestCreation, "implementationPullRequestCreate", SourceWriteStatus.Succeeded, ct);
            if (existingWrite is not null &&
                TryDeserialize(existingWrite.ResponseJson, out GitHubPullRequestCreateResponse existingCreated))
            {
                var existingItem = UpsertGeneratedPrItem(item, draft, existingCreated, route.GitHubRepository!, branchName, commitSha, clock.UtcNow);
                return new DeliveryReviewTarget(existingItem, existingCreated.HtmlUrl, existingWrite);
            }

            var failedWrite = await FindLatestDeliveryWriteAsync(draft, SourceWriteKind.PullRequestCreation, "implementationPullRequestCreate", ct);
            var request = ResolvePullRequestRequest(failedWrite, prTitle, branchName, baseBranch, prBody);
            var write = await GetOrCreateDeliveryWriteAsync(item, draft, round, route, SourceWriteKind.PullRequestCreation, "implementationPullRequestCreate", request, ct);
            AddTimeline(item, round, run, TimelineEventKind.SourceWriteQueued, ActorKind.Source, "GitHub", "Pull request creation queued", request.Title, clock.UtcNow);

            GitHubPullRequestCreateResponse created;
            try
            {
                created = await gitHub.CreatePullRequestAsync(route.GitHubRepository!, request.Title, request.Head, request.Base, request.Body, draft: false, ct);
            }
            catch (HttpRequestException ex) when (IsPullRequestAlreadyExists(ex))
            {
                var existing = await FindOpenGitHubPullRequestByHeadAsync(route.GitHubRepository!, request.Head, ct)
                    ?? throw OratorioApiException.Conflict(
                        "pullRequestAlreadyExists",
                        "A pull request already exists for this branch but could not be resolved for linking.",
                        new Dictionary<string, object?> { ["head"] = request.Head });
                created = new GitHubPullRequestCreateResponse(existing.Id, existing.Number, existing.HtmlUrl, existing.Title, existing.Head, existing.Base);
            }

            MarkWriteSucceeded(write, $"pr:{route.GitHubRepository!.FullName}#{created.Number}", created.HtmlUrl, JsonSerializer.Serialize(created, JsonOptions), clock.UtcNow);
            var prItem = UpsertGeneratedPrItem(item, draft, created, route.GitHubRepository, request.Head, commitSha, clock.UtcNow);
            return new DeliveryReviewTarget(prItem, created.HtmlUrl, write);
        }

        var existingMrWrite = await FindDeliveryWriteAsync(draft, SourceWriteKind.MergeRequestCreation, "implementationMergeRequestCreate", SourceWriteStatus.Succeeded, ct);
        if (existingMrWrite is not null &&
            TryDeserialize(existingMrWrite.ResponseJson, out GitLabMergeRequestCreateResponse existingMr))
        {
            var existingMrItem = UpsertGeneratedMrItem(item, draft, existingMr, route, branchName, commitSha, clock.UtcNow);
            return new DeliveryReviewTarget(existingMrItem, existingMr.WebUrl, existingMrWrite);
        }

        var failedMrWrite = await FindLatestDeliveryWriteAsync(draft, SourceWriteKind.MergeRequestCreation, "implementationMergeRequestCreate", ct);
        var mrRequest = ResolveMergeRequestRequest(failedMrWrite, prTitle, branchName, baseBranch, prBody);
        var mrWrite = await GetOrCreateDeliveryWriteAsync(item, draft, round, route, SourceWriteKind.MergeRequestCreation, "implementationMergeRequestCreate", mrRequest, ct);
        AddTimeline(item, round, run, TimelineEventKind.SourceWriteQueued, ActorKind.Source, "GitLab", "Merge request creation queued", mrRequest.Title, clock.UtcNow);

        var createdMr = await gitLab.CreateMergeRequestAsync(route.GitLabProject!, mrRequest.Title, mrRequest.SourceBranch, mrRequest.TargetBranch, mrRequest.Description, draft: false, ct);
        var externalId = BuildGitLabMergeRequestExternalId(route.Project, createdMr.Iid);
        MarkWriteSucceeded(mrWrite, externalId, createdMr.WebUrl, JsonSerializer.Serialize(createdMr, JsonOptions), clock.UtcNow);
        var mrItem = UpsertGeneratedMrItem(item, draft, createdMr, route, mrRequest.SourceBranch, commitSha, clock.UtcNow);
        return new DeliveryReviewTarget(mrItem, createdMr.WebUrl, mrWrite);
    }

    private async Task<OratorioItem?> FindExistingOpenGeneratedPrAsync(OratorioItem item, DeliveryRoute route, CancellationToken ct) =>
        await db.Items
            .Where(x =>
                x.ParentItemId == item.ItemId &&
                x.Kind == ItemKind.PullRequest &&
                x.Source == route.Provider &&
                x.SourceState == SourceState.Open &&
                x.State != ItemState.Archived)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

    private async Task<DeliveryReviewTarget> UpdateExistingReviewTargetAsync(
        OratorioItem item,
        OratorioImplementationDraft draft,
        OratorioRound round,
        DeliveryRoute route,
        OratorioItem existingPr,
        string branchName,
        string commitSha,
        CancellationToken ct)
    {
        var now = clock.UtcNow;
        var request = new PullRequestUpdateRequest(branchName, commitSha, existingPr.ExternalId);
        var write = await GetOrCreateDeliveryWriteAsync(item, draft, round, route, SourceWriteKind.PullRequestUpdate, "implementationBranchUpdate", request, ct);
        existingPr.HeadSha = commitSha;
        existingPr.Branch = branchName;
        existingPr.SourceState = SourceState.Open;
        existingPr.SourceUpdatedAt = now;
        existingPr.SourceDetailsStatus = SourceDetailsStatus.Stale;
        existingPr.UpdatedAt = now;
        MarkWriteSucceeded(write, existingPr.ExternalId, existingPr.ExternalUrl, JsonSerializer.Serialize(new { existingPr.ExternalId, branchName, commitSha }, JsonOptions), now);
        return new DeliveryReviewTarget(existingPr, existingPr.ExternalUrl, write);
    }

    private async Task<GitHubPullRequest?> FindOpenGitHubPullRequestByHeadAsync(GitHubRepositoryRef repository, string head, CancellationToken ct)
    {
        var headRef = head.Contains(':', StringComparison.Ordinal) ? head[(head.IndexOf(':', StringComparison.Ordinal) + 1)..] : head;
        var open = await gitHub.ListPullRequestsAsync(repository, GitHubListState.Open, ct);
        return open.FirstOrDefault(pr => string.Equals(pr.Head.Ref, headRef, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPullRequestAlreadyExists(HttpRequestException ex) =>
        ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity &&
        ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase);

    private async Task<OratorioSourceWriteLog?> FindDeliveryWriteAsync(OratorioImplementationDraft draft, SourceWriteKind kind, string intent, SourceWriteStatus status, CancellationToken ct) =>
        await db.SourceWriteLogs
            .Where(x =>
                x.ItemId == draft.ItemId &&
                x.RoundId == draft.RoundId &&
                x.Kind == kind &&
                x.Intent == intent &&
                x.Status == status)
            .OrderByDescending(x => x.CompletedAt ?? x.CreatedAt)
            .FirstOrDefaultAsync(ct);

    private async Task<OratorioSourceWriteLog?> FindLatestDeliveryWriteAsync(OratorioImplementationDraft draft, SourceWriteKind kind, string intent, CancellationToken ct) =>
        await db.SourceWriteLogs
            .Where(x =>
                x.ItemId == draft.ItemId &&
                x.RoundId == draft.RoundId &&
                x.Kind == kind &&
                x.Intent == intent)
            .OrderByDescending(x => x.CompletedAt ?? x.CreatedAt)
            .FirstOrDefaultAsync(ct);

    private async Task<OratorioSourceWriteLog> GetOrCreateDeliveryWriteAsync(
        OratorioItem item,
        OratorioImplementationDraft draft,
        OratorioRound round,
        DeliveryRoute route,
        SourceWriteKind kind,
        string intent,
        object request,
        CancellationToken ct)
    {
        var write = await FindLatestDeliveryWriteAsync(draft, kind, intent, ct);
        if (write is null || write.Status == SourceWriteStatus.Succeeded)
        {
            write = CreateWrite(item, round, route, kind, intent, null, request, clock.UtcNow);
            db.SourceWriteLogs.Add(write);
            return write;
        }

        write.Status = SourceWriteStatus.Pending;
        write.ErrorCode = null;
        write.ErrorMessage = null;
        write.CompletedAt = null;
        write.RequestJson = JsonSerializer.Serialize(request, JsonOptions);
        write.UpdatedAt = clock.UtcNow;
        return write;
    }

    private static void EnsureReadyWorktree(OratorioRun run)
    {
        if (string.IsNullOrWhiteSpace(run.WorktreePath) || run.WorktreeStatus is not (WorktreeStatus.Ready or WorktreeStatus.CleanupPending))
        {
            throw OratorioApiException.Conflict("managedWorktreeMissing", "Implementation delivery requires a ready Oratorio-managed worktree.");
        }
    }

    private static GitHubPullRequestDeliveryRequest ResolvePullRequestRequest(OratorioSourceWriteLog? write, string title, string head, string @base, string body)
    {
        if (TryDeserialize(write?.RequestJson, out GitHubPullRequestDeliveryRequest parsed))
        {
            return new GitHubPullRequestDeliveryRequest(
                FirstNonEmptyOrDefault(parsed.Title, title),
                FirstNonEmptyOrDefault(parsed.Head, head),
                FirstNonEmptyOrDefault(parsed.Base, @base),
                FirstNonEmptyOrDefault(parsed.Body, body));
        }

        return new GitHubPullRequestDeliveryRequest(title, head, @base, body);
    }

    private static GitLabMergeRequestDeliveryRequest ResolveMergeRequestRequest(OratorioSourceWriteLog? write, string title, string sourceBranch, string targetBranch, string description)
    {
        if (TryDeserialize(write?.RequestJson, out GitLabMergeRequestDeliveryRequest parsed))
        {
            return new GitLabMergeRequestDeliveryRequest(
                FirstNonEmptyOrDefault(parsed.Title, title),
                FirstNonEmptyOrDefault(parsed.SourceBranch, sourceBranch),
                FirstNonEmptyOrDefault(parsed.TargetBranch, targetBranch),
                FirstNonEmptyOrDefault(parsed.Description, description));
        }

        return new GitLabMergeRequestDeliveryRequest(title, sourceBranch, targetBranch, description);
    }

    private async Task<string> ResolveBaseBranchAsync(OratorioItem item, DeliveryRoute route, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(item.Branch))
        {
            return item.Branch!;
        }

        if (route.Provider == "gitlab")
        {
            var project = await gitLab.GetProjectAsync(route.GitLabProject!, ct);
            if (!string.IsNullOrWhiteSpace(project.DefaultBranch))
            {
                return project.DefaultBranch!;
            }
        }

        return "main";
    }

    private OratorioItem UpsertGeneratedPrItem(OratorioItem parent, OratorioImplementationDraft draft, GitHubPullRequestCreateResponse created, GitHubRepositoryRef repository, string branchName, string commitSha, DateTimeOffset now)
    {
        var externalId = $"pr:{repository.FullName}#{created.Number}";
        var item = db.Items.Local.FirstOrDefault(x => x.Source == "github" && x.ExternalId == externalId)
            ?? db.Items.FirstOrDefault(x => x.Source == "github" && x.ExternalId == externalId);
        if (item is null)
        {
            item = new OratorioItem
            {
                Source = "github",
                ExternalId = externalId,
                Kind = ItemKind.PullRequest,
                CreatedAt = now
            };
            db.Items.Add(item);
        }

        item.Title = created.Title;
        item.Description = draft.ProposedPrBody;
        item.Repository = repository.FullName;
        item.Branch = branchName;
        item.ExternalUrl = created.HtmlUrl;
        item.HeadSha = commitSha;
        item.SourceState = SourceState.Open;
        item.State = ItemState.Discovered;
        item.CheckState = CheckState.Attention;
        item.ParentItemId = parent.ItemId;
        item.GeneratedFromDraftId = draft.DraftId;
        item.SourceUpdatedAt = now;
        item.LastSourceSyncAt = now;
        item.UpdatedAt = now;
        return item;
    }

    private OratorioItem UpsertGeneratedMrItem(OratorioItem parent, OratorioImplementationDraft draft, GitLabMergeRequestCreateResponse created, DeliveryRoute route, string branchName, string commitSha, DateTimeOffset now)
    {
        var externalId = BuildGitLabMergeRequestExternalId(route.Project, created.Iid);
        var item = db.Items.Local.FirstOrDefault(x => x.Source == "gitlab" && x.ExternalId == externalId)
            ?? db.Items.FirstOrDefault(x => x.Source == "gitlab" && x.ExternalId == externalId);
        if (item is null)
        {
            item = new OratorioItem
            {
                Source = "gitlab",
                ExternalId = externalId,
                Kind = ItemKind.PullRequest,
                CreatedAt = now
            };
            db.Items.Add(item);
        }

        item.Title = created.Title;
        item.Description = created.Description ?? draft.ProposedPrBody;
        item.Repository = route.Project.Key;
        item.Branch = branchName;
        item.ExternalUrl = created.WebUrl;
        item.HeadSha = created.Sha ?? created.DiffRefs?.HeadSha ?? commitSha;
        item.SourceState = SourceState.Open;
        item.State = ItemState.Discovered;
        item.CheckState = CheckState.Attention;
        item.ParentItemId = parent.ItemId;
        item.GeneratedFromDraftId = draft.DraftId;
        item.SourceUpdatedAt = now;
        item.LastSourceSyncAt = now;
        item.SourceDetailsStatus = SourceDetailsStatus.Stale;
        item.UpdatedAt = now;
        return item;
    }

    private OratorioSourceWriteLog CreateWrite(OratorioItem item, OratorioRound round, DeliveryRoute route, SourceWriteKind kind, string intent, int? number, object request, DateTimeOffset now) =>
        new()
        {
            ItemId = item.ItemId,
            RoundId = round.RoundId,
            Source = kind == SourceWriteKind.LocalCommit ? "git" : route.Provider,
            Kind = kind,
            Intent = intent,
            Status = SourceWriteStatus.Pending,
            Repository = DisplayRepository(route),
            Number = number,
            RequestJson = JsonSerializer.Serialize(request, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now
        };

    private static void MarkWriteSucceeded(OratorioSourceWriteLog write, string externalId, string? externalUrl, string responseJson, DateTimeOffset now)
    {
        write.Status = SourceWriteStatus.Succeeded;
        write.ExternalId = externalId;
        write.ExternalUrl = externalUrl;
        write.ResponseJson = responseJson;
        write.AttemptCount++;
        write.CompletedAt = now;
        write.UpdatedAt = now;
    }

    private void MarkDraftFailed(OratorioImplementationDraft draft, string code, string message)
    {
        var now = clock.UtcNow;
        draft.Status = ImplementationDraftStatus.DeliveryFailed;
        draft.ErrorCode = code;
        draft.ErrorMessage = message;
        draft.UpdatedAt = now;
        foreach (var write in db.SourceWriteLogs.Local.Where(x => x.ItemId == draft.ItemId && x.RoundId == draft.RoundId && x.Status == SourceWriteStatus.Pending))
        {
            write.Status = SourceWriteStatus.Failed;
            write.ErrorCode = code;
            write.ErrorMessage = message;
            write.CompletedAt = now;
            write.UpdatedAt = now;
        }

        if (draft.Item is not null)
        {
            AddTimeline(draft.Item, draft.Round, draft.Run, TimelineEventKind.SourceWriteFailed, ActorKind.System, "Oratorio", "Implementation delivery failed", message, now);
        }
    }

    private static string BuildHandoffSummary(OratorioImplementationDraft draft, string agentSummary)
    {
        var lines = new List<string> { draft.Summary };
        if (!string.IsNullOrWhiteSpace(draft.PullRequestUrl))
        {
            lines.Add($"Generated PR: {draft.PullRequestUrl}");
        }
        if (!string.IsNullOrWhiteSpace(draft.BranchName))
        {
            lines.Add($"Branch: {draft.BranchName}");
        }
        if (!string.IsNullOrWhiteSpace(draft.CommitSha))
        {
            lines.Add($"Commit: {draft.CommitSha}");
        }
        if (!string.IsNullOrWhiteSpace(agentSummary) && !string.Equals(agentSummary.Trim(), draft.Summary.Trim(), StringComparison.Ordinal))
        {
            lines.Add(agentSummary.Trim());
        }

        return string.Join("\n\n", lines.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string SerializeList(IReadOnlyList<string>? values) =>
        JsonSerializer.Serialize((values ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray(), JsonOptions);

    private static void Require(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw OratorioApiException.Validation($"{field} is required.", new Dictionary<string, object?> { ["field"] = field });
        }
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))!.Trim();

    private static string FirstNonEmptyOrDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static bool SamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var leftFull = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rightFull = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(leftFull, rightFull, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeRepository(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            return null;
        }

        return SourceProjectKey.NormalizeGitHubRepository(repository);
    }

    private bool IsConfiguredGitLabProject(string projectPath)
    {
        var normalized = SourceProjectKey.NormalizeGitLabProjectPath(projectPath);
        return !string.IsNullOrWhiteSpace(normalized) &&
            gitLabOptions.CurrentValue.Projects
                .Select(SourceProjectKey.NormalizeGitLabProjectPath)
                .Any(candidate => string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string DisplayRepository(DeliveryRoute route) =>
        route.Provider == "github" ? route.Project.ProjectPath : route.Project.Key;

    private static string StoredRepository(DeliveryRoute route) =>
        route.Provider == "github" ? route.Project.ProjectPath : route.Project.Key;

    private static string SourceActor(DeliveryRoute route) =>
        route.Provider == "gitlab" ? "GitLab" : "GitHub";

    private static string ReviewTargetName(DeliveryRoute route) =>
        route.Provider == "gitlab" ? "merge request" : "pull request";

    private static string BuildGitLabMergeRequestExternalId(SourceProjectKey project, int iid) =>
        $"mr:{project.Instance}/{project.ProjectPath}!{iid}";

    private static bool TryResolveNumber(string externalId, out int number)
    {
        number = 0;
        var hash = externalId.LastIndexOf('#');
        return hash >= 0 && int.TryParse(externalId[(hash + 1)..], out number);
    }

    private static string? ReadOptionalString(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(property, out var element) ? element.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool IsImplementationDeliveryIntent(string? intent) =>
        intent is "implementationCommit" or "implementationBranchPush" or "implementationPullRequestCreate" or "implementationMergeRequestCreate" or "implementationBranchUpdate";

    private static bool TryDeserialize<T>(string? json, out T value)
    {
        value = default!;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<T>(json, JsonOptions);
            if (parsed is null)
            {
                return false;
            }

            value = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ShortId(string value) => value.Length <= 12 ? value : value[..12];

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

    private sealed record DeliveryRoute(
        string Provider,
        SourceProjectKey Project,
        GitHubRepositoryRef? GitHubRepository,
        GitLabProjectRef? GitLabProject)
    {
        public static DeliveryRoute Empty { get; } = new("", new SourceProjectKey("", "", ""), null, null);
    }

    private sealed record DeliveryReviewTarget(OratorioItem Item, string? Url, OratorioSourceWriteLog Write);
    private sealed record CommitDeliveryStep(string BranchName, string CommitSha);
    private sealed record PushDeliveryStep(string BranchName, string CommitSha);
    private sealed record ImplementationCommitRequest(string BranchName, string CommitMessage, IReadOnlyList<string> ChangedFiles);
    private sealed record BranchPushRequest(string BranchName, string CommitSha);
    private sealed record GitHubPullRequestDeliveryRequest(string Title, string Head, string Base, string Body);
    private sealed record GitLabMergeRequestDeliveryRequest(string Title, string SourceBranch, string TargetBranch, string Description);
    private sealed record PullRequestUpdateRequest(string BranchName, string CommitSha, string ExternalId);
}
