using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Integrations;

namespace DotCraft.Oratorio.Services;

public sealed class DiscussionWorkspacePreparer(
    OratorioDbContext db,
    ConversationThreadStarter starter,
    IGitHubApiClient gitHub)
{
    public static bool IsIndependentIssue(OratorioDiscussionTurn turn, string contextJson) =>
        turn.Item is { Kind: ItemKind.Issue, Source: "github" } &&
        ConversationThreadContext.Purpose(contextJson) != RunPurpose.Implementation;

    public async Task<ReviewTargetWorkspace> PrepareAsync(
        OratorioDiscussionTurn turn, string baseWorkspacePath, CancellationToken ct)
    {
        var item = turn.Item!;
        var refreshIssueBranch = item.Kind == ItemKind.Issue;
        turn.BaseWorkspacePath = baseWorkspacePath;
        if (!starter.UsesManagedWorktrees)
        {
            if (refreshIssueBranch)
            {
                throw new WorktreeException("issueDiscussionManagedWorktreeRequired", "Independent issue discussions require managed worktrees.");
            }

            await db.SaveChangesAsync(ct);
            return new ReviewTargetWorkspace(baseWorkspacePath, baseWorkspacePath, null, null, null);
        }

        turn.WorktreeStatus = WorktreeStatus.Preparing;
        await db.SaveChangesAsync(ct);
        var request = ConversationThreadStarter.CreateWorktreeRequest(
            turn.DiscussionTurnId, item, item.HeadSha, baseWorkspacePath);
        if (refreshIssueBranch)
        {
            if (!GitHubRepositoryRef.TryParse(item.Repository ?? "", out var repository))
            {
                throw new WorktreeException("issueDiscussionRepositoryInvalid", "The issue does not have a valid GitHub repository.");
            }

            var source = await gitHub.GetRepositoryAsync(repository, ct);
            if (string.IsNullOrWhiteSpace(source.DefaultBranch))
            {
                throw new WorktreeException("issueDiscussionDefaultBranchMissing", "GitHub did not return a default branch.");
            }

            request = request with { HeadSha = null, FetchBaseBranch = source.DefaultBranch };
        }

        var result = await starter.PrepareWorktreeAsync(request, ct);
        turn.BaseWorkspacePath = result.BaseWorkspacePath;
        turn.WorktreePath = result.WorktreePath;
        turn.WorktreeStatus = WorktreeStatus.Ready;
        await db.SaveChangesAsync(ct);
        return new ReviewTargetWorkspace(result.WorktreePath, result.BaseWorkspacePath, result.WorktreeBranch, result.BaseRef, result.BaseSha);
    }

    public static string DescribeIssueWorkspace(ReviewTargetWorkspace workspace) =>
        $"Issue analysis workspace:\n- Default branch: {workspace.BaseRef}\n- Commit: {workspace.BaseSha}\n- Execution path: {workspace.Path}\n";
}
