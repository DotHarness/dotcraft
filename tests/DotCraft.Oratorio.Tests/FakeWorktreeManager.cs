using System.Security.Cryptography;
using System.Text;
using DotCraft.Oratorio.Integrations;

namespace DotCraft.Oratorio.Tests;

internal sealed class FakeWorktreeManager : IWorktreeManager
{
    public List<WorktreePrepareRequest> PrepareRequests { get; } = [];
    public List<WorktreeCleanupRequest> CleanupRequests { get; } = [];
    public WorktreeException? Failure { get; set; }
    public string? BaseShaOverride { get; set; }

    public Task<WorktreePrepareResult> PrepareAsync(WorktreePrepareRequest request, CancellationToken ct)
    {
        PrepareRequests.Add(request);
        if (Failure is not null)
        {
            throw Failure;
        }

        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{request.Source}:{request.ExternalId}:{request.ItemId}")))[..10].ToLowerInvariant();
        var worktreePath = Path.Combine(request.BaseWorkspacePath, ".craft", "oratorio", "worktrees", key);
        var branch = $"oratorio/run/{key}";
        var baseRef = request.FetchBaseBranch ?? (string.IsNullOrWhiteSpace(request.HeadSha)
            ? string.IsNullOrWhiteSpace(request.SourceBranch) ? "HEAD" : request.SourceBranch
            : request.HeadSha);
        return Task.FromResult(new WorktreePrepareResult(
            request.BaseWorkspacePath,
            worktreePath,
            branch,
            baseRef,
            BaseShaOverride ?? (string.IsNullOrWhiteSpace(request.HeadSha) ? "test-base-sha" : request.HeadSha),
            Path.Combine(request.BaseWorkspacePath, ".craft", "oratorio", "worktrees")));
    }

    public Task CleanupAsync(WorktreeCleanupRequest request, CancellationToken ct)
    {
        CleanupRequests.Add(request);
        return Task.CompletedTask;
    }
}
