using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Services;
using DotCraft.Oratorio.Sources;

namespace DotCraft.Oratorio.GitHub;

public sealed class GitHubCommentScanService(
    OratorioDbContext db,
    IGitHubApiClient client,
    GitHubCommentCommandIntakeService intake,
    IOptionsMonitor<GitHubOptions> gitHubOptions,
    IOptionsMonitor<DotCraftOptions> dotCraftOptions,
    IClock clock,
    ILogger<GitHubCommentScanService> logger)
{
    private static readonly TimeSpan Overlap = TimeSpan.FromMinutes(2);

    public async Task ScanAsync(CancellationToken ct)
    {
        foreach (var repository in ResolveRepositories())
        {
            await ScanRepositoryAsync(repository, ct);
        }
    }

    private IReadOnlyList<GitHubRepositoryRef> ResolveRepositories()
    {
        var dotCraft = dotCraftOptions.CurrentValue;
        return gitHubOptions.CurrentValue.Repositories
            .Select(SourceProjectKey.NormalizeGitHubRepository)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(repository => GitHubCommentCommandIntakeService.HasWorkspaceMapping(dotCraft, repository))
            .Select(repository => GitHubRepositoryRef.TryParse(repository, out var parsed) ? parsed : null)
            .OfType<GitHubRepositoryRef>()
            .ToArray();
    }

    private async Task ScanRepositoryAsync(GitHubRepositoryRef repository, CancellationToken ct)
    {
        var now = clock.UtcNow;
        try
        {
            var cursor = await LoadCursorAsync(repository, now, ct);
            var issueSince = Floor(cursor.BaselineAt, cursor.IssueCommentsHighWaterAt);
            var issueComments = await client.ListRepositoryIssueCommentsAsync(
                repository,
                issueSince,
                cursor.IssueCommentsETag,
                ct);
            if (!issueComments.NotModified)
            {
                await IntakeAsync(
                    issueComments.Items
                        .Where(x => x.CreatedAt > issueSince)
                        .Select(x => GitHubCommentCandidate.FromConversationComment(repository.FullName, x)),
                    ct);
                cursor = await LoadCursorAsync(repository, now, ct);
                cursor.IssueCommentsHighWaterAt = Max(cursor.IssueCommentsHighWaterAt, issueComments.Items.Select(x => x.UpdatedAt));
                cursor.IssueCommentsETag = issueComments.ETag;
                cursor.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
            }

            var reviewSince = Floor(cursor.BaselineAt, cursor.ReviewCommentsHighWaterAt);
            var reviewComments = await client.ListRepositoryReviewCommentsAsync(
                repository,
                reviewSince,
                cursor.ReviewCommentsETag,
                ct);
            if (!reviewComments.NotModified)
            {
                await IntakeAsync(
                    reviewComments.Items
                        .Where(x => x.CreatedAt > reviewSince)
                        .Select(x => GitHubCommentCandidate.FromReviewComment(repository.FullName, x)),
                    ct);
                cursor = await LoadCursorAsync(repository, now, ct);
                cursor.ReviewCommentsHighWaterAt = Max(cursor.ReviewCommentsHighWaterAt, reviewComments.Items.Select(x => x.UpdatedAt));
                cursor.ReviewCommentsETag = reviewComments.ETag;
            }

            cursor.LastScannedAt = now;
            cursor.LastErrorCode = null;
            cursor.LastErrorMessage = null;
            cursor.LastErrorAt = null;
            cursor.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GitHub comment scan failed for {Repository}.", repository.FullName);
            await RecordFailureAsync(repository, ex, now, ct);
        }
    }

    private async Task IntakeAsync(IEnumerable<GitHubCommentCandidate?> candidates, CancellationToken ct)
    {
        foreach (var candidate in candidates)
        {
            if (candidate is null || candidate.IsBotAuthor)
            {
                continue;
            }

            await intake.IntakeAsync(candidate, null, ct);
        }
    }

    private async Task RecordFailureAsync(
        GitHubRepositoryRef repository,
        Exception exception,
        DateTimeOffset now,
        CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var cursor = await LoadCursorAsync(repository, now, ct);
        cursor.LastScannedAt = now;
        cursor.LastErrorCode = exception is GitHubAppAuthenticationRequiredException auth
            ? auth.ErrorCode
            : "githubCommentScanFailed";
        cursor.LastErrorMessage = exception.Message;
        cursor.LastErrorAt = now;
        cursor.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    private async Task<OratorioGitHubCommentScanCursor> LoadCursorAsync(
        GitHubRepositoryRef repository,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var cursor = await db.GitHubCommentScanCursors.FirstOrDefaultAsync(x => x.Repository == repository.FullName, ct);
        if (cursor is not null)
        {
            return cursor;
        }

        cursor = new OratorioGitHubCommentScanCursor
        {
            Repository = repository.FullName,
            BaselineAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.GitHubCommentScanCursors.Add(cursor);
        await db.SaveChangesAsync(ct);
        return cursor;
    }

    private static DateTimeOffset Floor(DateTimeOffset baseline, DateTimeOffset? highWater) =>
        highWater is { } value && value - Overlap > baseline
            ? value - Overlap
            : baseline;

    private static DateTimeOffset? Max(DateTimeOffset? current, IEnumerable<DateTimeOffset> observed)
    {
        var result = current;
        foreach (var value in observed)
        {
            if (result is null || value > result)
            {
                result = value;
            }
        }

        return result;
    }
}
