using DotCraft.Security;

namespace DotCraft.Sessions;

/// <summary>
/// Approval service that denies approval requests without prompting the user.
/// The active tool receives the denial as a normal tool result so the model can continue.
/// </summary>
internal sealed class InterruptOnApprovalService : IApprovalService
{
    private static readonly Task<bool> Denied = Task.FromResult(false);

    public Task<bool> RequestFileApprovalAsync(
        string operation,
        string path,
        ApprovalContext? context = null) =>
        Denied;

    public Task<bool> RequestShellApprovalAsync(
        string command,
        string? workingDir,
        ApprovalContext? context = null) =>
        Denied;

    public Task<bool> RequestResourceApprovalAsync(
        string kind,
        string operation,
        string target,
        ApprovalContext? context = null) =>
        Denied;
}
