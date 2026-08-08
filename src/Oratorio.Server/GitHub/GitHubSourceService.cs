using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;
using Oratorio.Server.Services;

namespace Oratorio.Server.GitHub;

public sealed class GitHubSourceService(
    OratorioDbContext db,
    IGitHubApiClient client,
    IOptionsMonitor<GitHubOptions> options,
    IGitHubCredentialResolver credentials,
    GitHubSyncCoordinator syncCoordinator,
    GitHubCommentCommandIntakeService commentCommands,
    TaskBoardPlacementService taskBoardPlacement,
    IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<GitHubSourceStatusResponse> GetStatusAsync(CancellationToken ct)
    {
        var current = options.CurrentValue;
        var status = credentials.Resolve(current);
        var lastSyncAt = await db.Items.AsNoTracking()
            .Where(x => x.Source == "github" && x.LastSourceSyncAt != null)
            .MaxAsync(x => x.LastSourceSyncAt, ct);
        return new GitHubSourceStatusResponse(
            current.Repositories.Length > 0 && status.HasAppAuthentication,
            status.HasAppAuthentication,
            current.WritesEnabled,
            status.CanWrite,
            status.HasWebhookSecret,
            string.IsNullOrWhiteSpace(current.Endpoint) ? "https://api.github.com" : current.Endpoint,
            current.Repositories,
            lastSyncAt);
    }

    public async Task<GitHubSyncResponse> SyncAsync(CancellationToken ct) =>
        await SyncAsync(null, ct);

    public async Task<GitHubSyncResponse> SyncAsync(string? repositoryFullName, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var configured = ResolveRepositories(repositoryFullName);
        var scanned = new List<string>();
        var errors = new List<GitHubSyncErrorDto>();
        var issues = 0;
        var prs = 0;
        var comments = 0;
        var skipped = 0;

        foreach (var repository in configured)
        {
            scanned.Add(repository.FullName);
            try
            {
                var result = await SyncRepositoryAsync(repository, now, null, GitHubSyncMode.Incremental, null, ct);
                issues += result.Issues;
                prs += result.PullRequests;
                comments += result.Comments;
                skipped += result.Skipped;
            }
            catch (GitHubAppAuthenticationRequiredException ex)
            {
                errors.Add(new GitHubSyncErrorDto(repository.FullName, ex.ErrorCode, ex.Message));
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
            {
                errors.Add(new GitHubSyncErrorDto(repository.FullName, "githubSyncFailed", ex.Message));
            }
        }

        return new GitHubSyncResponse(scanned, issues, prs, comments, skipped, errors, now);
    }

    public async Task<IResult> HandleWebhookAsync(HttpRequest request, CancellationToken ct)
    {
        var body = await ReadBodyAsync(request, ct);
        var eventName = request.Headers["X-GitHub-Event"].FirstOrDefault();
        if (string.Equals(eventName, "issue_comment", StringComparison.OrdinalIgnoreCase) &&
            !credentials.Resolve(options.CurrentValue).HasWebhookSecret)
        {
            return Results.Json(
                new
                {
                    error = "githubWebhookSecretRequired",
                    message = "GitHub issue_comment command intake requires a configured webhook secret."
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!ValidateSignature(body, request.Headers["X-Hub-Signature-256"].FirstOrDefault()))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var repository = TryReadRepository(body);
        if (string.IsNullOrWhiteSpace(repository))
        {
            return Results.BadRequest(new { error = "repositoryMissing", message = "GitHub webhook payload did not include a repository full_name." });
        }

        if (string.Equals(eventName, "issue_comment", StringComparison.OrdinalIgnoreCase))
        {
            var intake = await commentCommands.IntakeAsync(
                body,
                request.Headers["X-GitHub-Delivery"].FirstOrDefault(),
                ct);
            if (intake.Disposition == GitHubCommentCommandIntakeDisposition.Malformed)
            {
                return Results.BadRequest(new
                {
                    error = intake.ErrorCode ?? "githubWebhookPayloadInvalid",
                    message = intake.ErrorMessage ?? "GitHub issue_comment payload was invalid."
                });
            }

            if (intake.Command is not null &&
                intake.Disposition is GitHubCommentCommandIntakeDisposition.Queued or
                    GitHubCommentCommandIntakeDisposition.Rejected or
                    GitHubCommentCommandIntakeDisposition.Existing)
            {
                var commandResponse = new GitHubCommentCommandWebhookResponse(
                    intake.Command.CommandId,
                    intake.Command.Status,
                    intake.Duplicate,
                    intake.Command.ErrorCode);
                return intake.Disposition == GitHubCommentCommandIntakeDisposition.Queued
                    ? Results.Accepted(value: commandResponse)
                    : Results.Ok(commandResponse);
            }
        }

        var response = await syncCoordinator.EnqueueAsync(
            GitHubSyncTrigger.Webhook,
            GitHubSyncMode.Incremental,
            [repository],
            ct);
        return Results.Ok(response);
    }

    public async Task<GitHubRepositorySyncResult> SyncRepositoryAsync(
        GitHubRepositoryRef repository,
        DateTimeOffset now,
        DateTimeOffset? since,
        GitHubSyncMode mode,
        IGitHubRepositorySyncProgress? progress,
        CancellationToken ct)
    {
        var issueCount = 0;
        var prCount = 0;
        var commentCount = 0;
        var skipped = 0;

        if (progress is not null)
        {
            await progress.SetPhaseAsync(GitHubSyncRepositoryPhase.Fetching, ct);
        }

        var listState = mode == GitHubSyncMode.Full || since is not null
            ? GitHubListState.All
            : GitHubListState.Open;
        var sourceIssues = await client.ListIssuesAsync(repository, listState, since, ct);
        var issues = sourceIssues.Where(x => x.PullRequest is null).ToArray();
        var pullRequests = await ResolvePullRequestsAsync(repository, sourceIssues, since, mode, ct);
        skipped = sourceIssues.Count - issues.Length;

        if (progress is not null)
        {
            await progress.SetDiscoveredAsync(issues.Length, pullRequests.Count, skipped, ct);
            await progress.SetPhaseAsync(GitHubSyncRepositoryPhase.Importing, ct);
        }

        foreach (var issue in issues)
        {
            var item = new GitHubSyncItem(
                repository.FullName,
                $"issue:{repository.FullName}#{issue.Number}",
                "issue",
                issue.Number,
                issue.Title,
                issue.Body,
                issue.HtmlUrl,
                (issue.Assignees ?? []).FirstOrDefault()?.Login ?? issue.User?.Login,
                null,
                (issue.Labels ?? []).Select(x => x.Name).ToArray(),
                issue.CreatedAt,
                issue.UpdatedAt,
                false,
                null,
                ResolveIssueSourceState(issue),
                issue.ClosedAt,
                null,
                SerializePayload(new { issue }),
                []);

            await UpsertItemAsync(item, now, detailsHydrated: false, ct);
            issueCount++;
            if (progress is not null)
            {
                await progress.SetImportedAsync(issueCount, prCount, commentCount, skipped, ct);
            }
        }

        foreach (var pull in pullRequests)
        {
            await UpsertItemAsync(ToSyncItem(repository, pull), now, detailsHydrated: false, ct);
            prCount++;
            if (progress is not null)
            {
                await progress.SetImportedAsync(issueCount, prCount, commentCount, skipped, ct);
            }
        }

        return new GitHubRepositorySyncResult(issues.Length, pullRequests.Count, issueCount, prCount, commentCount, skipped);
    }

    public async Task<OratorioItem> SyncPullRequestAsync(
        GitHubRepositoryRef repository,
        int number,
        CancellationToken ct)
    {
        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, "Pull request number must be positive.");
        }

        var pull = await client.GetPullRequestAsync(repository, number, ct);
        if (pull.Number != number)
        {
            throw new InvalidOperationException(
                $"GitHub returned pull request #{pull.Number} while Oratorio requested #{number}.");
        }

        await UpsertItemAsync(ToSyncItem(repository, pull), clock.UtcNow, detailsHydrated: false, ct);
        return await db.Items.FirstAsync(
            x => x.Source == "github" && x.ExternalId == $"pr:{repository.FullName}#{number}",
            ct);
    }

    public async Task HydrateItemDetailsAsync(OratorioItem item, CancellationToken ct)
    {
        if (!string.Equals(item.Source, "github", StringComparison.OrdinalIgnoreCase))
        {
            throw OratorioApiException.Conflict(
                "unsupportedSource",
                "Source detail sync is only supported for GitHub items.",
                new Dictionary<string, object?> { ["source"] = item.Source });
        }

        if (item.SourceDetailsStatus == SourceDetailsStatus.Current)
        {
            return;
        }

        if (!TryResolveGitHubTarget(item, out var repository, out var number))
        {
            throw OratorioApiException.Conflict(
                "invalidGitHubTarget",
                "The item does not have a valid GitHub repository and number.",
                new Dictionary<string, object?> { ["itemId"] = item.ItemId, ["externalId"] = item.ExternalId });
        }

        var now = clock.UtcNow;
        try
        {
            var issueComments = await client.ListIssueCommentsAsync(repository, number, ct);
            IReadOnlyList<GitHubReview> reviews = [];
            IReadOnlyList<GitHubReviewComment> reviewComments = [];
            if (item.Kind == ItemKind.PullRequest)
            {
                reviews = await client.ListPullRequestReviewsAsync(repository, number, ct);
                reviewComments = await client.ListPullRequestReviewCommentsAsync(repository, number, ct);
            }

            var importedComments = (issueComments ?? []).Select(ToImportedIssueComment)
                .Concat(reviews.Where(x => !string.IsNullOrWhiteSpace(x.Body)).Select(ToImportedReview))
                .Concat(reviewComments.Select(ToImportedReviewComment))
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
                reviewState = item.Kind == ItemKind.PullRequest ? AggregateReviewState(reviews) : "none",
                issueComments = importedComments.Where(x => x.SourceCommentId.StartsWith("issue-comment:", StringComparison.Ordinal)),
                reviews = reviews.Select(x => new { x.Id, x.State, x.Body, x.HtmlUrl, user = x.User?.Login, x.SubmittedAt }),
                reviewComments = reviewComments.Select(x => new { x.Id, x.Body, x.HtmlUrl, x.Path, x.Line, x.OriginalLine, user = x.User?.Login, x.UpdatedAt })
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
                new Dictionary<string, object?> { ["itemId"] = item.ItemId, ["repository"] = item.Repository, ["externalId"] = item.ExternalId });
        }
    }

    private async Task<IReadOnlyList<GitHubPullRequest>> ResolvePullRequestsAsync(
        GitHubRepositoryRef repository,
        IReadOnlyList<GitHubIssue> sourceIssues,
        DateTimeOffset? since,
        GitHubSyncMode mode,
        CancellationToken ct)
    {
        if (since is null)
        {
            var state = mode == GitHubSyncMode.Full ? GitHubListState.All : GitHubListState.Open;
            return await client.ListPullRequestsAsync(repository, state, ct);
        }

        var pullRequests = new List<GitHubPullRequest>();
        foreach (var issue in sourceIssues.Where(x => x.PullRequest is not null))
        {
            pullRequests.Add(await client.GetPullRequestAsync(repository, issue.Number, ct));
        }

        return pullRequests;
    }

    private async Task<int> UpsertItemAsync(GitHubSyncItem sourceItem, DateTimeOffset now, bool detailsHydrated, CancellationToken ct)
    {
        var item = await db.Items
            .FirstOrDefaultAsync(x => x.Source == "github" && x.ExternalId == sourceItem.ExternalId, ct);
        var isNewItem = item is null;
        var sourceUpdated = item?.SourceUpdatedAt != sourceItem.UpdatedAt;
        var payloadHash = ComputePayloadHash(sourceItem.PayloadJson);
        var labelsJson = JsonSerializer.Serialize(sourceItem.Labels, JsonOptions);
        if (item is null)
        {
            item = new OratorioItem
            {
                Source = "github",
                ExternalId = sourceItem.ExternalId,
                Kind = sourceItem.Kind == "pullRequest" ? ItemKind.PullRequest : ItemKind.Issue,
                State = ItemState.Discovered,
                CheckState = sourceItem.Kind == "pullRequest" && !sourceItem.IsDraft ? CheckState.Attention : CheckState.NotConfigured,
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
                    x.Source == "github" &&
                    x.ExternalId == sourceItem.ExternalId &&
                    x.PayloadHash == payloadHash,
                ct);
        }

        var payloadChanged = isNewItem || matchingSnapshot is null;

        item.Title = sourceItem.Title;
        item.Description = sourceItem.Body;
        item.Repository = sourceItem.Repository;
        item.Assignee = sourceItem.Assignee;
        item.Branch = sourceItem.Branch;
        item.ExternalUrl = sourceItem.HtmlUrl;
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
                Source = "github",
                ExternalId = sourceItem.ExternalId,
                Repository = sourceItem.Repository,
                HeadSha = sourceItem.HeadSha,
                SourceUpdatedAt = sourceItem.UpdatedAt,
                PayloadJson = sourceItem.PayloadJson,
                PayloadHash = payloadHash,
                SyncedAt = now
            });
        }
        else
        {
            matchingSnapshot.Repository = sourceItem.Repository;
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
                "GitHub",
                isNewItem ? "GitHub synchronized" : "GitHub source updated",
                $"{sourceItem.Repository} #{sourceItem.Number} imported.",
                now);
        }

        var importedComments = await UpsertCommentsAsync(item, sourceItem.Comments, ct);
        await db.SaveChangesAsync(ct);
        return importedComments;
    }

    private async Task<int> UpsertCommentsAsync(OratorioItem item, IReadOnlyList<GitHubImportedComment> comments, CancellationToken ct)
    {
        var importedComments = 0;
        foreach (var sourceComment in comments)
        {
            var existing = await db.Comments.FirstOrDefaultAsync(
                x => x.Source == "github" && x.SourceCommentId == sourceComment.SourceCommentId,
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
                    Source = "github",
                    SourceCommentId = sourceComment.SourceCommentId,
                    ExternalUrl = sourceComment.HtmlUrl,
                    SourceUpdatedAt = sourceComment.UpdatedAt
                });
                importedComments++;
                continue;
            }

            existing.Item = item;
            existing.AuthorName = sourceComment.AuthorName;
            existing.Body = sourceComment.Body;
            existing.Purpose = CommentPurpose.SourceContext;
            existing.ExternalUrl = sourceComment.HtmlUrl;
            existing.SourceUpdatedAt = sourceComment.UpdatedAt;
        }

        return importedComments;
    }

    private async Task UpsertSourceSnapshotAsync(OratorioItem item, string payloadJson, DateTimeOffset now, CancellationToken ct)
    {
        var payloadHash = ComputePayloadHash(payloadJson);
        var matchingSnapshot = await db.SourceSnapshots.FirstOrDefaultAsync(
            x => x.ItemId == item.ItemId &&
                x.Source == "github" &&
                x.ExternalId == item.ExternalId &&
                x.PayloadHash == payloadHash,
            ct);
        if (matchingSnapshot is null)
        {
            db.SourceSnapshots.Add(new OratorioSourceSnapshot
            {
                Item = item,
                Source = "github",
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

    private void ApplySourceLifecycle(OratorioItem item, GitHubSyncItem sourceItem, DateTimeOffset now)
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
                    "GitHub",
                    sourceItem.SourceState == SourceState.Merged ? "Source PR merged" : "Source item closed",
                    $"{sourceItem.Repository} #{sourceItem.Number} was archived after GitHub reported it as {sourceItem.SourceState.ToString().ToLowerInvariant()}.",
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
            AddTimeline(item, TimelineEventKind.ItemReopened, "GitHub", "Source item reopened", $"{sourceItem.Repository} #{sourceItem.Number} is open again.", now);
        }
    }

    private static bool IsActive(OratorioItem item) =>
        item.State is ItemState.Dispatching or ItemState.Running;

    private static SourceState ResolveIssueSourceState(GitHubIssue issue) =>
        issue.State.Equals("open", StringComparison.OrdinalIgnoreCase)
            ? SourceState.Open
            : issue.State.Equals("closed", StringComparison.OrdinalIgnoreCase)
                ? SourceState.Closed
                : SourceState.Unknown;

    private static SourceState ResolvePullRequestSourceState(GitHubPullRequest pull)
    {
        if (pull.MergedAt is not null)
        {
            return SourceState.Merged;
        }

        return pull.State.Equals("open", StringComparison.OrdinalIgnoreCase)
            ? SourceState.Open
            : pull.State.Equals("closed", StringComparison.OrdinalIgnoreCase)
                ? SourceState.Closed
                : SourceState.Unknown;
    }

    private static GitHubSyncItem ToSyncItem(GitHubRepositoryRef repository, GitHubPullRequest pull) =>
        new(
            repository.FullName,
            $"pr:{repository.FullName}#{pull.Number}",
            "pullRequest",
            pull.Number,
            pull.Title,
            pull.Body,
            pull.HtmlUrl,
            (pull.Assignees ?? []).FirstOrDefault()?.Login ?? pull.User?.Login,
            pull.Head.Ref,
            (pull.Labels ?? []).Select(x => x.Name).ToArray(),
            pull.CreatedAt,
            pull.UpdatedAt,
            pull.Draft,
            pull.Head.Sha,
            ResolvePullRequestSourceState(pull),
            pull.ClosedAt,
            pull.MergedAt,
            SerializePayload(new { pullRequest = pull }),
            []);

    private IReadOnlyList<GitHubRepositoryRef> ResolveRepositories(string? repositoryFullName)
    {
        var values = string.IsNullOrWhiteSpace(repositoryFullName)
            ? options.CurrentValue.Repositories
            : [repositoryFullName];
        return values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => GitHubRepositoryRef.TryParse(x, out var repository) ? repository : null)
            .Where(x => x is not null)
            .Cast<GitHubRepositoryRef>()
            .ToArray();
    }

    private static bool TryResolveGitHubTarget(OratorioItem item, out GitHubRepositoryRef repository, out int number)
    {
        repository = new GitHubRepositoryRef("", "");
        number = 0;
        if (!GitHubRepositoryRef.TryParse(item.Repository ?? "", out repository))
        {
            return false;
        }

        var hash = item.ExternalId.LastIndexOf('#');
        return hash >= 0 && int.TryParse(item.ExternalId[(hash + 1)..], out number);
    }

    private bool ValidateSignature(string body, string? signatureHeader)
    {
        var secret = credentials.ResolveSecret(options.CurrentValue.WebhookSecret);
        if (string.IsNullOrWhiteSpace(secret))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(signatureHeader) || !signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expected = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signatureHeader));
    }

    private static async Task<string> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync(ct);
    }

    private static string? TryReadRepository(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("repository", out var repository) &&
                repository.TryGetProperty("full_name", out var fullName))
            {
                return fullName.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static GitHubImportedComment ToImportedIssueComment(GitHubComment comment) =>
        new($"issue-comment:{comment.Id}", comment.User?.Login ?? "github", comment.Body, comment.HtmlUrl, comment.CreatedAt, comment.UpdatedAt);

    private static GitHubImportedComment ToImportedReview(GitHubReview review) =>
        new($"review:{review.Id}", review.User?.Login ?? "github", $"Review {review.State}: {review.Body}", review.HtmlUrl, review.SubmittedAt, review.SubmittedAt);

    private static GitHubImportedComment ToImportedReviewComment(GitHubReviewComment comment)
    {
        var location = comment.Line ?? comment.OriginalLine;
        var prefix = location is null ? comment.Path : $"{comment.Path}:L{location}";
        return new($"review-comment:{comment.Id}", comment.User?.Login ?? "github", $"{prefix}\n\n{comment.Body}", comment.HtmlUrl, comment.CreatedAt, comment.UpdatedAt);
    }

    private static string AggregateReviewState(IReadOnlyList<GitHubReview> reviews)
    {
        var latestByUser = reviews
            .Where(x => x.User is not null)
            .GroupBy(x => x.User!.Login, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(review => review.SubmittedAt).First())
            .ToArray();
        if (latestByUser.Any(x => string.Equals(x.State, "CHANGES_REQUESTED", StringComparison.OrdinalIgnoreCase)))
        {
            return "changesRequested";
        }

        if (latestByUser.Any(x => string.Equals(x.State, "APPROVED", StringComparison.OrdinalIgnoreCase)))
        {
            return "approved";
        }

        return latestByUser.Length > 0 ? "pending" : "none";
    }

    private static string SerializePayload<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static string ComputePayloadHash(string payloadJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson))).ToLowerInvariant();

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
