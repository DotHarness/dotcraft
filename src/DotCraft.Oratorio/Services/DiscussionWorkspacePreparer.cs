using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.GitLab;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Sources;
using Microsoft.EntityFrameworkCore;

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

    public async Task ClaimImplementationAsync(
        OratorioDiscussionTurn turn, string threadId, string contextJson, CancellationToken ct)
    {
        if (!ConversationThreadContext.UsesManagedWorktree(contextJson, out var baseWorkspacePath))
            return;

        var run = await db.Runs.AsNoTracking()
            .Where(x => x.ItemId == turn.ItemId && x.ThreadId == threadId &&
                x.Purpose == RunPurpose.Implementation && x.WorktreePath != null)
            .OrderByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.Attempt)
            .FirstOrDefaultAsync(ct);
        if (run is not { WorktreePath: not null, WorktreeBranch: not null, BaseRef: not null, BaseSha: not null })
            throw new WorktreeException("implementationWorktreeMissing", "The implementation worktree is no longer available.");

        turn.BaseWorkspacePath = baseWorkspacePath;
        turn.WorktreePath = run.WorktreePath;
        turn.WorktreeStatus = WorktreeStatus.Preparing;
        await db.SaveChangesAsync(ct);
        var request = ConversationThreadStarter.CreateWorktreeRequest(
            turn.DiscussionTurnId, turn.Item!, null, baseWorkspacePath) with
        {
            ResumeFrom = new WorktreePrepareResult(baseWorkspacePath, run.WorktreePath,
                run.WorktreeBranch, run.BaseRef, run.BaseSha, Path.GetDirectoryName(run.WorktreePath)!),
            RequireExisting = true
        };
        await starter.PrepareWorktreeAsync(request, ct);
        turn.WorktreeStatus = WorktreeStatus.Ready;
        await db.SaveChangesAsync(ct);
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
