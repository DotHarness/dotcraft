using System.Text.Json;
using System.Text.Json.Serialization;
using DotCraft.Protocol.AppServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Realtime;

namespace DotCraft.Oratorio.Services;

public sealed class DiscussionTurnService(
    OratorioDbContext db,
    OratorioService oratorioService,
    ConversationThreadLocator threads,
    GitHubWriteService gitHubWrites,
    IClock clock,
    IOptionsMonitor<DotCraftOptions> options,
    BoardEventHub boardEvents)
{
    public const string GitHubOrigin = "github";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<ItemDetailResponse> CreateByItemIdAsync(string itemId, DiscussionTurnRequest request, CancellationToken ct)
    {
        ValidateRequired(request.Body, "body");
        var item = await db.Items.FirstOrDefaultAsync(x => x.ItemId == itemId, ct)
            ?? throw OratorioApiException.Conflict("itemNotFound", "The requested item does not exist.", new Dictionary<string, object?> { ["itemId"] = itemId });

        if (item.State == ItemState.Archived)
        {
            throw OratorioApiException.Conflict(
                "discussionTurnNotAllowed",
                "Cannot ask the agent while the Task is archived.",
                new Dictionary<string, object?> { ["state"] = item.State });
        }

        if (await HasPendingDeliveryAsync(item.ItemId, ct))
        {
            throw OratorioApiException.Conflict(
                "activeDiscussionTurnExists",
                "Cannot ask another question while an Agent Discussion Turn is waiting to be delivered.",
                new Dictionary<string, object?> { ["itemId"] = item.ItemId });
        }

        var thread = await threads.FindDiscussionThreadAsync(item.ItemId, ct);
        var hasActiveRun = await db.Runs.AnyAsync(x =>
            x.ItemId == item.ItemId &&
            x.RunnerKind == "appServer" &&
            (x.Status == RunStatus.Queued || x.Status == RunStatus.Dispatching || x.Status == RunStatus.Running),
            ct);
        if (thread is null && !hasActiveRun)
        {
            throw OratorioApiException.Conflict(
                "noCompatibleDiscussionThread",
                "Ask agent requires a conversation thread with the Oratorio discussion reply tool.",
                new Dictionary<string, object?> { ["itemId"] = item.ItemId });
        }

        var round = request.RoundNumber is not null
            ? await FindRoundByNumberAsync(item.ItemId, request.RoundNumber.Value, ct)
            : await FindCurrentRoundAsync(item, ct);
        var now = clock.UtcNow;
        var question = new OratorioComment
        {
            ItemId = item.ItemId,
            RoundId = round?.RoundId,
            AuthorKind = AuthorKind.Operator,
            AuthorName = "operator",
            Body = request.Body.Trim(),
            Visibility = CommentVisibility.Internal,
            Purpose = CommentPurpose.DiscussionQuestion,
            CreatedAt = now
        };
        AddTurn(item, round, question, thread?.ThreadId, EmptyToNull(request.ModelId), now);
        AddTimeline(item, round?.RoundId, TimelineEventKind.CommentAdded, ActorKind.Operator, "operator", "Agent question asked", question.Body, now);
        await db.SaveChangesAsync(ct);
        boardEvents.PublishTaskUpdated(item, now);
        return await oratorioService.GetItemDetailByIdAsync(item.ItemId, ct);
    }

    public async Task<bool> CreateFromGitHubQuestionAsync(OratorioGitHubCommentCommand command, OratorioItem item, CancellationToken ct)
    {
        if (await HasPendingDeliveryAsync(item.ItemId, ct))
        {
            return false;
        }

        var thread = await threads.FindDiscussionThreadAsync(item.ItemId, ct);
        var round = await FindCurrentRoundAsync(item, ct);
        var now = clock.UtcNow;
        var question = await db.Comments.FirstOrDefaultAsync(
            x => x.Source == GitHubOrigin && x.SourceCommentId == command.SourceCommentId,
            ct);
        if (question is null)
        {
            question = new OratorioComment
            {
                ItemId = item.ItemId,
                AuthorKind = AuthorKind.Source,
                AuthorName = command.ActorLogin,
                Visibility = CommentVisibility.Source,
                CreatedAt = now,
                Source = GitHubOrigin,
                SourceCommentId = command.SourceCommentId,
                ExternalUrl = command.CommentUrl
            };
        }

        question.RoundId = round?.RoundId;
        question.Body = command.Question ?? "";
        question.Purpose = CommentPurpose.DiscussionQuestion;
        var turn = AddTurn(item, round, question, thread?.ThreadId, null, now);
        turn.OriginProvider = GitHubOrigin;
        turn.OriginAuthorLogin = command.ActorLogin;
        turn.OriginUrl = command.CommentUrl;
        turn.OriginCommandId = command.CommandId;
        turn.OriginCommentKind = command.CommentKind;
        turn.OriginCommentId = command.CommentId;
        turn.OriginThreadRootCommentId = command.ThreadRootCommentId;
        turn.OriginPath = command.Path;
        turn.OriginLine = command.Line;

        command.Status = GitHubCommentCommandStatus.Dispatched;
        command.DiscussionTurnId = turn.DiscussionTurnId;
        command.NextAttemptAt = null;
        command.ErrorCode = null;
        command.ErrorMessage = null;
        command.UpdatedAt = now;
        command.CompletedAt = now;

        item.UpdatedAt = now;
        AddTimeline(item, round?.RoundId, TimelineEventKind.CommentAdded, ActorKind.Source, command.ActorLogin, "Agent question asked on GitHub", question.Body, now);
        await db.SaveChangesAsync(ct);
        boardEvents.PublishTaskUpdated(item, now);
        return true;
    }

    public async Task<DynamicToolCallResult> SubmitReplyForToolAsync(
        SubmitDiscussionReplyRequest request,
        DynamicToolCallParams call,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return ToolFailure("InvalidArguments", "body is required.");
        }

        var turn = await db.DiscussionTurns
            .Include(x => x.Item)
            .FirstOrDefaultAsync(x => x.DiscussionTurnId == request.DiscussionTurnId, ct);
        if (turn is null)
        {
            return ToolFailure("DiscussionTurnNotFound", "The requested Agent Discussion Turn does not exist.");
        }

        if (turn.Status != DiscussionTurnStatus.Running || !string.IsNullOrWhiteSpace(turn.ReplyCommentId))
        {
            return ToolFailure("DiscussionTurnAlreadyAnswered", "The Agent Discussion Turn is not waiting for a reply.");
        }

        if (!string.Equals(turn.ThreadId, call.ThreadId, StringComparison.Ordinal))
        {
            return ToolFailure("InvalidDiscussionTurnBinding", "The tool call thread does not match this Agent Discussion Turn.");
        }

        var now = clock.UtcNow;
        var reply = new OratorioComment
        {
            ItemId = turn.ItemId,
            RoundId = turn.RoundId,
            AuthorKind = AuthorKind.Agent,
            AuthorName = "DotCraft",
            Body = request.Body.Trim(),
            Visibility = CommentVisibility.Internal,
            Purpose = CommentPurpose.DiscussionReply,
            CreatedAt = now
        };

        db.Comments.Add(reply);
        turn.TurnId ??= call.TurnId;
        turn.ReplyCommentId = reply.CommentId;
        turn.Status = DiscussionTurnStatus.Succeeded;
        turn.ErrorCode = null;
        turn.ErrorMessage = null;
        Complete(turn, now, succeeded: true);
        if (turn.Item is not null)
        {
            turn.Item.UpdatedAt = now;
            AddTimeline(turn.Item, turn.RoundId, TimelineEventKind.CommentAdded, ActorKind.Agent, "DotCraft", "Agent answered discussion question", reply.Body, now);
        }

        await db.SaveChangesAsync(ct);
        if (turn.OriginProvider == GitHubOrigin)
        {
            await gitHubWrites.RecordDiscussionReplyAsync(turn, reply.Body, ct);
        }

        if (turn.Item is not null)
        {
            boardEvents.PublishTaskUpdated(turn.Item, now);
        }

        var response = new SubmitDiscussionReplyResponse(turn.DiscussionTurnId, reply.CommentId);
        return new DynamicToolCallResult
        {
            Success = true,
            ContentItems = [new DynamicToolContentItem { Type = "text", Text = "Discussion reply recorded." }],
            StructuredContent = JsonSerializer.SerializeToElement(response, JsonOptions)
        };
    }

    public async Task FailAsync(string discussionTurnId, string code, string message, CancellationToken ct)
    {
        var status = await db.DiscussionTurns.AsNoTracking()
            .Where(x => x.DiscussionTurnId == discussionTurnId)
            .Select(x => (DiscussionTurnStatus?)x.Status)
            .FirstOrDefaultAsync(ct);
        if (status is null or DiscussionTurnStatus.Succeeded or DiscussionTurnStatus.Failed)
        {
            return;
        }

        var turn = await db.DiscussionTurns.Include(x => x.Item).FirstAsync(x => x.DiscussionTurnId == discussionTurnId, ct);

        var now = clock.UtcNow;
        turn.Status = DiscussionTurnStatus.Failed;
        turn.ErrorCode = code;
        turn.ErrorMessage = message;
        Complete(turn, now, succeeded: false);
        if (turn.Item is not null)
        {
            turn.Item.UpdatedAt = now;
            AddTimeline(turn.Item, turn.RoundId, TimelineEventKind.RunFailed, ActorKind.System, "Oratorio", "Agent discussion failed", message, now);
        }

        await db.SaveChangesAsync(ct);
        if (turn.Item is not null)
        {
            boardEvents.PublishTaskUpdated(turn.Item, now);
        }
    }

    private OratorioDiscussionTurn AddTurn(
        OratorioItem item,
        OratorioRound? round,
        OratorioComment question,
        string? threadId,
        string? modelId,
        DateTimeOffset now)
    {
        var turn = new OratorioDiscussionTurn
        {
            ItemId = item.ItemId,
            RoundId = round?.RoundId,
            QuestionCommentId = question.CommentId,
            ThreadId = threadId,
            ModelId = modelId,
            Status = DiscussionTurnStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
        if (db.Entry(question).State == EntityState.Detached)
        {
            db.Comments.Add(question);
        }

        db.DiscussionTurns.Add(turn);
        item.UpdatedAt = now;
        return turn;
    }

    private void Complete(OratorioDiscussionTurn turn, DateTimeOffset now, bool succeeded)
    {
        turn.UpdatedAt = now;
        turn.CompletedAt = now;
        if (turn.WorktreeStatus is WorktreeStatus.Ready or WorktreeStatus.Preparing)
        {
            turn.WorktreeStatus = turn.WorktreePath is null ? WorktreeStatus.Failed : WorktreeStatus.CleanupPending;
            turn.WorktreeCleanupAfterAt = now + (succeeded
                ? options.CurrentValue.SucceededWorktreeRetention
                : options.CurrentValue.FailedWorktreeRetention);
        }
    }

    private Task<bool> HasPendingDeliveryAsync(string itemId, CancellationToken ct) =>
        db.DiscussionTurns.AnyAsync(x => x.ItemId == itemId && x.Status == DiscussionTurnStatus.Pending, ct);

    private static DynamicToolCallResult ToolFailure(string code, string message) =>
        new()
        {
            Success = false,
            ErrorCode = code,
            ErrorMessage = message
        };

    private async Task<OratorioRound?> FindCurrentRoundAsync(OratorioItem item, CancellationToken ct)
    {
        if (item.CurrentRound == 0)
        {
            return null;
        }

        return await db.Rounds.FirstOrDefaultAsync(x => x.ItemId == item.ItemId && x.RoundNumber == item.CurrentRound, ct);
    }

    private async Task<OratorioRound> FindRoundByNumberAsync(string itemId, int roundNumber, CancellationToken ct) =>
        await db.Rounds.FirstOrDefaultAsync(x => x.ItemId == itemId && x.RoundNumber == roundNumber, ct)
        ?? throw OratorioApiException.Validation("The requested round does not exist.", new Dictionary<string, object?> { ["roundNumber"] = roundNumber });

    private static void AddTimeline(
        OratorioItem item,
        string? roundId,
        TimelineEventKind kind,
        ActorKind actorKind,
        string actorName,
        string title,
        string? body,
        DateTimeOffset createdAt)
    {
        item.TimelineEvents.Add(new OratorioTimelineEvent
        {
            ItemId = item.ItemId,
            RoundId = roundId,
            Kind = kind,
            ActorKind = actorKind,
            ActorName = actorName,
            Title = title,
            Body = body,
            CreatedAt = createdAt
        });
    }

    private static void ValidateRequired(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw OratorioApiException.Validation($"{field} is required.", new Dictionary<string, object?> { ["field"] = field });
        }
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
