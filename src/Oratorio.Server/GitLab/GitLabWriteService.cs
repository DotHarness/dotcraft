using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;
using Oratorio.Server.Services;
using Oratorio.Server.Sources;

namespace Oratorio.Server.GitLab;

public sealed class GitLabWriteService(
    OratorioDbContext db,
    IGitLabApiClient client,
    IOptionsMonitor<GitLabOptions> options,
    IGitLabCredentialResolver credentials,
    IClock clock)
{
    public const string CommitStatusName = "oratorio/review";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task RecordDecisionWritesAsync(string decisionId, CancellationToken ct)
    {
        var decision = await db.Decisions
            .Include(x => x.Item)
            .Include(x => x.Round)
            .FirstOrDefaultAsync(x => x.DecisionId == decisionId, ct);
        if (decision?.Item is null || decision.Round is null || decision.Item.Source != "gitlab")
        {
            return;
        }

        if (decision.Decision is not (DecisionType.Approve or DecisionType.RequestChanges or DecisionType.Reject))
        {
            return;
        }

        if (!TryResolveGitLabTarget(decision.Item, out var project, out var iid))
        {
            return;
        }

        var writes = CreateDecisionWriteLogs(decision, project, iid);
        foreach (var write in writes)
        {
            await TryExecuteAsync(write, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<string> RetryAsync(string writeId, CancellationToken ct)
    {
        var write = await db.SourceWriteLogs
            .Include(x => x.Item)
            .FirstOrDefaultAsync(x => x.WriteId == writeId, ct)
            ?? throw OratorioApiException.Conflict("sourceWriteNotFound", "The requested source write does not exist.", new Dictionary<string, object?> { ["writeId"] = writeId });
        if (write.Status != SourceWriteStatus.Failed)
        {
            throw OratorioApiException.Conflict("invalidTransition", "Only failed source writes can be retried.", new Dictionary<string, object?> { ["status"] = write.Status });
        }

        write.Status = SourceWriteStatus.Pending;
        write.ErrorCode = null;
        write.ErrorMessage = null;
        write.UpdatedAt = clock.UtcNow;
        AddTimeline(write, TimelineEventKind.SourceWriteQueued, "Retrying GitLab write", null);
        await TryExecuteAsync(write, ct);
        await ReconcileReviewDraftPublishAsync(write, ct);
        await db.SaveChangesAsync(ct);
        return write.ItemId;
    }

    public async Task ExecuteAsync(OratorioSourceWriteLog write, CancellationToken ct) =>
        await TryExecuteAsync(write, ct);

    private IReadOnlyList<OratorioSourceWriteLog> CreateDecisionWriteLogs(OratorioDecision decision, GitLabProjectRef project, int iid)
    {
        var item = decision.Item!;
        var round = decision.Round!;
        var now = clock.UtcNow;
        var intent = DecisionIntent(decision.Decision);
        var body = DecisionBody(decision);
        var logs = new List<OratorioSourceWriteLog>();

        if (item.Kind == ItemKind.PullRequest)
        {
            logs.Add(CreateWriteLog(
                item,
                round,
                decision,
                SourceWriteKind.MergeRequestNote,
                intent,
                project,
                iid,
                item.HeadSha,
                new Dictionary<string, object?> { ["body"] = body },
                now));

            var state = decision.Decision == DecisionType.Approve ? "success" : "failed";
            logs.Add(CreateWriteLog(
                item,
                round,
                decision,
                SourceWriteKind.CommitStatus,
                intent,
                project,
                iid,
                item.HeadSha,
                new Dictionary<string, object?>
                {
                    ["name"] = CommitStatusName,
                    ["state"] = state,
                    ["description"] = body
                },
                now));
        }
        else if (item.Kind == ItemKind.Issue)
        {
            logs.Add(CreateWriteLog(
                item,
                round,
                decision,
                SourceWriteKind.IssueComment,
                intent,
                project,
                iid,
                null,
                new Dictionary<string, object?> { ["body"] = body },
                now));
        }

        foreach (var log in logs)
        {
            db.SourceWriteLogs.Add(log);
            AddTimeline(log, TimelineEventKind.SourceWriteQueued, "GitLab write queued", null);
        }

        return logs;
    }

    private static OratorioSourceWriteLog CreateWriteLog(
        OratorioItem item,
        OratorioRound round,
        OratorioDecision decision,
        SourceWriteKind kind,
        string intent,
        GitLabProjectRef project,
        int iid,
        string? headSha,
        object request,
        DateTimeOffset now) =>
        new()
        {
            ItemId = item.ItemId,
            RoundId = round.RoundId,
            DecisionId = decision.DecisionId,
            Source = "gitlab",
            Kind = kind,
            Intent = intent,
            Status = SourceWriteStatus.Pending,
            Repository = project.ProjectPath,
            Number = iid,
            HeadSha = headSha,
            RequestJson = JsonSerializer.Serialize(request, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now
        };

    private async Task TryExecuteAsync(OratorioSourceWriteLog write, CancellationToken ct)
    {
        var current = options.CurrentValue;
        if (!current.Enabled)
        {
            MarkFailed(write, "gitlabDisabled", "GitLab provider is disabled.");
            return;
        }

        if (!current.WritesEnabled)
        {
            MarkFailed(write, "gitlabWritesDisabled", "GitLab writes are disabled.");
            return;
        }

        if (ImplementationDraftService.IsImplementationDeliveryIntent(write.Intent))
        {
            MarkFailed(write, "unsupportedSourceWriteRetry", "Implementation delivery writes must be retried through implementation delivery.");
            return;
        }

        if (!GitLabProjectRef.TryParse(write.Repository, out var project) || write.Number is null)
        {
            MarkFailed(write, "invalidGitLabTarget", "The source write target is incomplete.");
            return;
        }

        if (!IsConfiguredProject(project.ProjectPath, current))
        {
            MarkFailed(write, "gitlabProjectNotConfigured", "The target GitLab project is not configured for Oratorio writes.");
            return;
        }

        var status = credentials.ResolveProject(current, project);
        if (!status.HasToken)
        {
            MarkFailed(write, "gitlabProjectProfileTokenMissing", $"GitLab project profile token is missing for {project.ProjectPath}.");
            return;
        }

        if (write.Kind == SourceWriteKind.CommitStatus && string.IsNullOrWhiteSpace(write.HeadSha))
        {
            MarkFailed(write, "gitlabHeadShaRequired", "A GitLab commit status requires a head SHA.");
            return;
        }

        write.AttemptCount++;
        write.UpdatedAt = clock.UtcNow;
        try
        {
            var response = write.Kind switch
            {
                SourceWriteKind.IssueComment => await client.CreateIssueNoteAsync(project, write.Number.Value, ReadString(write.RequestJson, "body"), ct),
                SourceWriteKind.MergeRequestNote => await client.CreateMergeRequestNoteAsync(project, write.Number.Value, ReadString(write.RequestJson, "body"), ct),
                SourceWriteKind.MergeRequestDiscussion => await CreateMergeRequestDiscussionSetAsync(project, write, ct),
                SourceWriteKind.ResolveReviewThread => await client.ResolveMergeRequestDiscussionAsync(project, write.Number.Value, ReadString(write.RequestJson, "threadId"), ReadBool(write.RequestJson, "resolved"), ct),
                SourceWriteKind.CommitStatus => await client.SetCommitStatusAsync(
                    project,
                    write.HeadSha!,
                    ReadString(write.RequestJson, "state"),
                    ReadOptionalString(write.RequestJson, "name") ?? CommitStatusName,
                    ReadString(write.RequestJson, "description"),
                    ReadOptionalString(write.RequestJson, "targetUrl"),
                    ct),
                _ => throw new InvalidOperationException($"Unsupported GitLab source write kind: {write.Kind}")
            };

            write.Status = SourceWriteStatus.Succeeded;
            write.ExternalId = response.ExternalId;
            write.ExternalUrl = response.ExternalUrl;
            write.ResponseJson = response.ResponseJson;
            write.ErrorCode = null;
            write.ErrorMessage = null;
            write.CompletedAt = clock.UtcNow;
            write.UpdatedAt = write.CompletedAt.Value;
            AddTimeline(write, TimelineEventKind.SourceWriteSucceeded, "GitLab write succeeded", write.ExternalUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            MarkFailed(write, ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } ? "gitlabRateLimited" : "gitlabWriteFailed", ex.Message);
        }
    }

    private async Task<GitLabWriteResponse> CreateMergeRequestDiscussionSetAsync(GitLabProjectRef project, OratorioSourceWriteLog write, CancellationToken ct)
    {
        var summary = ReadString(write.RequestJson, "body");
        GitLabWriteResponse? first = null;
        if (!string.IsNullOrWhiteSpace(summary))
        {
            first = await client.CreateMergeRequestNoteAsync(project, write.Number!.Value, summary, ct);
        }

        var responses = new List<object>();
        if (first is not null)
        {
            responses.Add(new { first.ExternalId, first.ExternalUrl });
        }

        var findingComments = await ResolveAcceptedDraftCommentsAsync(write, ct);
        foreach (var comment in ReadDiscussionComments(write.RequestJson))
        {
            var response = await client.CreateMergeRequestDiscussionAsync(project, write.Number!.Value, comment.Body, comment.Position, ct);
            first ??= response;
            responses.Add(new { response.ExternalId, response.ExternalUrl });
            if (!string.IsNullOrWhiteSpace(comment.FindingId))
            {
                var match = findingComments.FirstOrDefault(c => c.DraftCommentId == comment.FindingId);
                if (match is not null)
                {
                    match.RemoteThreadId = response.ExternalId;
                }
            }
        }

        return first ?? new GitLabWriteResponse(Guid.NewGuid().ToString("n"), null, JsonSerializer.Serialize(new { responses }, JsonOptions));
    }

    private async Task<List<OratorioReviewDraftComment>> ResolveAcceptedDraftCommentsAsync(OratorioSourceWriteLog write, CancellationToken ct)
    {
        var draftId = db.ChangeTracker.Entries<OratorioReviewDraft>()
            .Select(entry => entry.Entity)
            .FirstOrDefault(draft => draft.SourceWriteId == write.WriteId)?.DraftId
            ?? await db.ReviewDrafts.Where(draft => draft.SourceWriteId == write.WriteId).Select(draft => draft.DraftId).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(draftId))
        {
            return [];
        }

        return await db.ReviewDraftComments
            .Where(c => c.DraftId == draftId && c.Status == ReviewDraftCommentStatus.Accepted)
            .ToListAsync(ct);
    }

    private async Task ReconcileReviewDraftPublishAsync(OratorioSourceWriteLog write, CancellationToken ct)
    {
        if (write.Intent != "reviewDraftPublish")
        {
            return;
        }

        var draft = await db.ReviewDrafts.FirstOrDefaultAsync(x => x.SourceWriteId == write.WriteId, ct);
        if (draft is null)
        {
            return;
        }

        var now = write.CompletedAt ?? clock.UtcNow;
        if (write.Status == SourceWriteStatus.Succeeded)
        {
            draft.Status = ReviewDraftStatus.Published;
            draft.PublishedAt = now;
        }
        else if (write.Status == SourceWriteStatus.Failed)
        {
            draft.Status = ReviewDraftStatus.PublishFailed;
        }

        draft.UpdatedAt = now;
    }

    private void MarkFailed(OratorioSourceWriteLog write, string code, string message)
    {
        write.Status = SourceWriteStatus.Failed;
        write.ErrorCode = code;
        write.ErrorMessage = message;
        write.CompletedAt = clock.UtcNow;
        write.UpdatedAt = write.CompletedAt.Value;
        AddTimeline(write, TimelineEventKind.SourceWriteFailed, "GitLab write failed", message);
    }

    private void AddTimeline(OratorioSourceWriteLog write, TimelineEventKind kind, string title, string? body)
    {
        db.TimelineEvents.Add(new OratorioTimelineEvent
        {
            ItemId = write.ItemId,
            RoundId = write.RoundId,
            Kind = kind,
            ActorKind = ActorKind.Source,
            ActorName = "GitLab",
            Title = title,
            Body = body,
            MetadataJson = JsonSerializer.Serialize(new { writeId = write.WriteId, write.Kind, write.Intent }, JsonOptions),
            CreatedAt = clock.UtcNow
        });
    }

    private static bool TryResolveGitLabTarget(OratorioItem item, out GitLabProjectRef project, out int iid)
    {
        project = new GitLabProjectRef("");
        iid = 0;
        if (!GitLabProjectRef.TryParse(item.Repository, out project))
        {
            if (!SourceProjectKey.TryParse(item.Repository, out var key) ||
                !string.Equals(key.Provider, "gitlab", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            project = new GitLabProjectRef(key.ProjectPath);
        }

        var separator = item.Kind == ItemKind.PullRequest ? '!' : '#';
        var index = item.ExternalId.LastIndexOf(separator);
        return index >= 0 && int.TryParse(item.ExternalId[(index + 1)..], out iid);
    }

    private static bool IsConfiguredProject(string projectPath, GitLabOptions current)
    {
        var normalized = SourceProjectKey.NormalizeGitLabProjectPath(projectPath);
        return !string.IsNullOrWhiteSpace(normalized) &&
            current.Projects
                .Select(SourceProjectKey.NormalizeGitLabProjectPath)
                .Any(project => string.Equals(project, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string DecisionIntent(DecisionType decision) =>
        decision switch
        {
            DecisionType.Approve => "approve",
            DecisionType.RequestChanges => "requestChanges",
            DecisionType.Reject => "reject",
            _ => decision.ToString()
        };

    private static string DecisionBody(OratorioDecision decision)
    {
        var body = string.IsNullOrWhiteSpace(decision.Body) ? "No operator feedback was provided." : decision.Body.Trim();
        return decision.Decision switch
        {
            DecisionType.Approve => body,
            DecisionType.RequestChanges => $"Oratorio requested changes.\n\n{body}",
            DecisionType.Reject => $"Oratorio rejected this item.\n\n{body}",
            _ => body
        };
    }

    private static string ReadString(string json, string propertyName) =>
        ReadOptionalString(json, propertyName) ?? throw new JsonException($"Missing required property '{propertyName}'.");

    private static bool ReadBool(string json, string propertyName)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(propertyName, out var value) &&
            value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
    }

    private static string? ReadOptionalString(string json, string propertyName)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;
    }

    private static IReadOnlyList<GitLabMergeRequestDiscussionCommentWrite> ReadDiscussionComments(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("comments", out var commentsElement) ||
            commentsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var comments = new List<GitLabMergeRequestDiscussionCommentWrite>();
        foreach (var comment in commentsElement.EnumerateArray())
        {
            var position = comment.GetProperty("position");
            comments.Add(new GitLabMergeRequestDiscussionCommentWrite(
                ReadRequiredString(comment, "body"),
                new GitLabMergeRequestPosition(
                    ReadRequiredString(position, "baseSha"),
                    ReadRequiredString(position, "headSha"),
                    ReadRequiredString(position, "startSha"),
                    ReadRequiredString(position, "oldPath"),
                    ReadRequiredString(position, "newPath"),
                    ReadOptionalInt(position, "oldLine"),
                    ReadOptionalInt(position, "newLine")),
                ReadOptionalString(comment, "findingId")));
        }

        return comments;
    }

    private static string ReadRequiredString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString() ?? throw new JsonException($"Missing required property '{propertyName}'.")
            : throw new JsonException($"Missing required property '{propertyName}'.");

    private static int? ReadOptionalInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetInt32()
            : null;

    private static string? ReadOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
