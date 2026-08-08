using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;
using Oratorio.Server.DotCraft;
using Oratorio.Server.Realtime;

namespace Oratorio.Server.Services;

public sealed class DiscussionTurnService(
    IServiceScopeFactory scopeFactory,
    OratorioDbContext db,
    OratorioService oratorioService,
    IClock clock,
    IDotCraftAppServerClientFactory clientFactory,
    IOptionsMonitor<DotCraftOptions> options,
    BoardEventHub boardEvents,
    OratorioDynamicToolCatalog dynamicTools,
    ILogger<DiscussionTurnService> logger)
{
    private static readonly DiscussionTurnStatus[] ActiveStatuses = [DiscussionTurnStatus.Pending, DiscussionTurnStatus.Running];
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

        if (item.State is ItemState.Archived or ItemState.Dispatching or ItemState.Running)
        {
            throw OratorioApiException.Conflict(
                "discussionTurnNotAllowed",
                "Cannot ask the agent while the Task is archived or has an active run.",
                new Dictionary<string, object?> { ["state"] = item.State });
        }

        var hasActiveDiscussion = await db.DiscussionTurns.AnyAsync(x => x.ItemId == item.ItemId && ActiveStatuses.Contains(x.Status), ct);
        if (hasActiveDiscussion)
        {
            throw OratorioApiException.Conflict(
                "activeDiscussionTurnExists",
                "Cannot ask another question while an Agent Discussion Turn is active.",
                new Dictionary<string, object?> { ["itemId"] = item.ItemId });
        }

        var baseRun = await FindCompatibleBaseRunAsync(item.ItemId, ct)
            ?? throw OratorioApiException.Conflict(
                "noCompatibleDiscussionThread",
                "Ask agent requires a completed AppServer thread with the Oratorio discussion reply tool.",
                new Dictionary<string, object?> { ["itemId"] = item.ItemId });

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
        var turn = new OratorioDiscussionTurn
        {
            ItemId = item.ItemId,
            RoundId = round?.RoundId,
            QuestionCommentId = question.CommentId,
            BaseRunId = baseRun.RunId,
            ThreadId = baseRun.ThreadId!,
            ModelId = EmptyToNull(request.ModelId),
            AppServerEndpoint = baseRun.AppServerEndpoint,
            Status = DiscussionTurnStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Comments.Add(question);
        db.DiscussionTurns.Add(turn);
        item.UpdatedAt = now;
        AddTimeline(item, round, TimelineEventKind.CommentAdded, ActorKind.Operator, "operator", "Agent question asked", question.Body, now);
        await db.SaveChangesAsync(ct);
        boardEvents.PublishTaskUpdated(item, now);
        return await oratorioService.GetItemDetailByIdAsync(item.ItemId, ct);
    }

    public async Task<int> ProcessPendingAsync(CancellationToken ct)
    {
        var pendingIds = await db.DiscussionTurns.AsNoTracking()
            .Where(x => x.Status == DiscussionTurnStatus.Pending)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.DiscussionTurnId)
            .Take(1)
            .ToListAsync(ct);

        foreach (var turnId in pendingIds)
        {
            await ProcessPendingTurnAsync(turnId, ct);
        }

        return pendingIds.Count;
    }

    public async Task<DynamicToolCallResult> SubmitReplyForToolAsync(
        string? expectedDiscussionTurnId,
        SubmitDiscussionReplyRequest request,
        DynamicToolCallParams call,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(expectedDiscussionTurnId) &&
            !string.Equals(expectedDiscussionTurnId, request.DiscussionTurnId, StringComparison.Ordinal))
        {
            return ToolFailure("InvalidDiscussionTurnBinding", "The tool call is not bound to the current Agent Discussion Turn.");
        }

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

        if (turn.Status is not (DiscussionTurnStatus.Pending or DiscussionTurnStatus.Running) || !string.IsNullOrWhiteSpace(turn.ReplyCommentId))
        {
            return ToolFailure("DiscussionTurnAlreadyAnswered", "The Agent Discussion Turn is not waiting for a reply.");
        }

        if (!string.Equals(turn.ThreadId, call.ThreadId, StringComparison.Ordinal))
        {
            return ToolFailure("InvalidDiscussionTurnBinding", "The tool call thread does not match this Agent Discussion Turn.");
        }

        if (string.IsNullOrWhiteSpace(turn.TurnId) ||
            string.IsNullOrWhiteSpace(call.TurnId) ||
            !string.Equals(turn.TurnId, call.TurnId, StringComparison.Ordinal))
        {
            return ToolFailure("InvalidDiscussionTurnBinding", "The tool call turn does not match this Agent Discussion Turn.");
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
        turn.ReplyCommentId = reply.CommentId;
        turn.Status = DiscussionTurnStatus.Succeeded;
        turn.ErrorCode = null;
        turn.ErrorMessage = null;
        turn.UpdatedAt = now;
        turn.CompletedAt = now;
        if (turn.Item is not null)
        {
            turn.Item.UpdatedAt = now;
            AddTimeline(turn.Item, null, TimelineEventKind.CommentAdded, ActorKind.Agent, "DotCraft", "Agent answered discussion question", reply.Body, now, turn.RoundId);
        }

        await db.SaveChangesAsync(ct);
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

    private static DynamicToolCallResult ToolFailure(string code, string message) =>
        new()
        {
            Success = false,
            ErrorCode = code,
            ErrorMessage = message
        };

    private async Task ProcessPendingTurnAsync(string discussionTurnId, CancellationToken outerCt)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        timeout.CancelAfter(options.CurrentValue.RunTimeout);
        var ct = timeout.Token;

        try
        {
            var turn = await LoadTurnAsync(discussionTurnId, ct);
            if (turn.Status != DiscussionTurnStatus.Pending)
            {
                return;
            }

            await MarkRunningAsync(turn, ct);
            var endpoint = turn.AppServerEndpoint ?? turn.BaseRun?.AppServerEndpoint;
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                await FailTurnAsync(discussionTurnId, "discussionEndpointMissing", "The base AppServer endpoint is missing.", CancellationToken.None);
                return;
            }

            await using var client = await clientFactory.ConnectAsync(endpoint!, ct);
            if (!client.SupportsDynamicToolRebind)
            {
                await FailTurnAsync(discussionTurnId, "dynamicToolRebindUnsupported", "The AppServer cannot rebind Oratorio discussion tools for this thread.", CancellationToken.None);
                return;
            }
            if (!client.SupportsRuntimeAdditionalContext)
            {
                await FailTurnAsync(discussionTurnId, "runtimeAdditionalContextUnsupported", "The DotCraft AppServer does not support runtime additional context required by Oratorio discussion turns.", CancellationToken.None);
                return;
            }

            var toolSet = dynamicTools.CreateDiscussionToolSet();
            client.SetDynamicToolHandler(async (call, handlerCt) =>
            {
                using var scope = scopeFactory.CreateScope();
                return await dynamicTools.InvokeAsync(
                    call,
                    new OratorioToolInvocationContext(
                        scope.ServiceProvider,
                        call,
                        OratorioToolSurface.Discussion,
                        ExpectedThreadId: turn.ThreadId,
                        ExpectedDiscussionTurnId: discussionTurnId),
                    toolSet.AllowedLocalNames,
                    handlerCt);
            });
            var openFindings = await LoadOpenFindingsAsync(turn.ItemId, ct);
            var prompt = BuildPrompt(turn, openFindings);
            await client.ResumeThreadAsync(
                turn.ThreadId,
                toolSet.Declarations,
                prompt.RuntimeAdditionalContext,
                ct);
            await client.SubscribeThreadAsync(turn.ThreadId, ct);

            var input = new[]
            {
                new TurnInputPartDto("text", prompt.Prompt, null, null, null, null, null, null)
            };
            var appTurnId = await client.StartTurnAsync(turn.ThreadId, input, turn.ModelId, ct);
            if (string.IsNullOrWhiteSpace(appTurnId))
            {
                await FailTurnAsync(discussionTurnId, "discussionTurnStartFailed", "The AppServer did not return a turn id for the Agent Discussion Turn.", CancellationToken.None);
                return;
            }

            await MarkTurnStartedAsync(discussionTurnId, appTurnId!, prompt.ContextJson, ct);
            await ConsumeDiscussionNotificationsAsync(discussionTurnId, client, ct);
        }
        catch (OperationCanceledException) when (!outerCt.IsCancellationRequested)
        {
            await FailTurnAsync(discussionTurnId, "discussionTurnTimedOut", "The Agent Discussion Turn timed out.", CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Agent Discussion Turn {DiscussionTurnId} failed.", discussionTurnId);
            await FailTurnAsync(discussionTurnId, "discussionTurnFailed", ex.Message, CancellationToken.None);
        }
    }

    private async Task ConsumeDiscussionNotificationsAsync(string discussionTurnId, IDotCraftAppServerClient client, CancellationToken ct)
    {
        await foreach (var runEvent in client.ReadEventsAsync(ct))
        {
            if (runEvent.Type is "initialized" or "thread/started" or "thread/resumed" or "turn/started")
            {
                continue;
            }

            if (runEvent.Type == "turn/completed")
            {
                var status = await db.DiscussionTurns.AsNoTracking()
                    .Where(x => x.DiscussionTurnId == discussionTurnId)
                    .Select(x => new { x.Status, x.ReplyCommentId })
                    .FirstAsync(ct);
                if (status.Status == DiscussionTurnStatus.Succeeded && !string.IsNullOrWhiteSpace(status.ReplyCommentId))
                {
                    return;
                }

                await FailTurnAsync(discussionTurnId, "discussionReplyMissing", "The agent completed without submitting a discussion reply.", ct);
                return;
            }

            if (runEvent is DotCraftRunEvent<TurnNotification> { Type: "turn/failed" } failed)
            {
                await FailTurnAsync(
                    discussionTurnId,
                    "discussionTurnFailed",
                    failed.Params.Error ?? failed.Params.Turn.Error ?? "The Agent Discussion Turn failed.",
                    ct);
                return;
            }

            if (runEvent.Type == "turn/cancelled")
            {
                await FailTurnAsync(discussionTurnId, "discussionTurnCancelled", "The Agent Discussion Turn was cancelled.", ct);
                return;
            }
        }

        await FailTurnAsync(discussionTurnId, "discussionTurnDisconnected", "The AppServer disconnected before the Agent Discussion Turn completed.", ct);
    }

    private async Task<OratorioDiscussionTurn> LoadTurnAsync(string discussionTurnId, CancellationToken ct) =>
        await db.DiscussionTurns
            .Include(x => x.Item)
            .Include(x => x.BaseRun)
            .Include(x => x.QuestionComment)
            .FirstOrDefaultAsync(x => x.DiscussionTurnId == discussionTurnId, ct)
        ?? throw OratorioApiException.Conflict("discussionTurnNotFound", "The requested Agent Discussion Turn does not exist.", new Dictionary<string, object?> { ["discussionTurnId"] = discussionTurnId });

    private async Task MarkRunningAsync(OratorioDiscussionTurn turn, CancellationToken ct)
    {
        var now = clock.UtcNow;
        turn.Status = DiscussionTurnStatus.Running;
        turn.StartedAt = now;
        turn.UpdatedAt = now;
        turn.ErrorCode = null;
        turn.ErrorMessage = null;
        if (turn.Item is not null)
        {
            turn.Item.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        if (turn.Item is not null)
        {
            boardEvents.PublishTaskUpdated(turn.Item, now);
        }
    }

    private async Task MarkTurnStartedAsync(string discussionTurnId, string turnId, string contextJson, CancellationToken ct)
    {
        var turn = await db.DiscussionTurns.Include(x => x.Item).FirstAsync(x => x.DiscussionTurnId == discussionTurnId, ct);
        if (turn.Status != DiscussionTurnStatus.Running)
        {
            return;
        }

        var now = clock.UtcNow;
        turn.TurnId = turnId;
        turn.PromptContextJson = contextJson;
        turn.UpdatedAt = now;
        if (turn.Item is not null)
        {
            turn.Item.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        if (turn.Item is not null)
        {
            boardEvents.PublishTaskUpdated(turn.Item, now);
        }
    }

    private async Task FailTurnAsync(string discussionTurnId, string code, string message, CancellationToken ct)
    {
        var turn = await db.DiscussionTurns.Include(x => x.Item).FirstOrDefaultAsync(x => x.DiscussionTurnId == discussionTurnId, ct);
        if (turn is null || turn.Status == DiscussionTurnStatus.Succeeded)
        {
            return;
        }

        var now = clock.UtcNow;
        turn.Status = DiscussionTurnStatus.Failed;
        turn.ErrorCode = code;
        turn.ErrorMessage = message;
        turn.UpdatedAt = now;
        turn.CompletedAt = now;
        if (turn.Item is not null)
        {
            turn.Item.UpdatedAt = now;
            AddTimeline(turn.Item, null, TimelineEventKind.RunFailed, ActorKind.System, "Oratorio", "Agent discussion failed", message, now, turn.RoundId);
        }

        await db.SaveChangesAsync(ct);
        if (turn.Item is not null)
        {
            boardEvents.PublishTaskUpdated(turn.Item, now);
        }
    }

    private DiscussionPrompt BuildPrompt(OratorioDiscussionTurn turn, IReadOnlyList<FindingPromptInfo> openFindings)
    {
        var context = new
        {
            promptMode = "discussion",
            runtimeContextVersion = AppServerPromptBuilder.RuntimeContextVersion,
            requiredDynamicTools = dynamicTools.CreateDiscussionToolSet().QualifiedIds,
            discussionTurn = new
            {
                turn.DiscussionTurnId,
                turn.ItemId,
                turn.RoundId,
                turn.QuestionCommentId,
                turn.BaseRunId,
                turn.ThreadId
            },
            openFindings = openFindings.Select(f => new { f.FindingId, f.Severity, f.Title, f.Path, f.Line }),
            item = turn.Item is null
                ? null
                : new
                {
                    turn.Item.ItemId,
                    turn.Item.Source,
                    turn.Item.ExternalId,
                    turn.Item.Kind,
                    turn.Item.Title,
                    Body = turn.Item.Description,
                    turn.Item.Repository,
                    turn.Item.Branch,
                    turn.Item.HeadSha
                },
            baseRun = turn.BaseRun is null
                ? null
                : new
                {
                    turn.BaseRun.RunId,
                    turn.BaseRun.RoundId,
                    turn.BaseRun.Purpose,
                    turn.BaseRun.Summary,
                    turn.BaseRun.CompletedAt
            },
            question = turn.QuestionComment?.Body
        };
        var contextJson = JsonSerializer.Serialize(context, JsonOptions);
        var runtimeAdditionalContext = AppServerPromptBuilder.BuildThreadRuntimeAdditionalContext();

        var prompt = new StringBuilder();
        prompt.AppendLine("You are answering an Oratorio Agent Discussion Turn in an existing DotCraft thread.");
        prompt.AppendLine();
        prompt.AppendLine("Task:");
        prompt.AppendLine($"- Title: {turn.Item?.Title ?? "unknown"}");
        prompt.AppendLine($"- Source: {turn.Item?.Source ?? "unknown"} {turn.Item?.ExternalId ?? "unknown"}");
        prompt.AppendLine($"- Repository: {turn.Item?.Repository ?? "none"}");
        prompt.AppendLine($"- Branch: {turn.Item?.Branch ?? "none"}");
        prompt.AppendLine();
        prompt.AppendLine("Most recent completed run:");
        prompt.AppendLine($"- Run: {turn.BaseRunId}");
        prompt.AppendLine($"- Summary: {EmptyToNone(turn.BaseRun?.Summary)}");
        prompt.AppendLine();
        prompt.AppendLine("Discussion turn:");
        prompt.AppendLine($"- discussionTurnId `{turn.DiscussionTurnId}`");
        prompt.AppendLine();
        prompt.AppendLine("Operator question:");
        prompt.AppendLine(turn.QuestionComment?.Body.Trim() ?? "");
        prompt.AppendLine();
        if (openFindings.Count > 0)
        {
            prompt.AppendLine("Open published review findings (resolvable):");
            foreach (var finding in openFindings)
            {
                prompt.AppendLine($"- [{finding.FindingId}] {finding.Severity} {finding.Title} ({finding.Path}:{finding.Line})");
            }

            prompt.AppendLine();
        }

        prompt.AppendLine("Instructions:");
        prompt.AppendLine("- Answer only the operator question shown above.");
        prompt.AppendLine("- Use the Oratorio discussion runtime context for reply submission and boundaries.");
        return new DiscussionPrompt(contextJson, prompt.ToString(), runtimeAdditionalContext);
    }

    private async Task<IReadOnlyList<FindingPromptInfo>> LoadOpenFindingsAsync(string itemId, CancellationToken ct) =>
        await db.ReviewDraftComments.AsNoTracking()
            .Where(c =>
                c.Draft!.ItemId == itemId &&
                c.Draft.Status == ReviewDraftStatus.Published &&
                c.Status == ReviewDraftCommentStatus.Accepted &&
                c.ResolutionState == ReviewFindingResolutionState.Open)
            .OrderBy(c => c.Draft!.CreatedAt)
            .ThenBy(c => c.Path)
            .ThenBy(c => c.Line)
            .Select(c => new FindingPromptInfo(c.DraftCommentId, c.Severity, c.Title, c.Path, c.Line))
            .ToListAsync(ct);

    private sealed record FindingPromptInfo(string FindingId, string Severity, string Title, string Path, int Line);

    private async Task<OratorioRun?> FindCompatibleBaseRunAsync(string itemId, CancellationToken ct)
    {
        var candidates = await db.Runs.AsNoTracking()
            .Where(x =>
                x.ItemId == itemId &&
                x.RunnerKind == "appServer" &&
                x.Status == RunStatus.Succeeded &&
                x.ThreadId != null &&
                x.AppServerEndpoint != null &&
                x.PromptContextJson != null)
            .OrderByDescending(x => x.CompletedAt ?? x.StartedAt ?? DateTimeOffset.MinValue)
            .Take(20)
            .ToListAsync(ct);

        return candidates.FirstOrDefault(x => IsCompatiblePromptContext(x.PromptContextJson!));
    }

    private static bool IsCompatiblePromptContext(string contextJson)
    {
        try
        {
            using var document = JsonDocument.Parse(contextJson);
            var root = document.RootElement;
            if (!TryGetProperty(root, "promptMode", "PromptMode", out var promptMode) ||
                !string.Equals(promptMode.GetString(), "compact", StringComparison.Ordinal))
            {
                return false;
            }

            if (!TryGetProperty(root, "runtimeContextVersion", "RuntimeContextVersion", out var runtimeContextVersion) ||
                runtimeContextVersion.ValueKind != JsonValueKind.String ||
                !string.Equals(runtimeContextVersion.GetString(), AppServerPromptBuilder.RuntimeContextVersion, StringComparison.Ordinal))
            {
                return false;
            }

            return TryGetProperty(root, "requiredDynamicTools", "RequiredDynamicTools", out var toolsElement) &&
                toolsElement.ValueKind == JsonValueKind.Array &&
                toolsElement.EnumerateArray().Select(x => x.GetString()).Contains(OratorioDynamicToolCatalog.SubmitDiscussionReplyId, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

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
        OratorioRound? round,
        TimelineEventKind kind,
        ActorKind actorKind,
        string actorName,
        string title,
        string? body,
        DateTimeOffset createdAt,
        string? roundIdOverride = null)
    {
        item.TimelineEvents.Add(new OratorioTimelineEvent
        {
            ItemId = item.ItemId,
            RoundId = roundIdOverride ?? round?.RoundId,
            Kind = kind,
            ActorKind = actorKind,
            ActorName = actorName,
            Title = title,
            Body = body,
            CreatedAt = createdAt
        });
    }

    private static bool TryGetProperty(JsonElement element, string camelName, string pascalName, out JsonElement value) =>
        element.TryGetProperty(camelName, out value) || element.TryGetProperty(pascalName, out value);

    private static void ValidateRequired(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw OratorioApiException.Validation($"{field} is required.", new Dictionary<string, object?> { ["field"] = field });
        }
    }

    private static string EmptyToNone(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record DiscussionPrompt(
        string ContextJson,
        string Prompt,
        IReadOnlyDictionary<string, AppServerRuntimeAdditionalContextEntry> RuntimeAdditionalContext);
}
