namespace DotCraft.Security;

/// <summary>
/// Approval service that grants all requests unconditionally.
/// Used for programmatic/trusted channels (e.g. API) where no interactive approver is available.
/// </summary>
public sealed class AutoApproveApprovalService : IApprovalService
{
    public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null)
        => Task.FromResult(true);

    public Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null)
        => Task.FromResult(true);

    public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null)
        => Task.FromResult(true);
}
