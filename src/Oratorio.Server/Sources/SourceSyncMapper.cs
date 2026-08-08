using Oratorio.Server.Api;
using Oratorio.Server.Domain;

namespace Oratorio.Server.Sources;

/// <summary>
/// Projects provider-specific sync jobs into the source-neutral sync contract.
/// </summary>
public static class SourceSyncMapper
{
    public static SourceSyncJobDto FromGitHub(GitHubSyncJobDto job, string? endpoint)
    {
        var projects = job.Repositories.Select(run => FromGitHub(run, endpoint)).ToArray();
        return new SourceSyncJobDto(
            job.JobId,
            "github",
            MapTrigger(job.Trigger),
            MapMode(job.Mode),
            MapStatus(job.Status),
            job.RepositoriesTotal,
            job.RepositoriesCompleted,
            job.RepositoriesFailed,
            job.IssuesImported,
            job.PullRequestsImported,
            job.CommentsImported,
            job.Skipped,
            job.ErrorCode,
            job.ErrorMessage,
            job.CreatedAt,
            job.UpdatedAt,
            job.StartedAt,
            job.CompletedAt,
            projects);
    }

    public static SourceSyncProjectRunDto FromGitHub(GitHubSyncRepositoryRunDto run, string? endpoint)
    {
        var key = SourceProjectKey.FromGitHubRepository(run.Repository, endpoint);
        return new SourceSyncProjectRunDto(
            run.RepositoryRunId,
            run.JobId,
            "github",
            key.Key,
            key.ProjectPath,
            run.Repository,
            MapStatus(run.Status),
            MapPhase(run.Phase),
            run.IssuesDiscovered,
            run.PullRequestsDiscovered,
            run.IssuesImported,
            run.PullRequestsImported,
            run.CommentsImported,
            run.Skipped,
            run.ErrorCode,
            run.ErrorMessage,
            run.CreatedAt,
            run.UpdatedAt,
            run.StartedAt,
            run.CompletedAt);
    }

    public static GitHubSyncMode ToGitHubMode(SourceSyncMode mode) =>
        mode switch
        {
            SourceSyncMode.Full => GitHubSyncMode.Full,
            _ => GitHubSyncMode.Incremental
        };

    private static SourceSyncTrigger MapTrigger(GitHubSyncTrigger trigger) =>
        trigger switch
        {
            GitHubSyncTrigger.Webhook => SourceSyncTrigger.Webhook,
            GitHubSyncTrigger.Scheduled => SourceSyncTrigger.Scheduled,
            _ => SourceSyncTrigger.Manual
        };

    private static SourceSyncMode MapMode(GitHubSyncMode mode) =>
        mode switch
        {
            GitHubSyncMode.Full => SourceSyncMode.Full,
            _ => SourceSyncMode.Incremental
        };

    private static SourceSyncStatus MapStatus(GitHubSyncStatus status) =>
        status switch
        {
            GitHubSyncStatus.Queued => SourceSyncStatus.Queued,
            GitHubSyncStatus.Running => SourceSyncStatus.Running,
            GitHubSyncStatus.Succeeded => SourceSyncStatus.Succeeded,
            GitHubSyncStatus.PartialFailed => SourceSyncStatus.PartialFailed,
            GitHubSyncStatus.Failed => SourceSyncStatus.Failed,
            _ => SourceSyncStatus.Failed
        };

    private static SourceSyncProjectStatus MapStatus(GitHubSyncRepositoryStatus status) =>
        status switch
        {
            GitHubSyncRepositoryStatus.Queued => SourceSyncProjectStatus.Queued,
            GitHubSyncRepositoryStatus.Running => SourceSyncProjectStatus.Running,
            GitHubSyncRepositoryStatus.Succeeded => SourceSyncProjectStatus.Succeeded,
            GitHubSyncRepositoryStatus.Failed => SourceSyncProjectStatus.Failed,
            _ => SourceSyncProjectStatus.Failed
        };

    private static SourceSyncProjectPhase MapPhase(GitHubSyncRepositoryPhase phase) =>
        phase switch
        {
            GitHubSyncRepositoryPhase.Queued => SourceSyncProjectPhase.Queued,
            GitHubSyncRepositoryPhase.Fetching => SourceSyncProjectPhase.Fetching,
            GitHubSyncRepositoryPhase.Importing => SourceSyncProjectPhase.Importing,
            GitHubSyncRepositoryPhase.Done => SourceSyncProjectPhase.Done,
            GitHubSyncRepositoryPhase.Failed => SourceSyncProjectPhase.Failed,
            _ => SourceSyncProjectPhase.Failed
        };
}
