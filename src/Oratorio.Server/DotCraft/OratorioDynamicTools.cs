using System.ComponentModel;
using System.Text.Json;
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk;
using DotCraft.Sdk.DynamicTools;
using Microsoft.EntityFrameworkCore;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;
using Oratorio.Server.Services;

namespace Oratorio.Server.DotCraft;

public enum OratorioToolSurface
{
    Run,
    Discussion,
    AppBinding
}

public sealed record OratorioAppBindingGrantContext(
    string BindingId,
    long AuthorityRevision);

public sealed record OratorioToolInvocationContext(
    IServiceProvider Services,
    DynamicToolCallParams Call,
    OratorioToolSurface Surface,
    string? RunId = null,
    string? ExpectedThreadId = null,
    string? ExpectedDiscussionTurnId = null,
    OratorioAppBindingGrantContext? BindingGrant = null);

public sealed record OratorioDynamicToolSet(
    IReadOnlyList<RuntimeDynamicToolDeclaration> Declarations,
    IReadOnlySet<string> AllowedLocalNames,
    IReadOnlyList<string> QualifiedIds);

/// <summary>
/// The single source of truth for Oratorio tool identity, generated schemas, declarations,
/// surface allowlists, and registry dispatch.
/// </summary>
public sealed class OratorioDynamicToolCatalog
{
    public const string Namespace = "oratorio_run";
    public const string NamespaceDescription = "Submit typed Oratorio run artifacts and operate on the authorized Oratorio project board.";

    public const string SubmitDiscussionReplyName = "SubmitDiscussionReply";
    public const string SubmitDiscussionReplyId = $"{Namespace}.{SubmitDiscussionReplyName}";
    public const string ResolveReviewFindingName = "ResolveReviewFinding";
    public const string ResolveReviewFindingId = $"{Namespace}.{ResolveReviewFindingName}";
    public const string SubmitReviewDraftName = "SubmitReviewDraft";
    public const string SubmitReviewDraftId = $"{Namespace}.{SubmitReviewDraftName}";
    public const string SubmitImplementationDraftName = "SubmitImplementationDraft";
    public const string SubmitImplementationDraftId = $"{Namespace}.{SubmitImplementationDraftName}";
    public const string SubmitFollowUpDraftName = "SubmitFollowUpDraft";
    public const string SubmitFollowUpDraftId = $"{Namespace}.{SubmitFollowUpDraftName}";
    public const string ListBoardItemsName = "ListBoardItems";
    public const string GetBoardItemName = "GetBoardItem";
    public const string CreateBoardTaskName = "CreateBoardTask";
    public const string QueueReviewRoundName = "QueueReviewRound";

    private static readonly IReadOnlyDictionary<string, string> NamespaceDescriptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Namespace] = NamespaceDescription
        };

    private static readonly HashSet<RunStatus> TerminalRunStatuses =
    [
        RunStatus.Succeeded,
        RunStatus.Failed,
        RunStatus.Cancelled,
        RunStatus.TimedOut
    ];

    private readonly DynamicToolRegistry _registry;
    private readonly IReadOnlyDictionary<string, DynamicToolDescriptor> _descriptors;

    public OratorioDynamicToolCatalog(ILogger<OratorioDynamicToolCatalog> logger)
    {
        _registry = new DynamicToolRegistry(new DynamicToolRegistryOptions
        {
            ContextType = typeof(OratorioToolInvocationContext),
            UnknownToolCode = "UnsupportedTool",
            InvalidArgumentCode = "InvalidArguments",
            InvalidArgumentHint = "Use the generated input schema and remove undeclared fields.",
            InternalErrorCode = "AdapterToolCallFailed",
            InternalErrorMessage = "The Oratorio tool failed unexpectedly.",
            InternalErrorLogger = (exception, toolName) =>
                logger.LogError(exception, "Unexpected failure in Oratorio dynamic tool {ToolName}.", toolName)
        });
        _registry.Register(new OratorioDynamicToolFacade(), Namespace);
        _descriptors = _registry.ListDescriptors().ToDictionary(x => x.LocalName, StringComparer.Ordinal);
    }

    public IReadOnlyList<DynamicToolDescriptor> BoardDescriptors =>
        SelectDescriptors(
            ListBoardItemsName,
            GetBoardItemName,
            CreateBoardTaskName,
            QueueReviewRoundName);

    public IReadOnlyList<DynamicToolDescriptor> Descriptors =>
        _descriptors.Values.OrderBy(x => x.Order).ThenBy(x => x.LocalName, StringComparer.Ordinal).ToArray();

    public OratorioDynamicToolSet CreateDiscussionToolSet() =>
        CreateSet(SubmitDiscussionReplyName, ResolveReviewFindingName);

    public OratorioDynamicToolSet CreateRunToolSet(RunPurpose purpose, ItemKind itemKind, string? source)
    {
        var names = new List<string>
        {
            SubmitDiscussionReplyName,
            ResolveReviewFindingName,
            SubmitFollowUpDraftName
        };
        if (itemKind == ItemKind.PullRequest && source is "github" or "gitlab")
        {
            names.Add(SubmitReviewDraftName);
        }
        if (purpose == RunPurpose.Implementation &&
            (itemKind == ItemKind.LocalTask ||
             itemKind == ItemKind.Issue && source is "github" or "gitlab"))
        {
            names.Add(SubmitImplementationDraftName);
        }

        return CreateSet(names.ToArray());
    }

    public async Task<DynamicToolCallResult> InvokeAsync(
        DynamicToolCallParams call,
        OratorioToolInvocationContext context,
        IReadOnlySet<string> allowlist,
        CancellationToken ct)
    {
        if (context.Surface != OratorioToolSurface.AppBinding &&
            !string.Equals(call.Namespace, Namespace, StringComparison.Ordinal))
        {
            return Failure("UnsupportedTool", "Only Oratorio runtime dynamic tools declared for this surface are supported.");
        }
        if (!allowlist.Contains(call.Tool))
        {
            return Failure("UnsupportedTool", $"The Oratorio tool '{call.Tool}' is not allowed on this surface.");
        }
        if (!string.IsNullOrWhiteSpace(context.ExpectedThreadId) &&
            !string.Equals(context.ExpectedThreadId, call.ThreadId, StringComparison.Ordinal))
        {
            return Failure(
                context.Surface == OratorioToolSurface.Run ? "InvalidRunBinding" : "InvalidDiscussionTurnBinding",
                "The tool call is not bound to the active Oratorio thread.");
        }
        if (context.Surface == OratorioToolSurface.AppBinding && context.BindingGrant is null)
        {
            return Failure("InvalidAppBinding", "The tool call is not bound to an active Oratorio App Binding authority.");
        }

        var bindingFailure = await ValidateActiveBindingAsync(context, ct);
        if (bindingFailure is not null)
        {
            return bindingFailure;
        }

        DynamicToolOutcome outcome = await _registry.InvokeAsync(Namespace, call.Tool, call.Arguments, context, ct);
        if (outcome.Ok && outcome.Data is DynamicToolCallResult result)
        {
            return result;
        }
        if (outcome.Ok)
        {
            return Success("Oratorio tool completed.", outcome.Data);
        }

        return Failure(
            outcome.Code ?? "AdapterToolCallFailed",
            outcome.Message ?? "The Oratorio tool failed.",
            outcome.Field,
            outcome.Hint);
    }

    private async Task<DynamicToolCallResult?> ValidateActiveBindingAsync(OratorioToolInvocationContext context, CancellationToken ct)
    {
        if (context.RunId is not null)
        {
            var db = context.Services.GetRequiredService<OratorioDbContext>();
            var run = await db.Runs.AsNoTracking()
                .Where(x => x.RunId == context.RunId)
                .Select(x => new { x.Status, x.ThreadId, x.TurnId })
                .FirstOrDefaultAsync(ct);
            if (run is null || TerminalRunStatuses.Contains(run.Status))
            {
                return Failure("RunNotActive", "The Oratorio run is no longer active.");
            }
            if (!string.IsNullOrWhiteSpace(run.ThreadId) &&
                !string.Equals(run.ThreadId, context.Call.ThreadId, StringComparison.Ordinal))
            {
                return Failure("InvalidRunBinding", "The tool call thread does not match this Oratorio run.");
            }
            if (!string.IsNullOrWhiteSpace(run.TurnId) &&
                !string.Equals(run.TurnId, context.Call.TurnId, StringComparison.Ordinal))
            {
                return Failure("InvalidRunBinding", "The tool call turn does not match this Oratorio run.");
            }
        }

        if (context.ExpectedDiscussionTurnId is not null)
        {
            var db = context.Services.GetRequiredService<OratorioDbContext>();
            var turn = await db.DiscussionTurns.AsNoTracking()
                .Where(x => x.DiscussionTurnId == context.ExpectedDiscussionTurnId)
                .Select(x => new { x.Status, x.ThreadId, x.TurnId })
                .FirstOrDefaultAsync(ct);
            if (turn is null || turn.Status is not (DiscussionTurnStatus.Pending or DiscussionTurnStatus.Running))
            {
                return Failure("DiscussionTurnNotActive", "The Agent Discussion Turn is no longer active.");
            }
            if (!string.Equals(turn.ThreadId, context.Call.ThreadId, StringComparison.Ordinal) ||
                !string.IsNullOrWhiteSpace(turn.TurnId) &&
                !string.Equals(turn.TurnId, context.Call.TurnId, StringComparison.Ordinal))
            {
                return Failure("InvalidDiscussionTurnBinding", "The tool call is not bound to the current Agent Discussion Turn.");
            }
        }

        return null;
    }

    private OratorioDynamicToolSet CreateSet(params string[] localNames)
    {
        IReadOnlyList<DynamicToolDescriptor> descriptors = SelectDescriptors(localNames);
        IReadOnlyList<RuntimeDynamicToolDeclaration> declarations =
            RuntimeDynamicToolDeclarationBuilder.Build(
                descriptors,
                NamespaceDescriptions);
        return new(
            declarations,
            localNames.ToHashSet(StringComparer.Ordinal),
            descriptors.Select(x => x.QualifiedName).Order(StringComparer.Ordinal).ToArray());
    }

    private IReadOnlyList<DynamicToolDescriptor> SelectDescriptors(params string[] localNames) =>
        localNames.Select(name => _descriptors[name]).ToArray();

    private static DynamicToolCallResult Success(string message, object? structuredContent) =>
        new()
        {
            Success = true,
            ContentItems = [new DynamicToolContentItem { Type = "text", Text = message }],
            StructuredContent = structuredContent is null ? null : JsonSerializer.SerializeToElement(structuredContent, DynamicToolJson.Options)
        };

    private static DynamicToolCallResult Failure(string code, string message, string? field = null, string? hint = null)
    {
        var structured = new { error = new { code, message, field, hint } };
        return new DynamicToolCallResult
        {
            Success = false,
            ContentItems = [new DynamicToolContentItem { Type = "text", Text = message }],
            StructuredContent = JsonSerializer.SerializeToElement(structured, DynamicToolJson.Options),
            ErrorCode = code,
            ErrorMessage = message
        };
    }
}

internal sealed class OratorioDynamicToolFacade
{
    [DynamicTool(
        OratorioDynamicToolCatalog.SubmitDiscussionReplyName,
        "Submit the answer for the current Oratorio Agent Discussion Turn. Oratorio stores the reply as an internal discussion comment.",
        Order = 10)]
    public Task<DynamicToolCallResult> SubmitDiscussionReplyAsync(
        SubmitDiscussionReplyToolArgs args,
        OratorioToolInvocationContext context,
        CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var service = context.Services.GetRequiredService<DiscussionTurnService>();
            return await service.SubmitReplyForToolAsync(
                context.ExpectedDiscussionTurnId,
                new SubmitDiscussionReplyRequest(args.DiscussionTurnId, args.Body),
                context.Call,
                ct);
        });

    [DynamicTool(
        OratorioDynamicToolCatalog.ResolveReviewFindingName,
        "Resolve a published Oratorio review finding once it is fixed or agreed to be a non-issue. Use resolutionKind 'fixed' when the current code addresses it, or 'dismissed' when it was agreed not to action.",
        Order = 20)]
    public Task<DynamicToolCallResult> ResolveReviewFindingAsync(
        ResolveReviewFindingToolArgs args,
        OratorioToolInvocationContext context,
        CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var service = context.Services.GetRequiredService<ReviewFindingResolutionService>();
            var request = new ResolveReviewFindingRequest(
                args.FindingId,
                ToWire<ReviewFindingResolution>(args.ResolutionKind)!,
                args.Note);
            var response = context.Surface switch
            {
                OratorioToolSurface.Run when context.RunId is not null =>
                    await service.ResolveForRunAsync(context.RunId, request, ct),
                OratorioToolSurface.Discussion when context.ExpectedDiscussionTurnId is not null =>
                    await service.ResolveForDiscussionAsync(context.ExpectedDiscussionTurnId, request, ct),
                _ => throw new DynamicToolException("InvalidArguments", "ResolveReviewFinding requires an active run or discussion binding.")
            };
            return Success(
                $"Review finding {response.FindingId} resolved ({response.ResolutionKind}).",
                response);
        });

    [DynamicTool(
        OratorioDynamicToolCatalog.SubmitReviewDraftName,
        "Submit the structured review draft for this Oratorio review-analysis run. Use kind to choose suggestion or commentOnly for every inline comment.",
        Order = 30)]
    public Task<DynamicToolCallResult> SubmitReviewDraftAsync(
        SubmitReviewDraftToolArgs args,
        OratorioToolInvocationContext context,
        CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var request = new SubmitReviewDraftRequest(
                new ReviewDraftSummaryRequest(
                    args.Summary.MajorCount ?? 0,
                    args.Summary.MinorCount ?? 0,
                    args.Summary.SuggestionCount ?? 0,
                    args.Summary.Body),
                args.Comments?.Select(MapReviewComment).ToArray());
            string runId = RequireRun(context);
            var response = await context.Services.GetRequiredService<ReviewDraftService>()
                .SubmitForRunAsync(runId, request, ct);
            return Success(
                $"Review draft {response.DraftId} recorded with {response.AcceptedCount} accepted inline comment(s) and {response.WarningCount} warning(s).",
                response);
        });

    [DynamicTool(
        OratorioDynamicToolCatalog.SubmitImplementationDraftName,
        "Submit the structured implementation result for this Oratorio implementation run.",
        Order = 40)]
    public Task<DynamicToolCallResult> SubmitImplementationDraftAsync(
        SubmitImplementationDraftToolArgs args,
        OratorioToolInvocationContext context,
        CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var request = new SubmitImplementationDraftRequest(
                args.Summary,
                args.Tests,
                args.Risks,
                args.ChangedFiles,
                args.ProposedCommitMessage,
                args.ProposedPrTitle,
                args.ProposedPrBody);
            var response = await context.Services.GetRequiredService<ImplementationDraftService>()
                .SubmitForRunAsync(RequireRun(context), request, ct);
            return Success(
                $"Implementation draft {response.DraftId} recorded with {response.DeliveryPolicy} delivery policy.",
                response);
        });

    [DynamicTool(
        OratorioDynamicToolCatalog.SubmitFollowUpDraftName,
        "Submit zero or more structured follow-up task proposals discovered during this Oratorio run.",
        Order = 50)]
    public Task<DynamicToolCallResult> SubmitFollowUpDraftAsync(
        SubmitFollowUpDraftToolArgs args,
        OratorioToolInvocationContext context,
        CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var proposals = args.Proposals.Select(x => new FollowUpProposalRequest(
                x.Title,
                x.Body,
                x.Rationale,
                x.Repository,
                x.Assignee,
                x.Branch,
                x.Labels)).ToArray();
            var response = await context.Services.GetRequiredService<FollowUpDraftService>()
                .SubmitForRunAsync(RequireRun(context), new SubmitFollowUpDraftRequest(proposals), ct);
            return Success($"{response.AcceptedCount} follow-up draft proposal(s) recorded.", response);
        });

    [DynamicTool(
        OratorioDynamicToolCatalog.ListBoardItemsName,
        "List Oratorio board items. Use filters to narrow by state, source, repository, assignee, or search text.",
        Order = 60)]
    public Task<DynamicToolCallResult> ListBoardItemsAsync(
        ListBoardItemsToolArgs args,
        OratorioToolInvocationContext context,
        CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var result = await context.Services.GetRequiredService<OratorioService>().ListItemsAsync(
                args.State,
                args.Source,
                kind: null,
                args.Repository,
                args.Assignee,
                args.Q,
                sort: null,
                args.IncludeArchived,
                args.Limit,
                cursor: null,
                ct);
            return Success(
                $"Found {result.Items.Count} Oratorio board item(s).",
                new
                {
                    result.Items,
                    result.NextCursor,
                    context.BindingGrant!.BindingId,
                    context.BindingGrant.AuthorityRevision
                });
        });

    [DynamicTool(
        OratorioDynamicToolCatalog.GetBoardItemName,
        "Read one Oratorio board item by item id or task short id.",
        Order = 70)]
    public Task<DynamicToolCallResult> GetBoardItemAsync(
        GetBoardItemToolArgs args,
        OratorioToolInvocationContext context,
        CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var detail = await context.Services.GetRequiredService<OratorioService>()
                .GetTaskDetailAsync(args.ItemId, ct);
            return Success(
                $"Loaded Oratorio item '{detail.Item.Title}'.",
                new
                {
                    Detail = detail,
                    context.BindingGrant!.BindingId,
                    context.BindingGrant.AuthorityRevision
                });
        });

    [DynamicTool(
        OratorioDynamicToolCatalog.CreateBoardTaskName,
        "Create a local Oratorio task on the board. Use only when the user explicitly asks to change Oratorio state.",
        Order = 80)]
    public Task<DynamicToolCallResult> CreateBoardTaskAsync(
        CreateBoardTaskToolArgs args,
        OratorioToolInvocationContext context,
        CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var detail = await context.Services.GetRequiredService<OratorioService>().CreateLocalTaskAsync(
                new CreateLocalTaskRequest(
                    args.Title,
                    args.Description,
                    args.Repository,
                    args.Assignee,
                    args.Branch,
                    args.Labels),
                ct);
            return Success(
                $"Created Oratorio local task '{detail.Item.Title}'.",
                new
                {
                    Detail = detail,
                    context.BindingGrant!.BindingId,
                    context.BindingGrant.AuthorityRevision
                });
        });

    [DynamicTool(
        OratorioDynamicToolCatalog.QueueReviewRoundName,
        "Queue an Oratorio review-analysis round for an existing board item. Use only when the user explicitly asks to change Oratorio state.",
        Order = 90)]
    public Task<DynamicToolCallResult> QueueReviewRoundAsync(
        QueueReviewRoundToolArgs args,
        OratorioToolInvocationContext context,
        CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var service = context.Services.GetRequiredService<OratorioService>();
            var detail = await service.GetTaskDetailAsync(args.ItemId, ct);
            var result = await service.DispatchByIdAsync(
                detail.Item.ItemId,
                new DispatchRequest(
                    "appServer",
                    args.Note,
                    MockOutcome: null,
                    MockDurationSeconds: null,
                    WorkMode: "reviewAnalysis",
                    DeliveryPolicy: DeliveryPolicy.ManualDelivery),
                RunDispatchTrigger.AppBinding,
                ct);
            return Success(
                $"Queued an Oratorio review round for '{result.Item.Title}'.",
                new
                {
                    Detail = result,
                    context.BindingGrant!.BindingId,
                    context.BindingGrant.AuthorityRevision
                });
        });

    private static ReviewDraftCommentRequest MapReviewComment(ReviewDraftCommentToolArgs input)
    {
        ReviewDraftSuggestionRequest? suggestion = null;
        ReviewDraftCommentOnlyRequest? commentOnly = null;
        if (input.Kind == ReviewCommentKind.Suggestion)
        {
            if (string.IsNullOrWhiteSpace(input.OldText) || input.NewText is null)
            {
                throw new DynamicToolException(
                    "InvalidArguments",
                    "kind 'suggestion' requires non-empty oldText and a declared newText.",
                    "comments");
            }
            suggestion = new(input.OldText, input.NewText);
        }
        else
        {
            if (input.Line is null || input.Reason is null)
            {
                throw new DynamicToolException(
                    "InvalidArguments",
                    "kind 'commentOnly' requires line and reason.",
                    "comments");
            }
            commentOnly = new(
                input.Line.Value,
                ToWire(input.Side),
                input.StartLine,
                ToWire(input.StartSide),
                ToWire(input.Reason));
        }

        return new(
            ToWire(input.Severity),
            input.Title,
            input.Body,
            input.Path,
            suggestion,
            commentOnly);
    }

    private static string RequireRun(OratorioToolInvocationContext context) =>
        context.RunId ?? throw new DynamicToolException("InvalidArguments", "This tool requires an active Oratorio run.");

    private static async Task<DynamicToolCallResult> ExecuteAsync(Func<Task<DynamicToolCallResult>> action)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DynamicToolException)
        {
            throw;
        }
        catch (OratorioApiException ex)
        {
            string content = ex.Details is null
                ? ex.Message
                : $"{ex.Message}\nDetails: {JsonSerializer.Serialize(ex.Details, DynamicToolJson.Options)}";
            return new DynamicToolCallResult
            {
                Success = false,
                ContentItems = [new DynamicToolContentItem { Type = "text", Text = content }],
                StructuredContent = JsonSerializer.SerializeToElement(
                    new { error = new { code = ex.Code, message = ex.Message, details = ex.Details } },
                    DynamicToolJson.Options),
                ErrorCode = ex.Code,
                ErrorMessage = ex.Message
            };
        }
        catch (HttpRequestException)
        {
            const string message = "A source provider request failed while handling this Oratorio tool call. Retry after source connectivity and permissions are healthy.";
            return new DynamicToolCallResult
            {
                Success = false,
                ContentItems = [new DynamicToolContentItem { Type = "text", Text = message }],
                StructuredContent = JsonSerializer.SerializeToElement(
                    new { error = new { code = "upstreamSourceRequestFailed", message } },
                    DynamicToolJson.Options),
                ErrorCode = "upstreamSourceRequestFailed",
                ErrorMessage = message
            };
        }
    }

    private static DynamicToolCallResult Success(string message, object? structuredContent) =>
        new()
        {
            Success = true,
            ContentItems = [new DynamicToolContentItem { Type = "text", Text = message }],
            StructuredContent = structuredContent is null ? null : JsonSerializer.SerializeToElement(structuredContent, DynamicToolJson.Options)
        };

    private static string? ToWire<T>(T? value) where T : struct, Enum =>
        value is null ? null : JsonSerializer.Serialize(value.Value, DynamicToolJson.Options).Trim('"');
}

public enum ReviewCommentKind
{
    Suggestion,
    CommentOnly
}

public enum ReviewCommentSeverity
{
    Red,
    Yellow
}

public enum ReviewCommentSide
{
    Left,
    Right
}

public enum ReviewCommentOnlyReason
{
    NeedsHumanDecision,
    RequiresLargerChange,
    CannotAnchorSafely,
    InvestigateOnly,
    LeftSideOrDeletion
}

public enum ReviewFindingResolution
{
    Fixed,
    Dismissed
}

public sealed class SubmitDiscussionReplyToolArgs
{
    [Description("The active Agent Discussion Turn id.")]
    public required string DiscussionTurnId { get; init; }

    [Description("The answer to record as an internal discussion comment.")]
    public required string Body { get; init; }
}

public sealed class ResolveReviewFindingToolArgs
{
    [Description("The published review finding id (ReviewDraftComment id) to resolve.")]
    public required string FindingId { get; init; }

    [Description("fixed when the issue was addressed; dismissed when it was agreed not to action.")]
    public required ReviewFindingResolution ResolutionKind { get; init; }

    [Description("Optional short rationale for the resolution.")]
    public string? Note { get; init; }
}

public sealed class SubmitReviewDraftToolArgs
{
    public required ReviewDraftSummaryToolArgs Summary { get; init; }
    public IReadOnlyList<ReviewDraftCommentToolArgs>? Comments { get; init; }
}

public sealed class ReviewDraftSummaryToolArgs
{
    [SchemaMinimum(0)]
    public int? MajorCount { get; init; }

    [SchemaMinimum(0)]
    public int? MinorCount { get; init; }

    [SchemaMinimum(0)]
    public int? SuggestionCount { get; init; }

    public required string Body { get; init; }
}

public sealed class ReviewDraftCommentToolArgs
{
    [Description("Selects the authoritative comment branch. Fields from the other branch are ignored.")]
    public required ReviewCommentKind Kind { get; init; }

    public required string Title { get; init; }
    public required string Body { get; init; }

    [Description("Repository-relative file path.")]
    public required string Path { get; init; }

    public ReviewCommentSeverity? Severity { get; init; }

    [Description("Required when kind is suggestion: exact current text in the diff.")]
    public string? OldText { get; init; }

    [Description("Required when kind is suggestion; may be empty to delete oldText.")]
    public string? NewText { get; init; }

    [Description("Required when kind is commentOnly.")]
    [SchemaMinimum(1)]
    public int? Line { get; init; }

    public ReviewCommentSide? Side { get; init; }

    [SchemaMinimum(1)]
    public int? StartLine { get; init; }

    public ReviewCommentSide? StartSide { get; init; }

    [Description("Required when kind is commentOnly.")]
    public ReviewCommentOnlyReason? Reason { get; init; }
}

public sealed class SubmitImplementationDraftToolArgs
{
    public required string Summary { get; init; }
    public IReadOnlyList<string>? Tests { get; init; }
    public IReadOnlyList<string>? Risks { get; init; }
    public IReadOnlyList<string>? ChangedFiles { get; init; }
    public required string ProposedCommitMessage { get; init; }
    public required string ProposedPrTitle { get; init; }
    public required string ProposedPrBody { get; init; }
}

public sealed class SubmitFollowUpDraftToolArgs
{
    public required IReadOnlyList<FollowUpProposalToolArgs> Proposals { get; init; }
}

public sealed class FollowUpProposalToolArgs
{
    public required string Title { get; init; }
    public required string Body { get; init; }
    public string? Rationale { get; init; }
    public string? Repository { get; init; }
    public string? Assignee { get; init; }
    public string? Branch { get; init; }
    public IReadOnlyList<string>? Labels { get; init; }
}

public sealed class ListBoardItemsToolArgs
{
    [Description("Optional comma-separated Oratorio item states.")]
    public string? State { get; init; }

    [Description("Optional source filter such as local or github.")]
    public string? Source { get; init; }

    public string? Repository { get; init; }
    public string? Assignee { get; init; }

    [Description("Search text.")]
    public string? Q { get; init; }

    [SchemaMinimum(1)]
    [SchemaMaximum(100)]
    public int? Limit { get; init; }

    public bool? IncludeArchived { get; init; }
}

public sealed class GetBoardItemToolArgs
{
    [Description("Oratorio item id or task short id.")]
    public required string ItemId { get; init; }
}

public sealed class CreateBoardTaskToolArgs
{
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? Repository { get; init; }
    public string? Assignee { get; init; }
    public string? Branch { get; init; }
    public IReadOnlyList<string>? Labels { get; init; }
}

public sealed class QueueReviewRoundToolArgs
{
    [Description("Oratorio item id or task short id.")]
    public required string ItemId { get; init; }

    public string? Note { get; init; }
}
