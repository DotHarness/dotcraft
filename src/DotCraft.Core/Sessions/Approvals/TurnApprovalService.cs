using DotCraft.Security;
using DotCraft.Security.ShellCommands;

namespace DotCraft.Sessions;

/// <summary>Follows the thread's approval policy as it is when each request is made.</summary>
internal sealed class TurnApprovalService(
    Func<ApprovalPolicy> resolvePolicy,
    SessionApprovalService interactive) : IApprovalService, IApprovalServiceDecorator
{
    private static readonly AutoApproveApprovalService AutoApprove = new();
    private static readonly DenyApprovalService Deny = new();

    private IApprovalService Current => resolvePolicy() switch
    {
        ApprovalPolicy.AutoApprove => AutoApprove,
        ApprovalPolicy.Deny => Deny,
        _ => interactive
    };

    IApprovalService IApprovalServiceDecorator.GetInnerApprovalService(ApprovalContext? context) => Current;

    public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null) =>
        Current.RequestFileApprovalAsync(operation, path, context);

    public Task<bool> RequestShellApprovalAsync(ShellApprovalRequest request, ApprovalContext? context = null) =>
        Current.RequestShellApprovalAsync(request, context);

    public Task<bool> RequestResourceApprovalAsync(
        string kind,
        string operation,
        string target,
        ApprovalContext? context = null) =>
        Current.RequestResourceApprovalAsync(kind, operation, target, context);

    public Task<bool> RequestResourceApprovalAsync(ResourceApprovalRequest request, ApprovalContext? context = null) =>
        Current.RequestResourceApprovalAsync(request, context);
}
