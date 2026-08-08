using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;
using Oratorio.Server.Services;
using Oratorio.Server.Sources;

namespace Oratorio.Server.GitLab;

public sealed class GitLabSourceService(
    OratorioDbContext db,
    IGitLabApiClient client,
    IOptionsMonitor<GitLabOptions> options,
    IGitLabCredentialResolver credentials,
    GitLabSyncCoordinator syncCoordinator,
    TaskBoardPlacementService taskBoardPlacement,
    IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<IResult> HandleWebhookAsync(HttpRequest request, CancellationToken ct)
    {
        var body = await ReadBodyAsync(request, ct);
        var projectPath = TryReadProjectPath(body);
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return Results.BadRequest(new { error = "projectMissing", message = "GitLab webhook payload did not include a project path." });
        }

        if (!IsConfiguredProject(projectPath))
        {
            return Results.Ok(new { ignored = true, reason = "projectNotConfigured", project = projectPath });
        }

        if (!VerifyWebhook(request, body, new GitLabProjectRef(projectPath)))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var response = await syncCoordinator.EnqueueAsync(
            SourceSyncTrigger.Webhook,
            SourceSyncMode.Incremental,
            [projectPath],
            ct);
        return Results.Ok(response);
    }

    public async Task<GitLabProjectSyncResult> SyncProjectAsync(
        GitLabProjectRef project,
        DateTimeOffset now,
        DateTimeOffset? since,
        SourceSyncMode mode,
        IGitLabProjectSyncProgress? progress,
        CancellationToken ct)
    {
        if (progress is not null)
        {
            await progress.SetPhaseAsync(SourceSyncProjectPhase.Fetching, ct);
        }

        var resolvedProject = await client.GetProjectAsync(project, ct);
        var projectPath = SourceProjectKey.NormalizeGitLabProjectPath(resolvedProject.PathWithNamespace) ?? project.ProjectPath;
        var resolvedRef = new GitLabProjectRef(projectPath);
        var projectKey = SourceProjectKey.FromGitLabProject(projectPath, options.CurrentValue.Endpoint);
        var instance = projectKey.Instance;
        var listState = mode == SourceSyncMode.Full || since is not null
            ? GitLabListState.All
            : GitLabListState.Opened;
        var issues = await client.ListIssuesAsync(resolvedRef, listState, since, ct);
        var mergeRequests = await client.ListMergeRequestsAsync(resolvedRef, listState, since, ct);

        if (progress is not null)
        {
            await progress.SetDiscoveredAsync(issues.Count, mergeRequests.Count, 0, ct);
            await progress.SetPhaseAsync(SourceSyncProjectPhase.Importing, ct);
        }

        var issueCount = 0;
        var mergeRequestCount = 0;
        var commentCount = 0;
        foreach (var issue in issues)
        {
            var item = new GitLabSyncItem(
                projectPath,
                projectKey.Key,
                $"issue:{instance}/{projectPath}#{issue.Iid}",
                "issue",
                issue.Iid,
                issue.Title,
                issue.Description,
                issue.WebUrl,
                ResolveAssignee(issue.Assignees, issue.Author),
                null,
                null,
                (issue.Labels ?? []).ToArray(),
                issue.CreatedAt,
                issue.UpdatedAt,
                false,
                null,
                ResolveIssueSourceState(issue),
                issue.ClosedAt,
                null,
                SerializePayload(new { issue, project = resolvedProject }),
                []);

            await UpsertItemAsync(item, now, detailsHydrated: false, ct);
            issueCount++;
            if (progress is not null)
            {
                await progress.SetImportedAsync(issueCount, mergeRequestCount, commentCount, 0, ct);
            }
        }

        foreach (var mergeRequest in mergeRequests)
        {
            var item = new GitLabSyncItem(
                projectPath,
                projectKey.Key,
                $"mr:{instance}/{projectPath}!{mergeRequest.Iid}",
                "mergeRequest",
                mergeRequest.Iid,
                mergeRequest.Title,
                mergeRequest.Description,
                mergeRequest.WebUrl,
                ResolveAssignee(mergeRequest.Assignees, mergeRequest.Author),
                mergeRequest.SourceBranch,
                mergeRequest.TargetBranch,
                (mergeRequest.Labels ?? []).ToArray(),
                mergeRequest.CreatedAt,
                mergeRequest.UpdatedAt,
                mergeRequest.Draft,
                mergeRequest.Sha ?? mergeRequest.DiffRefs?.HeadSha,
                ResolveMergeRequestSourceState(mergeRequest),
                mergeRequest.ClosedAt,
                mergeRequest.MergedAt,
                SerializePayload(new { mergeRequest, project = resolvedProject }),
                []);

            await UpsertItemAsync(item, now, detailsHydrated: false, ct);
            mergeRequestCount++;
            if (progress is not null)
            {
                await progress.SetImportedAsync(issueCount, mergeRequestCount, commentCount, 0, ct);
            }
        }

        return new GitLabProjectSyncResult(issues.Count, mergeRequests.Count, issueCount, mergeRequestCount, commentCount, 0);
    }

    public async Task HydrateItemDetailsAsync(OratorioItem item, CancellationToken ct)
    {
        if (!string.Equals(item.Source, "gitlab", StringComparison.OrdinalIgnoreCase))
        {
            throw OratorioApiException.Conflict(
                "unsupportedSource",
                "Source detail sync is only supported for GitLab items.",
                new Dictionary<string, object?> { ["source"] = item.Source });
        }

        if (item.SourceDetailsStatus == SourceDetailsStatus.Current)
        {
            return;
        }

        if (!TryResolveGitLabTarget(item, out var project, out var iid))
        {
            throw OratorioApiException.Conflict(
                "invalidGitLabTarget",
                "The item does not have a valid GitLab project and IID.",
                new Dictionary<string, object?> { ["itemId"] = item.ItemId, ["externalId"] = item.ExternalId });
        }

        var now = clock.UtcNow;
        try
        {
            var issueNotes = item.Kind == ItemKind.Issue
                ? await client.ListIssueNotesAsync(project, iid, ct)
                : [];
            IReadOnlyList<GitLabNote> mergeRequestNotes = [];
            IReadOnlyList<GitLabDiscussion> discussions = [];
            GitLabMergeRequest? mergeRequest = null;
            if (item.Kind == ItemKind.PullRequest)
            {
                mergeRequest = await client.GetMergeRequestAsync(project, iid, ct);
                mergeRequestNotes = await client.ListMergeRequestNotesAsync(project, iid, ct);
                discussions = await client.ListMergeRequestDiscussionsAsync(project, iid, ct);
            }

            var projectKey = SourceProjectKey.FromGitLabProject(project.ProjectPath, options.CurrentValue.Endpoint).Key;
            var importedComments = issueNotes.Select(note => ToImportedIssueNote(projectKey, iid, note))
                .Concat(mergeRequestNotes.Select(note => ToImportedMergeRequestNote(projectKey, iid, note)))
                .Concat(discussions.SelectMany(discussion => discussion.Notes.Select(note => ToImportedDiscussionNote(projectKey, iid, discussion, note))))
                .ToArray();

            await UpsertCommentsAsync(item, importedComments, ct);
            var payloadJson = SerializePayload(new
            {
                item = new
                {
                    item.ExternalId,
                    item.Kind,
                    item.SourceUpdatedAt,
                    item.SourceState,
                    item.HeadSha
                },
                mergeRequest,
                issueNotes,
                mergeRequestNotes,
                discussions
            });
            await UpsertSourceSnapshotAsync(item, payloadJson, now, ct);
            item.SourceDetailsStatus = SourceDetailsStatus.Current;
            item.SourceDetailsHydratedAt = now;
            item.SourceDetailsErrorCode = null;
            item.SourceDetailsErrorMessage = null;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            item.SourceDetailsStatus = SourceDetailsStatus.Failed;
            item.SourceDetailsErrorCode = "sourceDetailsSyncFailed";
            item.SourceDetailsErrorMessage = ex.Message;
            await db.SaveChangesAsync(ct);
            throw OratorioApiException.Conflict(
                "sourceDetailsSyncFailed",
                ex.Message,
                new Dictionary<string, object?> { ["itemId"] = item.ItemId, ["project"] = item.Repository, ["externalId"] = item.ExternalId });
        }
    }

    private async Task<int> UpsertItemAsync(GitLabSyncItem sourceItem, DateTimeOffset now, bool detailsHydrated, CancellationToken ct)
    {
        var item = await db.Items
            .FirstOrDefaultAsync(x => x.Source == "gitlab" && x.ExternalId == sourceItem.ExternalId, ct);
        var isNewItem = item is null;
        var sourceUpdated = item?.SourceUpdatedAt != sourceItem.UpdatedAt;
        var payloadHash = ComputePayloadHash(sourceItem.PayloadJson);
        var labelsJson = JsonSerializer.Serialize(sourceItem.Labels, JsonOptions);
        if (item is null)
        {
            item = new OratorioItem
            {
                Source = "gitlab",
                ExternalId = sourceItem.ExternalId,
                Kind = sourceItem.Kind == "mergeRequest" ? ItemKind.PullRequest : ItemKind.Issue,
                State = ItemState.Discovered,
                CheckState = sourceItem.Kind == "mergeRequest" && !sourceItem.IsDraft ? CheckState.Attention : CheckState.NotConfigured,
                SourceDetailsStatus = detailsHydrated ? SourceDetailsStatus.Current : SourceDetailsStatus.Stale,
                SourceDetailsHydratedAt = detailsHydrated ? now : null,
                CreatedAt = now
            };
        }

        OratorioSourceSnapshot? matchingSnapshot = null;
        if (!isNewItem)
        {
            matchingSnapshot = await db.SourceSnapshots.FirstOrDefaultAsync(
                x => x.ItemId == item.ItemId &&
                    x.Source == "gitlab" &&
                    x.ExternalId == sourceItem.ExternalId &&
                    x.PayloadHash == payloadHash,
                ct);
        }

        var payloadChanged = isNewItem || matchingSnapshot is null;

        item.Title = sourceItem.Title;
        item.Description = sourceItem.Description;
        item.Repository = sourceItem.ProjectKey;
        item.Assignee = sourceItem.Assignee;
        item.Branch = sourceItem.Branch;
        item.ExternalUrl = sourceItem.WebUrl;
        item.LabelsJson = labelsJson;
        item.SourceUpdatedAt = sourceItem.UpdatedAt;
        item.IsDraft = sourceItem.IsDraft;
        item.HeadSha = sourceItem.HeadSha;
        item.SourceState = sourceItem.SourceState;
        item.SourceClosedAt = sourceItem.SourceClosedAt;
        item.SourceMergedAt = sourceItem.SourceMergedAt;
        if (detailsHydrated)
        {
            item.SourceDetailsStatus = SourceDetailsStatus.Current;
            item.SourceDetailsHydratedAt = now;
            item.SourceDetailsErrorCode = null;
            item.SourceDetailsErrorMessage = null;
        }
        else if (isNewItem || sourceUpdated)
        {
            item.SourceDetailsStatus = SourceDetailsStatus.Stale;
            item.SourceDetailsErrorCode = null;
            item.SourceDetailsErrorMessage = null;
        }

        if (payloadChanged)
        {
            item.UpdatedAt = now;
        }

        item.LastSourceSyncAt = now;
        ApplySourceLifecycle(item, sourceItem, now);

        if (isNewItem)
        {
            await taskBoardPlacement.AssignNewItemProjectionAsync(item, TaskStatusMapping.Project(item.State), ct);
            db.Items.Add(item);
        }

        if (matchingSnapshot is null)
        {
            db.SourceSnapshots.Add(new OratorioSourceSnapshot
            {
                Item = item,
                Source = "gitlab",
                ExternalId = sourceItem.ExternalId,
                Repository = sourceItem.ProjectKey,
                HeadSha = sourceItem.HeadSha,
                SourceUpdatedAt = sourceItem.UpdatedAt,
                PayloadJson = sourceItem.PayloadJson,
                PayloadHash = payloadHash,
                SyncedAt = now
            });
        }
        else
        {
            matchingSnapshot.Repository = sourceItem.ProjectKey;
            matchingSnapshot.HeadSha = sourceItem.HeadSha;
            matchingSnapshot.SourceUpdatedAt = sourceItem.UpdatedAt;
            matchingSnapshot.PayloadJson = sourceItem.PayloadJson;
            matchingSnapshot.SyncedAt = now;
        }

        if (payloadChanged)
        {
            AddTimeline(
                item,
                TimelineEventKind.SourceSynced,
                "GitLab",
                isNewItem ? "GitLab synchronized" : "GitLab source updated",
                $"{sourceItem.ProjectPath} !/{sourceItem.Iid} imported.",
                now);
        }

        var importedComments = await UpsertCommentsAsync(item, sourceItem.Comments, ct);
        await db.SaveChangesAsync(ct);
        return importedComments;
    }

    private async Task<int> UpsertCommentsAsync(OratorioItem item, IReadOnlyList<GitLabImportedComment> comments, CancellationToken ct)
    {
        var importedComments = 0;
        foreach (var sourceComment in comments)
        {
            var existing = await db.Comments.FirstOrDefaultAsync(
                x => x.Source == "gitlab" && x.SourceCommentId == sourceComment.SourceCommentId,
                ct);
            if (existing is null)
            {
                db.Comments.Add(new OratorioComment
                {
                    Item = item,
                    AuthorKind = AuthorKind.Source,
                    AuthorName = sourceComment.AuthorName,
                    Body = sourceComment.Body,
                    Visibility = CommentVisibility.Source,
                    Purpose = CommentPurpose.SourceContext,
                    CreatedAt = sourceComment.CreatedAt,
                    Source = "gitlab",
                    SourceCommentId = sourceComment.SourceCommentId,
                    ExternalUrl = sourceComment.Url,
                    SourceUpdatedAt = sourceComment.UpdatedAt
                });
                importedComments++;
                continue;
            }

            existing.Item = item;
            existing.AuthorName = sourceComment.AuthorName;
            existing.Body = sourceComment.Body;
            existing.Purpose = CommentPurpose.SourceContext;
            existing.ExternalUrl = sourceComment.Url;
            existing.SourceUpdatedAt = sourceComment.UpdatedAt;
        }

        return importedComments;
    }

    private async Task UpsertSourceSnapshotAsync(OratorioItem item, string payloadJson, DateTimeOffset now, CancellationToken ct)
    {
        var payloadHash = ComputePayloadHash(payloadJson);
        var matchingSnapshot = await db.SourceSnapshots.FirstOrDefaultAsync(
            x => x.ItemId == item.ItemId &&
                x.Source == "gitlab" &&
                x.ExternalId == item.ExternalId &&
                x.PayloadHash == payloadHash,
            ct);
        if (matchingSnapshot is null)
        {
            db.SourceSnapshots.Add(new OratorioSourceSnapshot
            {
                Item = item,
                Source = "gitlab",
                ExternalId = item.ExternalId,
                Repository = item.Repository,
                HeadSha = item.HeadSha,
                SourceUpdatedAt = item.SourceUpdatedAt,
                PayloadJson = payloadJson,
                PayloadHash = payloadHash,
                SyncedAt = now
            });
            return;
        }

        matchingSnapshot.Repository = item.Repository;
        matchingSnapshot.HeadSha = item.HeadSha;
        matchingSnapshot.SourceUpdatedAt = item.SourceUpdatedAt;
        matchingSnapshot.PayloadJson = payloadJson;
        matchingSnapshot.SyncedAt = now;
    }

    private void ApplySourceLifecycle(OratorioItem item, GitLabSyncItem sourceItem, DateTimeOffset now)
    {
        if (sourceItem.SourceState is SourceState.Closed or SourceState.Merged)
        {
            if (IsActive(item))
            {
                return;
            }

            var reason = sourceItem.SourceState == SourceState.Merged
                ? ArchiveReason.SourceMerged
                : ArchiveReason.SourceClosed;
            if (item.State != ItemState.Archived || item.ArchiveReason != reason)
            {
                item.State = ItemState.Archived;
                item.ArchiveReason = reason;
                item.UpdatedAt = now;
                AddTimeline(
                    item,
                    TimelineEventKind.ItemArchived,
                    "GitLab",
                    sourceItem.SourceState == SourceState.Merged ? "Source MR merged" : "Source item closed",
                    $"{sourceItem.ProjectPath} !/{sourceItem.Iid} was archived after GitLab reported it as {sourceItem.SourceState.ToString().ToLowerInvariant()}.",
                    now);
            }

            return;
        }

        if (sourceItem.SourceState == SourceState.Open &&
            item.State == ItemState.Archived &&
            item.ArchiveReason is ArchiveReason.SourceClosed or ArchiveReason.SourceMerged)
        {
            item.State = ItemState.Discovered;
            item.ArchiveReason = null;
            item.CheckState = item.Kind == ItemKind.PullRequest && !item.IsDraft ? CheckState.Attention : CheckState.NotConfigured;
            item.UpdatedAt = now;
            AddTimeline(item, TimelineEventKind.ItemReopened, "GitLab", "Source item reopened", $"{sourceItem.ProjectPath} !/{sourceItem.Iid} is open again.", now);
        }
    }

    private bool VerifyWebhook(HttpRequest request, string body, GitLabProjectRef project)
    {
        var current = options.CurrentValue;
        var signingToken = credentials.ResolveWebhookSigningToken(current, project);
        var signature = request.Headers["webhook-signature"].FirstOrDefault();
        var webhookId = request.Headers["webhook-id"].FirstOrDefault();
        var timestamp = request.Headers["webhook-timestamp"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(signature) ||
            !string.IsNullOrWhiteSpace(webhookId) ||
            !string.IsNullOrWhiteSpace(timestamp))
        {
            return !string.IsNullOrWhiteSpace(signingToken) &&
                ValidateStandardWebhookSignature(signingToken, webhookId, timestamp, signature, body, current.WebhookSigningToleranceSeconds);
        }

        var secret = credentials.ResolveWebhookSecret(current, project);
        if (!string.IsNullOrWhiteSpace(secret))
        {
            return ConstantTimeEquals(secret, request.Headers["X-Gitlab-Token"].FirstOrDefault());
        }

        return current.AllowLocalDevelopmentUnsafeWebhooks;
    }

    private bool ValidateStandardWebhookSignature(
        string signingToken,
        string? webhookId,
        string? timestamp,
        string? signatures,
        string body,
        int toleranceSeconds)
    {
        if (string.IsNullOrWhiteSpace(webhookId) ||
            string.IsNullOrWhiteSpace(timestamp) ||
            string.IsNullOrWhiteSpace(signatures))
        {
            return false;
        }

        if (!long.TryParse(timestamp, out var unixSeconds))
        {
            return false;
        }

        var requestTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (Math.Abs((clock.UtcNow - requestTime).TotalSeconds) > Math.Clamp(toleranceSeconds, 1, 3600))
        {
            return false;
        }

        byte[] key;
        try
        {
            key = signingToken.StartsWith("whsec_", StringComparison.Ordinal)
                ? Convert.FromBase64String(signingToken["whsec_".Length..])
                : Encoding.UTF8.GetBytes(signingToken);
        }
        catch (FormatException)
        {
            return false;
        }

        var message = $"{webhookId}.{timestamp}.{body}";
        var expected = "v1," + Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(message)));
        return signatures
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(candidate => ConstantTimeEquals(expected, candidate));
    }

    private bool IsConfiguredProject(string projectPath)
    {
        var normalized = SourceProjectKey.NormalizeGitLabProjectPath(projectPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return options.CurrentValue.Projects
            .Select(SourceProjectKey.NormalizeGitLabProjectPath)
            .Any(candidate => string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryResolveGitLabTarget(OratorioItem item, out GitLabProjectRef project, out int iid)
    {
        project = new GitLabProjectRef("");
        iid = 0;
        var repository = item.Repository ?? "";
        if (SourceProjectKey.TryParse(repository, out var key) &&
            string.Equals(key.Provider, "gitlab", StringComparison.OrdinalIgnoreCase))
        {
            project = new GitLabProjectRef(key.ProjectPath);
        }
        else if (!GitLabProjectRef.TryParse(repository, out project))
        {
            return false;
        }

        var marker = item.Kind == ItemKind.PullRequest
            ? item.ExternalId.LastIndexOf('!')
            : item.ExternalId.LastIndexOf('#');
        return marker >= 0 && int.TryParse(item.ExternalId[(marker + 1)..], out iid);
    }

    private static string? TryReadProjectPath(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("project", out var project) &&
                project.ValueKind == JsonValueKind.Object &&
                project.TryGetProperty("path_with_namespace", out var pathWithNamespace))
            {
                return SourceProjectKey.NormalizeGitLabProjectPath(pathWithNamespace.GetString());
            }

            if (root.TryGetProperty("repository", out var repository) &&
                repository.ValueKind == JsonValueKind.Object &&
                repository.TryGetProperty("homepage", out var homepage))
            {
                return SourceProjectKey.NormalizeGitLabProjectPath(homepage.GetString());
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static async Task<string> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync(ct);
    }

    private static bool IsActive(OratorioItem item) =>
        item.State is ItemState.Dispatching or ItemState.Running;

    private static SourceState ResolveIssueSourceState(GitLabIssue issue) =>
        issue.State.Equals("opened", StringComparison.OrdinalIgnoreCase)
            ? SourceState.Open
            : issue.State.Equals("closed", StringComparison.OrdinalIgnoreCase)
                ? SourceState.Closed
                : SourceState.Unknown;

    private static SourceState ResolveMergeRequestSourceState(GitLabMergeRequest mergeRequest)
    {
        if (mergeRequest.MergedAt is not null ||
            mergeRequest.State.Equals("merged", StringComparison.OrdinalIgnoreCase))
        {
            return SourceState.Merged;
        }

        return mergeRequest.State.Equals("opened", StringComparison.OrdinalIgnoreCase)
            ? SourceState.Open
            : mergeRequest.State.Equals("closed", StringComparison.OrdinalIgnoreCase)
                ? SourceState.Closed
                : SourceState.Unknown;
    }

    private static string? ResolveAssignee(IReadOnlyList<GitLabUser>? assignees, GitLabUser? fallback) =>
        DisplayName((assignees ?? []).FirstOrDefault()) ?? DisplayName(fallback);

    private static string? DisplayName(GitLabUser? user) =>
        string.IsNullOrWhiteSpace(user?.Username) ? user?.Name : user.Username;

    private static GitLabImportedComment ToImportedIssueNote(string projectKey, int iid, GitLabNote note) =>
        new($"issue-note:{projectKey}:#{iid}:{note.Id}", DisplayName(note.Author) ?? "gitlab", note.Body, note.Url, note.CreatedAt, note.UpdatedAt);

    private static GitLabImportedComment ToImportedMergeRequestNote(string projectKey, int iid, GitLabNote note) =>
        new($"mr-note:{projectKey}:!{iid}:{note.Id}", DisplayName(note.Author) ?? "gitlab", note.Body, note.Url, note.CreatedAt, note.UpdatedAt);

    private static GitLabImportedComment ToImportedDiscussionNote(string projectKey, int iid, GitLabDiscussion discussion, GitLabDiscussionNote note)
    {
        var prefix = note.Position is null
            ? null
            : $"{note.Position.NewPath ?? note.Position.OldPath}:{note.Position.NewLine ?? note.Position.OldLine}";
        var body = string.IsNullOrWhiteSpace(prefix) ? note.Body : $"{prefix}\n\n{note.Body}";
        return new($"mr-discussion-note:{projectKey}:!{iid}:{discussion.Id}:{note.Id}", DisplayName(note.Author) ?? "gitlab", body, note.Url, note.CreatedAt, note.UpdatedAt);
    }

    private static string SerializePayload<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static string ComputePayloadHash(string payloadJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson))).ToLowerInvariant();

    private static bool ConstantTimeEquals(string? expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length &&
            CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static void AddTimeline(OratorioItem item, TimelineEventKind kind, string actorName, string title, string? body, DateTimeOffset now)
    {
        item.TimelineEvents.Add(new OratorioTimelineEvent
        {
            Item = item,
            Kind = kind,
            ActorKind = ActorKind.Source,
            ActorName = actorName,
            Title = title,
            Body = body,
            CreatedAt = now
        });
    }
}
