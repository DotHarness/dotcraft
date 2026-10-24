using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.Services;

namespace DotCraft.Oratorio.GitHub;

public sealed class GitHubCommentCommandProcessor(
    OratorioDbContext db,
    GitHubSourceService gitHubSource,
    OratorioService oratorio,
    DiscussionTurnService discussionTurns,
    IGitHubApiClient client,
    IOptionsMonitor<GitHubOptions> options,
    IGitHubCredentialResolver credentials,
    IClock clock,
    ILogger<GitHubCommentCommandProcessor> logger)
{
    private const string AcknowledgementReaction = "eyes";
    private const int MaxAttempts = 3;
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SecondRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan QuestionWaitDelay = TimeSpan.FromSeconds(1);

    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var command = await db.GitHubCommentCommands
            .Where(x =>
                x.Status == GitHubCommentCommandStatus.Queued &&
                (x.NextAttemptAt == null || x.NextAttemptAt <= now))
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (command is null)
        {
            return false;
        }

        if (command.CommandKind == GitHubCommentCommandKind.Question && await QuestionMustWaitAsync(command, ct))
        {
            await DeferQuestionAsync(command, ct);
            return true;
        }

        command.AttemptCount++;
        command.NextAttemptAt = null;
        command.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        try
        {
            if (!GitHubRepositoryRef.TryParse(command.Repository, out var repository))
            {
                throw OratorioApiException.Conflict(
                    "githubCommentCommandRepositoryInvalid",
                    $"GitHub comment command repository '{command.Repository}' is invalid.");
            }

            var item = await gitHubSource.SyncPullRequestAsync(
                repository,
                command.PullRequestNumber,
                ct);
            if (item.SourceState != SourceState.Open)
            {
                await RejectAsync(
                    command,
                    "githubCommentCommandPullRequestNotOpen",
                    $"Pull request {command.Repository}#{command.PullRequestNumber} is not open.",
                    ct);
                return true;
            }

            await AcknowledgeAsync(command, repository, ct);
            if (command.CommandKind == GitHubCommentCommandKind.Question)
            {
                if (!await discussionTurns.CreateFromGitHubQuestionAsync(command, item, ct))
                {
                    command.AttemptCount--;
                    await DeferQuestionAsync(command, ct);
                    return true;
                }
            }
            else
            {
                await oratorio.DispatchGitHubCommentCommandAsync(command.CommandId, item.ItemId, ct);
            }

            logger.LogInformation(
                "Dispatched GitHub {CommandKind} command {CommandId} for {Repository}#{PullRequestNumber}.",
                command.CommandKind,
                command.CommandId,
                command.Repository,
                command.PullRequestNumber);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsExpectedProcessingFailure(ex))
        {
            await RecordFailureAsync(command, ex, ct);
        }

        return true;
    }

    private async Task<bool> QuestionMustWaitAsync(OratorioGitHubCommentCommand command, CancellationToken ct)
    {
        var earlierQuestionQueued = await db.GitHubCommentCommands.AnyAsync(x =>
            x.CommandId != command.CommandId &&
            x.CommandKind == GitHubCommentCommandKind.Question &&
            x.Status == GitHubCommentCommandStatus.Queued &&
            x.Repository == command.Repository &&
            x.PullRequestNumber == command.PullRequestNumber &&
            x.CreatedAt < command.CreatedAt,
            ct);
        if (earlierQuestionQueued || !GitHubRepositoryRef.TryParse(command.Repository, out var repository))
        {
            return earlierQuestionQueued;
        }

        var externalId = GitHubSourceService.PullRequestExternalId(repository, command.PullRequestNumber);
        return await db.DiscussionTurns.AnyAsync(x =>
            x.Status == DiscussionTurnStatus.Pending &&
            x.Item!.Source == "github" &&
            x.Item.ExternalId == externalId,
            ct);
    }

    private async Task DeferQuestionAsync(OratorioGitHubCommentCommand command, CancellationToken ct)
    {
        var now = clock.UtcNow;
        command.NextAttemptAt = now + QuestionWaitDelay;
        command.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    private async Task RejectAsync(
        OratorioGitHubCommentCommand command,
        string code,
        string message,
        CancellationToken ct)
    {
        var now = clock.UtcNow;
        command.Status = GitHubCommentCommandStatus.Rejected;
        command.ErrorCode = code;
        command.ErrorMessage = message;
        command.NextAttemptAt = null;
        command.UpdatedAt = now;
        command.CompletedAt = now;
        await db.SaveChangesAsync(ct);
    }

    private async Task AcknowledgeAsync(
        OratorioGitHubCommentCommand command,
        GitHubRepositoryRef repository,
        CancellationToken ct)
    {
        if (command.AcknowledgedAt is not null || !credentials.Resolve(options.CurrentValue).CanWrite)
        {
            return;
        }

        try
        {
            if (command.CommentKind == GitHubCommentKind.ReviewComment)
            {
                await client.CreateReviewCommentReactionAsync(repository, command.CommentId, AcknowledgementReaction, ct);
            }
            else
            {
                await client.CreateIssueCommentReactionAsync(repository, command.CommentId, AcknowledgementReaction, ct);
            }

            command.AcknowledgedAt = clock.UtcNow;
            command.AcknowledgementErrorCode = null;
            command.AcknowledgementErrorMessage = null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            command.AcknowledgementErrorCode = ex is GitHubAppAuthenticationRequiredException auth
                ? auth.ErrorCode
                : "githubCommentCommandReactionFailed";
            command.AcknowledgementErrorMessage = ex.Message;
            logger.LogWarning(
                ex,
                "GitHub comment command {CommandId} could not add its acknowledgement reaction.",
                command.CommandId);
        }

        command.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task RecordFailureAsync(
        OratorioGitHubCommentCommand command,
        Exception exception,
        CancellationToken ct)
    {
        var now = clock.UtcNow;
        var (code, message) = DescribeFailure(exception);
        command.ErrorCode = code;
        command.ErrorMessage = message;
        command.UpdatedAt = now;

        if (IsTransient(exception) && command.AttemptCount < MaxAttempts)
        {
            command.Status = GitHubCommentCommandStatus.Queued;
            command.NextAttemptAt = now + (command.AttemptCount == 1 ? FirstRetryDelay : SecondRetryDelay);
            command.CompletedAt = null;
            logger.LogWarning(
                exception,
                "GitHub comment command {CommandId} attempt {Attempt} failed transiently; retry scheduled for {NextAttemptAt}.",
                command.CommandId,
                command.AttemptCount,
                command.NextAttemptAt);
        }
        else
        {
            command.Status = GitHubCommentCommandStatus.Failed;
            command.NextAttemptAt = null;
            command.CompletedAt = now;
            logger.LogWarning(
                exception,
                "GitHub comment command {CommandId} failed permanently after {AttemptCount} attempt(s).",
                command.CommandId,
                command.AttemptCount);
        }

        await db.SaveChangesAsync(ct);
    }

    private static bool IsExpectedProcessingFailure(Exception exception) =>
        exception is HttpRequestException or
            JsonException or
            InvalidOperationException or
            OratorioApiException or
            ArgumentException;

    private static bool IsTransient(Exception exception) =>
        exception switch
        {
            GitHubAppAuthenticationRequiredException => false,
            OratorioApiException { Code: "sourceDetailsSyncFailed" } => true,
            OratorioApiException => false,
            HttpRequestException { StatusCode: null } => true,
            HttpRequestException { StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests } => true,
            HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError } => true,
            HttpRequestException => false,
            JsonException => true,
            InvalidOperationException => true,
            _ => false
        };

    private static (string Code, string Message) DescribeFailure(Exception exception) =>
        exception switch
        {
            GitHubAppAuthenticationRequiredException auth => (auth.ErrorCode, auth.Message),
            OratorioApiException api => (api.Code, api.Message),
            HttpRequestException => ("githubCommentCommandGitHubReadFailed", exception.Message),
            JsonException => ("githubCommentCommandGitHubReadFailed", exception.Message),
            InvalidOperationException => ("githubCommentCommandProcessingFailed", exception.Message),
            ArgumentException => ("githubCommentCommandTargetInvalid", exception.Message),
            _ => ("githubCommentCommandProcessingFailed", exception.Message)
        };
}
