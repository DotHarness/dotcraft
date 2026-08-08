using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;

namespace Oratorio.Server.DotCraft;

public sealed class AppServerPromptBuilder(OratorioDbContext db)
{
    public const string RuntimeContextVersion = "oratorio-runtime-context-v1";
    public const string ThreadStateWorkspaceMode = "base";

    private const string RunContractInstructions = """
        Oratorio owns this DotCraft thread's board/run lifecycle. Follow the current turn facts and use only the Oratorio runtime tools exposed for that turn.
        - Oratorio performs external delivery; do not push, merge, approve, request changes, create PRs/MRs, or mutate GitHub/GitLab directly.
        - Propose separate work with oratorio_run.SubmitFollowUpDraft when available.
        - Use oratorio_run.SubmitDiscussionReply only when the current turn is an Agent Discussion Turn.
        """;

    private const string DiscussionTurnInstructions = """
        Agent Discussion Turns:
        - When the user turn identifies an Oratorio Agent Discussion Turn, answer only that operator question.
        - Call oratorio_run.SubmitDiscussionReply with the discussionTurnId supplied in the user turn/context and your Markdown reply.
        - If the user turn lists open findings and the discussion shows one is a non-issue or already handled, you may resolve it with oratorio_run.ResolveReviewFinding; otherwise leave it open.
        - Do not modify files or turn the question into follow-up work.
        """;

    private const string ReviewDraftIntroInstructions = """
        During Oratorio PR/MR review-analysis runs when oratorio_run.SubmitReviewDraft is available:
        - Call oratorio_run.SubmitReviewDraft with the final draft; retry only when the tool asks you to repair anchors.
        - Clean review: summary.body `No issues found.`, majorCount 0, minorCount 0, suggestionCount 0, comments: [].
        - Findings review: summary.body `Found N issue.` or `Found N issues.`, with details in inline comments.
        - Prioritize actionable bugs and investigation flags.
        - Severity: RED for high-confidence correctness/security/data-loss/workflow bugs; YELLOW for lower-confidence, maintainability, or investigation findings.
        - Inline comments: concise problem title, natural reviewer prose explaining failure mode and impact, and a short fix direction when useful.
        """;

    private const string ReviewDraftDiffInstructions = """
        - Do not treat git show HEAD or HEAD^..HEAD as the complete PR/MR review range.
        - For large PRs/MRs, inspect local git diff shards such as file lists, stats, and focused per-path diffs instead of relying on a single full diff.
        - Prioritize high-risk changed files and submit only high-confidence inline findings with precise repository-relative paths.
        - Every inline comment must set kind to suggestion or commentOnly. kind is authoritative; fields from the other branch are ignored.
        - For each fixable RIGHT-side finding, set kind: suggestion and provide flat oldText/newText fields. oldText must be the exact current contiguous right-side diff text to replace, including enough surrounding lines to be unique; Oratorio derives the GitHub/GitLab review anchor.
        - For prose-only findings, set kind: commentOnly and provide flat line/reason fields; side/startLine/startSide are optional. reason is one of: needsHumanDecision, requiresLargerChange, cannotAnchorSafely, investigateOnly, leftSideOrDeletion.
        - Never send the retired nested suggestion or commentOnly objects.
        - If oratorio_run.SubmitReviewDraft fails with InvalidArguments, reviewDraftAnchorNotCommentable, reviewDraftSuggestionTextNotFound, or reviewDraftSuggestionTextAmbiguous, repair the kind-discriminated payload and call oratorio_run.SubmitReviewDraft again before your final response.
        - Count only accepted concrete code suggestions in suggestionCount; do not count prose-only findings or follow-up ideas as suggestions.
        - Do not place machine-readable review JSON in the final answer.
        """;

    private const string ImplementationDraftInstructions = """
        During Oratorio implementation runs when oratorio_run.SubmitImplementationDraft is available:
        - Modify only files inside the Oratorio-managed execution worktree named in the user turn.
        - When your implementation draft is ready, call oratorio_run.SubmitImplementationDraft with summary, tests, risks, changedFiles, proposedCommitMessage, proposedPrTitle, and proposedPrBody.
        - Do not place machine-readable implementation JSON in the final answer.
        """;

    private const string FollowUpDraftInstructions = """
        During Oratorio review or implementation runs when oratorio_run.SubmitFollowUpDraft is available:
        - If you identify follow-up work that should be split from the current round, call oratorio_run.SubmitFollowUpDraft with proposals.
        - Follow-up drafts are advisory, not hidden requirements for this round.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<AppServerPrompt> BuildAsync(
        OratorioRun run,
        string? operatorNote,
        string workspacePath,
        IReadOnlyList<string> requiredDynamicTools,
        bool incremental,
        CancellationToken ct)
    {
        var item = await db.Items.AsNoTracking().FirstAsync(x => x.ItemId == run.ItemId, ct);
        var round = await db.Rounds.AsNoTracking().FirstAsync(x => x.RoundId == run.RoundId, ct);
        var comments = await db.Comments.AsNoTracking()
            .Where(x => x.ItemId == item.ItemId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
        var rounds = await db.Rounds.AsNoTracking()
            .Where(x => x.ItemId == item.ItemId)
            .OrderBy(x => x.RoundNumber)
            .ToListAsync(ct);
        var allRuns = await db.Runs.AsNoTracking()
            .Where(x => x.ItemId == item.ItemId)
            .OrderBy(x => x.StartedAt ?? x.CompletedAt ?? DateTimeOffset.MinValue)
            .ToListAsync(ct);
        var decisions = await db.Decisions.AsNoTracking()
            .Where(x => x.ItemId == item.ItemId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
        var priorRuns = await db.Runs.AsNoTracking()
            .Where(x => x.ItemId == item.ItemId && x.RunId != run.RunId && x.Summary != null)
            .OrderBy(x => x.StartedAt ?? x.CompletedAt)
            .Select(x => new
            {
                x.Attempt,
                x.RunnerKind,
                x.Status,
                x.Summary,
                x.ErrorCode,
                x.CompletedAt
            })
            .ToListAsync(ct);
        var previousRetryAttempt = run.RetryCount > 0
            ? allRuns
                .Where(x => x.RoundId == run.RoundId && x.Attempt == run.Attempt - 1)
                .OrderByDescending(x => x.CompletedAt ?? x.StartedAt ?? DateTimeOffset.MinValue)
                .FirstOrDefault()
            : null;
        var isPullRequestReview = run.Purpose == RunPurpose.ReviewAnalysis && item.Source is "github" or "gitlab" && item.Kind == ItemKind.PullRequest;
        var priorOpenFindings = isPullRequestReview
            ? await db.ReviewDraftComments.AsNoTracking()
                .Where(c =>
                    c.Draft!.ItemId == item.ItemId &&
                    c.Draft.Status == ReviewDraftStatus.Published &&
                    c.Status == ReviewDraftCommentStatus.Accepted &&
                    c.ResolutionState == ReviewFindingResolutionState.Open)
                .OrderBy(c => c.Draft!.CreatedAt)
                .ThenBy(c => c.Path)
                .ThenBy(c => c.Line)
                .Select(c => new PromptFinding(c.DraftCommentId, c.Severity, c.Title, c.Path, c.Line))
                .ToListAsync(ct)
            : [];
        var isImplementationRun = run.Purpose == RunPurpose.Implementation &&
            item.Kind is ItemKind.Issue or ItemKind.LocalTask;
        var generatedPr = isImplementationRun
            ? await db.Items.AsNoTracking()
                .Where(x =>
                    x.ParentItemId == item.ItemId &&
                    x.Kind == ItemKind.PullRequest &&
                    (x.Source == "github" || x.Source == "gitlab") &&
                    x.SourceState == SourceState.Open)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(ct)
            : null;
        var followUpFindings = generatedPr is null
            ? new List<PromptFollowUpFinding>()
            : await db.ReviewDraftComments.AsNoTracking()
                .Where(c =>
                    c.Draft!.ItemId == generatedPr.ItemId &&
                    c.Draft.Status == ReviewDraftStatus.Published &&
                    c.Status == ReviewDraftCommentStatus.Accepted &&
                    c.ResolutionState == ReviewFindingResolutionState.Open)
                .OrderBy(c => c.Draft!.CreatedAt)
                .ThenBy(c => c.Path)
                .ThenBy(c => c.Line)
                .Select(c => new PromptFollowUpFinding(c.Severity, c.Title, c.Body, c.Path, c.Line, c.SuggestionReplacement))
                .Take(50)
                .ToListAsync(ct);
        var lastImplementationCompletedAt = allRuns
            .Where(r => r.RunId != run.RunId && r.Purpose == RunPurpose.Implementation && r.Status == RunStatus.Succeeded && r.CompletedAt != null)
            .Select(r => r.CompletedAt!.Value)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();
        var followUpComments = generatedPr is null
            ? new List<PromptFollowUpComment>()
            : (await db.Comments.AsNoTracking()
                .Where(c =>
                    c.ItemId == generatedPr.ItemId &&
                    c.Purpose == CommentPurpose.SourceContext &&
                    c.SourceCommentId != null)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new { c.AuthorName, c.Body, c.CreatedAt, c.SourceUpdatedAt })
                .ToListAsync(ct))
                .Where(c => (c.SourceUpdatedAt ?? c.CreatedAt) > lastImplementationCompletedAt)
                .Select(c => new PromptFollowUpComment(c.AuthorName, c.Body))
                .Take(50)
                .ToList();
        var sourceSnapshots = await db.SourceSnapshots.AsNoTracking()
            .Where(x => x.ItemId == item.ItemId)
            .OrderByDescending(x => x.SyncedAt)
            .Take(20)
            .ToListAsync(ct);
        var sourceSnapshot = sourceSnapshots.FirstOrDefault();
        var reviewDiff = ResolveReviewDiffTarget(item, sourceSnapshots);
        var decisionCommentIds = decisions
            .Select(x => x.CommentId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);
        var feedbackRoundNumber = round.RoundNumber == 1 ? 1 : round.RoundNumber - 1;
        var feedbackRoundId = rounds.FirstOrDefault(x => x.RoundNumber == feedbackRoundNumber)?.RoundId;
        var feedbackForThisRound = feedbackRoundId is null
            ? Array.Empty<PromptFeedback>()
            : decisions
                .Where(decision => decision.RoundId == feedbackRoundId && decision.Decision == DecisionType.RequestChanges && !string.IsNullOrWhiteSpace(decision.Body))
                .Select(decision => new PromptFeedback(
                    "requestChanges",
                    decision.DecisionId,
                    decision.CommentId,
                    decision.Body!,
                    decision.CreatedAt))
                .Concat(comments
                    .Where(comment =>
                        comment.RoundId == feedbackRoundId &&
                        IsOperatorComment(comment) &&
                        !decisionCommentIds.Contains(comment.CommentId))
                    .Select(comment => new PromptFeedback(
                        "operatorComment",
                        null,
                        comment.CommentId,
                        comment.Body,
                        comment.CreatedAt)))
                .ToArray();

        var context = new
        {
            promptMode = "compact",
            runtimeContextVersion = RuntimeContextVersion,
            threadStateWorkspaceMode = ThreadStateWorkspaceMode,
            turnPromptMode = incremental ? "incremental" : "full",
            mode = run.Purpose == RunPurpose.Implementation ? "implementation" : "readOnlyReviewAnalysis",
            requiredDynamicTools,
            item = new
            {
                item.ItemId,
                item.Source,
                item.ExternalId,
                Kind = item.Kind.ToString(),
                item.Title,
                Body = item.Description,
                item.Repository,
                item.Assignee,
                item.Branch,
                item.ExternalUrl,
                Labels = ParseStringArray(item.LabelsJson),
                item.HeadSha,
                item.IsDraft,
                item.SourceUpdatedAt,
                item.LastSourceSyncAt
            },
            workspace = new
            {
                Path = workspacePath,
                BasePath = run.BaseWorkspacePath,
                WorktreePath = run.WorktreePath,
                run.WorktreeBranch,
                run.BaseRef,
                run.BaseSha,
                item.Repository,
                item.Branch,
                item.HeadSha
            },
            currentRound = new
            {
                round.RoundId,
                round.RoundNumber,
                round.Status,
                run.Attempt,
                run.RetryCount,
                run.Purpose,
                run.DispatchTrigger,
                run.TargetHeadSha,
                run.DeliveryPolicy,
                OperatorNote = operatorNote
            },
            roundHistory = rounds.Select(historyRound => new
            {
                historyRound.RoundId,
                historyRound.RoundNumber,
                historyRound.Status,
                historyRound.Summary,
                historyRound.CreatedAt,
                historyRound.CompletedAt,
                runs = allRuns
                    .Where(historyRun => historyRun.RoundId == historyRound.RoundId)
                    .Select(historyRun => new
                    {
                        historyRun.RunId,
                        historyRun.Attempt,
                        historyRun.RunnerKind,
                        historyRun.Status,
                        historyRun.Summary,
                        historyRun.ErrorCode,
                        historyRun.StartedAt,
                        historyRun.CompletedAt
                    }),
                decisions = decisions
                    .Where(decision => decision.RoundId == historyRound.RoundId)
                    .Select(decision => new
                    {
                        decision.DecisionId,
                        decision.Decision,
                        decision.Body,
                        decision.CommentId,
                        decision.CreatedAt
                    }),
                operatorComments = comments
                    .Where(comment => comment.RoundId == historyRound.RoundId && IsOperatorComment(comment))
                    .Select(comment => new
                    {
                        comment.CommentId,
                        comment.Body,
                        comment.CreatedAt
                    })
            }),
            feedbackForThisRound,
            priorOpenFindings = priorOpenFindings.Select(f => new { f.FindingId, f.Severity, f.Title, f.Path, f.Line }),
            importedComments = comments.Where(IsSourceComment).Select(x => new
            {
                x.AuthorKind,
                x.AuthorName,
                x.Body,
                x.Visibility,
                x.CreatedAt,
                x.Source,
                x.SourceCommentId,
                x.ExternalUrl,
                x.SourceUpdatedAt
            }),
            priorSummaries = priorRuns,
            sourceSnapshot = sourceSnapshot is null
                ? null
                : new
                {
                    sourceSnapshot.Source,
                    sourceSnapshot.ExternalId,
                    sourceSnapshot.Repository,
                    sourceSnapshot.HeadSha,
                    sourceSnapshot.SourceUpdatedAt,
                    sourceSnapshot.SyncedAt,
                    Payload = TryParseJson(sourceSnapshot.PayloadJson)
                }
        };

        var contextJson = JsonSerializer.Serialize(context, JsonOptions);
        var runtimeAdditionalContext = BuildThreadRuntimeAdditionalContext();
        var prompt = new StringBuilder();
        if (incremental)
        {
            prompt.AppendLine("You are continuing an existing Oratorio DotCraft thread with incremental context.");
        }
        else if (run.Purpose == RunPurpose.Implementation)
        {
            prompt.AppendLine("You are running an Oratorio implementation round through DotCraft AppServer.");
        }
        else
        {
            prompt.AppendLine("You are running an Oratorio read-only review analysis round through DotCraft AppServer.");
        }
        prompt.AppendLine();
        prompt.AppendLine(incremental ? "Review target reminder:" : "Review target:");
        prompt.AppendLine($"- Title: {item.Title}");
        prompt.AppendLine($"- Source: {item.Source} {item.ExternalId}");
        prompt.AppendLine($"- Kind: {item.Kind}");
        prompt.AppendLine($"- Repository: {item.Repository ?? "none"}");
        prompt.AppendLine($"- Branch: {item.Branch ?? "none"}");
        prompt.AppendLine($"- Head SHA: {item.HeadSha ?? "none"}");
        prompt.AppendLine($"- Target head SHA: {run.TargetHeadSha ?? item.HeadSha ?? "none"}");
        if (reviewDiff is not null)
        {
            prompt.AppendLine($"- Review diff base: {reviewDiff.BaseRef} / {reviewDiff.BaseSha}");
            prompt.AppendLine($"- Review diff head: {reviewDiff.HeadRef} / {reviewDiff.HeadSha}");
            prompt.AppendLine($"- Review diff range: {reviewDiff.BaseSha}...{reviewDiff.HeadSha}");
        }
        if (!incremental)
        {
            prompt.AppendLine($"- Review target head SHA: {item.HeadSha ?? run.BaseSha ?? "none"}");
            prompt.AppendLine($"- Head ref/SHA: {item.Branch ?? "none"} / {item.HeadSha ?? "none"}");
            prompt.AppendLine($"- Workspace checkout ref/SHA: {run.BaseRef ?? "none"} / {run.BaseSha ?? "none"}");
            prompt.AppendLine($"- URL: {item.ExternalUrl ?? "none"}");
            prompt.AppendLine($"- Workspace: {workspacePath}");
            if (!string.IsNullOrWhiteSpace(run.BaseWorkspacePath) && !SamePath(run.BaseWorkspacePath, workspacePath))
            {
                prompt.AppendLine($"- Base workspace: {run.BaseWorkspacePath}");
                prompt.AppendLine($"- Managed worktree branch: {run.WorktreeBranch ?? "none"}");
                prompt.AppendLine($"- Managed worktree checkout: {run.BaseSha ?? run.BaseRef ?? "none"}");
            }
        }
        if (!incremental && !string.IsNullOrWhiteSpace(item.Description))
        {
            prompt.AppendLine();
            prompt.AppendLine("Source description:");
            prompt.AppendLine(item.Description.Trim());
        }

        prompt.AppendLine();
        prompt.AppendLine(incremental ? "Incremental operator input:" : "New operator feedback:");
        if (!string.IsNullOrWhiteSpace(operatorNote))
        {
            prompt.AppendLine($"- Dispatch note: {operatorNote.Trim()}");
        }

        if (feedbackForThisRound.Length == 0)
        {
            prompt.AppendLine("- None.");
        }
        else
        {
            foreach (var feedback in feedbackForThisRound)
            {
                prompt.AppendLine($"- {feedback.Kind}: {feedback.Body.Trim()}");
            }
        }

        if (previousRetryAttempt is not null)
        {
            prompt.AppendLine();
            prompt.AppendLine("Retry recovery:");
            prompt.AppendLine($"- This is attempt {run.Attempt}; attempt {previousRetryAttempt.Attempt} ended with {previousRetryAttempt.ErrorCode ?? previousRetryAttempt.Status.ToString()}.");
            if (!string.IsNullOrWhiteSpace(previousRetryAttempt.ErrorMessage))
            {
                prompt.AppendLine($"- Previous failure: {previousRetryAttempt.ErrorMessage.Trim()}");
            }

            if (string.Equals(previousRetryAttempt.ErrorCode, "reviewDraftRequired", StringComparison.Ordinal))
            {
                prompt.AppendLine("- Reuse the prior analysis when available, repair any rejected Review Draft payload, and call oratorio_run.SubmitReviewDraft.");
                prompt.AppendLine("- Do not finish this turn until oratorio_run.SubmitReviewDraft returns a successful result, including for a clean review.");
            }
            else if (previousRetryAttempt.ErrorCode is "appServerTurnFailed" or "appServerTurnCancelled")
            {
                prompt.AppendLine("- Continue the interrupted review, verify the result against the current target, and complete the required oratorio_run.SubmitReviewDraft delivery before finishing.");
            }
            else
            {
                prompt.AppendLine("- Re-run the requested work and complete all required Oratorio dynamic-tool deliveries before finishing.");
            }
        }

        prompt.AppendLine();
        prompt.AppendLine("Current task:");
        if (run.Purpose == RunPurpose.Implementation)
        {
            prompt.AppendLine("- Implement the requested change in the Oratorio-managed worktree.");
            prompt.AppendLine("- Run focused validation when feasible and include observed results.");
        }
        else
        {
            prompt.AppendLine("- Inspect the current source state in the workspace and produce a concise review summary for the Oratorio operator.");
            prompt.AppendLine("- Mention blockers, missing information, and suggested next operator actions.");
        }

        if (isImplementationRun && generatedPr is not null && (followUpFindings.Count > 0 || followUpComments.Count > 0))
        {
            prompt.AppendLine();
            prompt.AppendLine($"Review feedback on the generated pull request ({generatedPr.ExternalUrl ?? generatedPr.ExternalId}, branch {generatedPr.Branch ?? "unknown"}):");
            prompt.AppendLine("- You are continuing this existing pull request. Apply fixes in the managed worktree; your committed changes are delivered as follow-up commits to the same pull request.");
            prompt.AppendLine("- Do not resolve review findings yourself; a follow-up Oratorio review re-evaluates the new head and resolves the findings it confirms fixed.");
            if (followUpFindings.Count > 0)
            {
                prompt.AppendLine("- Open review findings to address:");
                foreach (var finding in followUpFindings)
                {
                    var anchor = finding.Line is null ? finding.Path : $"{finding.Path}:{finding.Line}";
                    prompt.AppendLine($"  - {finding.Severity} {finding.Title} ({anchor})");
                    if (!string.IsNullOrWhiteSpace(finding.Body))
                    {
                        prompt.AppendLine($"    {finding.Body.Trim()}");
                    }

                    if (!string.IsNullOrWhiteSpace(finding.SuggestionReplacement))
                    {
                        prompt.AppendLine("    Suggested replacement:");
                        foreach (var line in finding.SuggestionReplacement.Replace("\r\n", "\n").Split('\n'))
                        {
                            prompt.AppendLine($"      {line}");
                        }
                    }
                }
            }

            if (followUpComments.Count > 0)
            {
                prompt.AppendLine("- New human review comments to consider:");
                foreach (var comment in followUpComments)
                {
                    prompt.AppendLine($"  - {comment.AuthorName}: {comment.Body.Trim()}");
                }
            }
        }

        if (run.Purpose == RunPurpose.ReviewAnalysis && item.Source is "github" or "gitlab" && item.Kind == ItemKind.PullRequest)
        {
            if (priorOpenFindings.Count > 0)
            {
                prompt.AppendLine();
                prompt.AppendLine("Open findings from earlier published review rounds:");
                foreach (var finding in priorOpenFindings)
                {
                    prompt.AppendLine($"- [{finding.FindingId}] {finding.Severity} {finding.Title} ({finding.Path}:{finding.Line})");
                }

                prompt.AppendLine($"- For each open finding above that the current head now addresses, call {OratorioDynamicToolCatalog.ResolveReviewFindingId} with its findingId and resolutionKind `fixed`. Leave still-present findings open and do not re-report them as new comments.");
            }
        }
        prompt.AppendLine();
        prompt.AppendLine("Available tools:");
        if (requiredDynamicTools.Count == 0)
        {
            prompt.AppendLine("- None.");
        }
        else
        {
            foreach (var tool in requiredDynamicTools)
            {
                prompt.AppendLine($"- {tool}");
            }
        }

        return new AppServerPrompt(contextJson, prompt.ToString(), runtimeAdditionalContext);
    }

    public static IReadOnlyDictionary<string, AppServerRuntimeAdditionalContextEntry> BuildThreadRuntimeAdditionalContext() =>
        new Dictionary<string, AppServerRuntimeAdditionalContextEntry>(StringComparer.Ordinal)
        {
            ["oratorio.runContract"] = RuntimeEntry(RunContractInstructions),
            ["oratorio.discussionTurn"] = RuntimeEntry(DiscussionTurnInstructions),
            ["oratorio.reviewDraft"] = RuntimeEntry(BuildReviewDraftRuntimeContext()),
            ["oratorio.implementationDraft"] = RuntimeEntry(ImplementationDraftInstructions),
            ["oratorio.followUpDraft"] = RuntimeEntry(FollowUpDraftInstructions)
        };

    private static string BuildReviewDraftRuntimeContext()
    {
        var sb = new StringBuilder();
        sb.AppendLine("PR/MR review draft contract:");
        sb.AppendLine();
        sb.AppendLine(ReviewDraftIntroInstructions.Trim());
        sb.AppendLine("- When the user turn lists a Review diff range, first inspect its file list/stat and focused per-path diffs before concluding the PR/MR is clean.");
        sb.AppendLine(ReviewDraftDiffInstructions.Trim());
        sb.AppendLine("- When the user turn lists open findings from earlier published review rounds, call oratorio_run.ResolveReviewFinding for each finding that the current head now addresses, and do not re-report still-present findings as new comments.");
        return sb.ToString().Trim();
    }

    private static AppServerRuntimeAdditionalContextEntry RuntimeEntry(string value) =>
        new(value.Trim());

    private static bool IsOperatorComment(OratorioComment comment) =>
        comment.AuthorKind == AuthorKind.Operator &&
        comment.Purpose == CommentPurpose.Feedback &&
        string.IsNullOrWhiteSpace(comment.Source) &&
        string.IsNullOrWhiteSpace(comment.SourceCommentId) &&
        string.IsNullOrWhiteSpace(comment.ExternalUrl);

    private static bool IsSourceComment(OratorioComment comment) =>
        comment.Purpose == CommentPurpose.SourceContext ||
        comment.AuthorKind == AuthorKind.Source ||
        !string.IsNullOrWhiteSpace(comment.Source) ||
        !string.IsNullOrWhiteSpace(comment.SourceCommentId) ||
        !string.IsNullOrWhiteSpace(comment.ExternalUrl);

    private static ReviewDiffTarget? ResolveReviewDiffTarget(OratorioItem item, IReadOnlyList<OratorioSourceSnapshot> sourceSnapshots)
    {
        if (item.Kind != ItemKind.PullRequest ||
            string.IsNullOrWhiteSpace(item.HeadSha))
        {
            return null;
        }

        foreach (var snapshot in sourceSnapshots)
        {
            if (!string.Equals(snapshot.Source, item.Source, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = item.Source switch
            {
                "github" => TryReadGitHubReviewDiffTarget(snapshot.PayloadJson),
                "gitlab" => TryReadGitLabReviewDiffTarget(snapshot.PayloadJson),
                _ => null
            };
            if (target is not null && string.Equals(target.HeadSha, item.HeadSha, StringComparison.OrdinalIgnoreCase))
            {
                return target;
            }
        }

        return null;
    }

    private static ReviewDiffTarget? TryReadGitHubReviewDiffTarget(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (!TryGetObject(document.RootElement, out var pullRequest, "pull_request", "pullRequest") ||
                !TryGetObject(pullRequest, out var @base, "base", "Base") ||
                !TryGetObject(pullRequest, out var head, "head", "Head"))
            {
                return null;
            }

            var baseRef = TryGetString(@base, "ref", "Ref");
            var baseSha = TryGetString(@base, "sha", "Sha");
            var headRef = TryGetString(head, "ref", "Ref");
            var headSha = TryGetString(head, "sha", "Sha");
            return string.IsNullOrWhiteSpace(baseRef) ||
                string.IsNullOrWhiteSpace(baseSha) ||
                string.IsNullOrWhiteSpace(headRef) ||
                string.IsNullOrWhiteSpace(headSha)
                ? null
                : new ReviewDiffTarget(baseRef, baseSha, headRef, headSha);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ReviewDiffTarget? TryReadGitLabReviewDiffTarget(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (!TryGetObject(document.RootElement, out var mergeRequest, "merge_request", "mergeRequest") ||
                !TryGetObject(mergeRequest, out var diffRefs, "diff_refs", "diffRefs"))
            {
                return null;
            }

            var baseRef = TryGetString(mergeRequest, "target_branch", "targetBranch");
            var baseSha = TryGetString(diffRefs, "base_sha", "baseSha");
            var headRef = TryGetString(mergeRequest, "source_branch", "sourceBranch");
            var headSha = TryGetString(diffRefs, "head_sha", "headSha");
            return string.IsNullOrWhiteSpace(baseRef) ||
                string.IsNullOrWhiteSpace(baseSha) ||
                string.IsNullOrWhiteSpace(headRef) ||
                string.IsNullOrWhiteSpace(headSha)
                ? null
                : new ReviewDiffTarget(baseRef, baseSha, headRef, headSha);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetObject(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(name, out value) &&
                value.ValueKind == JsonValueKind.Object)
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? TryGetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static JsonElement? TryParseJson(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool SamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            left = Path.GetFullPath(left);
            right = Path.GetFullPath(right);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return string.Equals(left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private sealed record PromptFeedback(string Kind, string? DecisionId, string? CommentId, string Body, DateTimeOffset CreatedAt);

    private sealed record PromptFinding(string FindingId, string Severity, string Title, string Path, int Line);

    private sealed record PromptFollowUpFinding(string Severity, string Title, string Body, string Path, int? Line, string? SuggestionReplacement);

    private sealed record PromptFollowUpComment(string AuthorName, string Body);

    private sealed record ReviewDiffTarget(string BaseRef, string BaseSha, string HeadRef, string HeadSha);
}

public sealed record AppServerPrompt(
    string ContextJson,
    string Prompt,
    IReadOnlyDictionary<string, AppServerRuntimeAdditionalContextEntry> RuntimeAdditionalContext);
