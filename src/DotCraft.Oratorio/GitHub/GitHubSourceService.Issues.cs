using Microsoft.EntityFrameworkCore;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;

namespace DotCraft.Oratorio.GitHub;

public sealed partial class GitHubSourceService
{
    public async Task<OratorioItem> SyncIssueAsync(GitHubRepositoryRef repository, int number, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        var issue = await client.GetIssueAsync(repository, number, ct);
        if (issue.Number != number || issue.PullRequest is not null)
        {
            throw new InvalidOperationException($"GitHub did not return issue #{number}.");
        }

        await UpsertItemAsync(ToSyncItem(repository, issue), clock.UtcNow, detailsHydrated: false, ct);
        var externalId = TargetExternalId(repository, ItemKind.Issue, number);
        var item = await db.Items.FirstAsync(x => x.Source == "github" && x.ExternalId == externalId, ct);
        item.SourceDetailsStatus = SourceDetailsStatus.Stale;
        return item;
    }

    public static string TargetExternalId(GitHubRepositoryRef repository, ItemKind kind, int number) =>
        kind == ItemKind.Issue ? $"issue:{repository.FullName}#{number}" : PullRequestExternalId(repository, number);

    private static GitHubSyncItem ToSyncItem(GitHubRepositoryRef repository, GitHubIssue issue) =>
        new(
            repository.FullName,
            TargetExternalId(repository, ItemKind.Issue, issue.Number),
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
}
