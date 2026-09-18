using DotCraft.Security.ShellCommands;

namespace DotCraft.Security;

public sealed class PrefixedApprovalService(IApprovalService inner, string prefix)
    : IApprovalService, IApprovalServiceDecorator
{
    private readonly string _prefix = string.IsNullOrWhiteSpace(prefix) ? string.Empty : prefix;

    IApprovalService IApprovalServiceDecorator.GetInnerApprovalService(ApprovalContext? context) => inner;

    public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null)
        => inner.RequestFileApprovalAsync(operation, Prefix(path), context);

    public Task<bool> RequestShellApprovalAsync(ShellApprovalRequest request, ApprovalContext? context = null)
        => inner.RequestShellApprovalAsync(
            string.IsNullOrEmpty(_prefix) ? request : request with { Label = _prefix.Trim() },
            context);

    public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null)
        => inner.RequestResourceApprovalAsync(kind, operation, Prefix(target), context);

    private string Prefix(string value)
        => string.IsNullOrEmpty(_prefix) || string.IsNullOrEmpty(value)
            ? value
            : _prefix + value;
}
