namespace DotCraft.Security;

/// <summary>
/// Decorates an <see cref="IApprovalService"/> and prefixes shell/file targets so users can
/// tell approvals were triggered by a subagent.
/// </summary>
public sealed class PrefixedApprovalService(IApprovalService inner, string prefix) : IApprovalService
{
    private readonly string _prefix = string.IsNullOrWhiteSpace(prefix) ? string.Empty : prefix;

    public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null)
        => inner.RequestFileApprovalAsync(operation, Prefix(path), context);

    public Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null)
        => inner.RequestShellApprovalAsync(Prefix(command), workingDir, context);

    public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null)
        => inner.RequestResourceApprovalAsync(kind, operation, Prefix(target), context);

    private string Prefix(string value)
        => string.IsNullOrEmpty(_prefix) || string.IsNullOrEmpty(value)
            ? value
            : _prefix + value;
}
