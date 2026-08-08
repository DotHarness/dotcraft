using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;
using Oratorio.Server.Services;

namespace Oratorio.Server.GitHub;

public sealed class GitHubCommentCommandProcessor(
    OratorioDbContext db,
    GitHubSourceService gitHubSource,
    OratorioService oratorio,
    IClock clock,
    ILogger<GitHubCommentCommandProcessor> logger)
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SecondRetryDelay = TimeSpan.FromSeconds(30);

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
                    $"GitHub review command repository '{command.Repository}' is invalid.");
            }

            var item = await gitHubSource.SyncPullRequestAsync(
                repository,
                command.PullRequestNumber,
                ct);
            await oratorio.DispatchGitHubCommentCommandAsync(command.CommandId, item.ItemId, ct);
            logger.LogInformation(
                "Dispatched GitHub review command {CommandId} for {Repository}#{PullRequestNumber}.",
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
                "GitHub review command {CommandId} attempt {Attempt} failed transiently; retry scheduled for {NextAttemptAt}.",
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
                "GitHub review command {CommandId} failed permanently after {AttemptCount} attempt(s).",
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
