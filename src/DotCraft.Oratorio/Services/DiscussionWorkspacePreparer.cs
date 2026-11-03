using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.GitLab;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Sources;

namespace DotCraft.Oratorio.Services;

public sealed class DiscussionWorkspacePreparer(
    OratorioDbContext db,
    ConversationThreadStarter starter,
    IGitHubApiClient gitHub,
    IGitLabApiClient gitLab)
{
    public static bool IsIndependent(OratorioDiscussionTurn turn, string contextJson) =>
        turn.Item!.Kind != ItemKind.PullRequest &&
        ConversationThreadContext.Purpose(contextJson) != RunPurpose.Implementation;

    public async Task<ReviewTargetWorkspace> PrepareAsync(
        OratorioDiscussionTurn turn, string baseWorkspacePath, CancellationToken ct)
    {
        var item = turn.Item!;
        var refreshBaseBranch = item.Kind != ItemKind.PullRequest;
        turn.BaseWorkspacePath = baseWorkspacePath;
        if (!starter.UsesManagedWorktrees)
        {
            if (refreshBaseBranch)
            {
                throw new WorktreeException("issueDiscussionManagedWorktreeRequired", "Independent discussions require managed worktrees.");
            }

            await db.SaveChangesAsync(ct);
            return new ReviewTargetWorkspace(baseWorkspacePath, baseWorkspacePath, null, null, null);
        }

        turn.WorktreeStatus = WorktreeStatus.Preparing;
        await db.SaveChangesAsync(ct);
        var request = ConversationThreadStarter.CreateWorktreeRequest(
            turn.DiscussionTurnId, item, item.HeadSha, baseWorkspacePath);
        if (refreshBaseBranch)
        {
            var branch = item.Kind == ItemKind.LocalTask && !string.IsNullOrWhiteSpace(item.Branch)
                ? item.Branch
                : await ResolveDefaultBranchAsync(item.Repository, ct);
            request = request with { HeadSha = null, FetchBaseBranch = branch };
        }

        var result = await starter.PrepareWorktreeAsync(request, ct);
        turn.BaseWorkspacePath = result.BaseWorkspacePath;
        turn.WorktreePath = result.WorktreePath;
        turn.WorktreeStatus = WorktreeStatus.Ready;
        await db.SaveChangesAsync(ct);
        return new ReviewTargetWorkspace(result.WorktreePath, result.BaseWorkspacePath, result.WorktreeBranch, result.BaseRef, result.BaseSha);
    }

    public static string DescribeIndependentWorkspace(ReviewTargetWorkspace workspace) =>
        $"Discussion workspace:\n- Base branch: {workspace.BaseRef}\n- Commit: {workspace.BaseSha}\n- Execution path: {workspace.Path}\n";

    private async Task<string> ResolveDefaultBranchAsync(string? repository, CancellationToken ct)
    {
        string? branch;
        if (SourceProjectKey.TryParse(repository, out var key) && key.Provider == "gitlab")
        {
            branch = (await gitLab.GetProjectAsync(new GitLabProjectRef(key.ProjectPath), ct)).DefaultBranch;
        }
        else if (GitHubRepositoryRef.TryParse(repository ?? "", out var gitHubRepository))
        {
            branch = (await gitHub.GetRepositoryAsync(gitHubRepository, ct)).DefaultBranch;
        }
        else
        {
            throw new WorktreeException("issueDiscussionRepositoryInvalid", "The item does not have a valid GitHub or GitLab repository.");
        }

        return string.IsNullOrWhiteSpace(branch)
            ? throw new WorktreeException("issueDiscussionDefaultBranchMissing", "The source did not return a default branch.")
            : branch;
    }
}
