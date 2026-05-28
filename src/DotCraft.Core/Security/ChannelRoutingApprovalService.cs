namespace DotCraft.Security;

/// <summary>
/// Routes approval requests to the channel-specific approval service that matches the
/// ApprovalContext.Source. Falls back to a default service when no match is found or
/// when context is null (e.g. heartbeat tasks without a creator channel).
/// </summary>
public sealed class ChannelRoutingApprovalService(
    IReadOnlyDictionary<string, IApprovalService> channelServices,
    IApprovalService fallback) : IApprovalService
{
    public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null)
        => Resolve(context).RequestFileApprovalAsync(operation, path, context);

    public Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null)
        => Resolve(context).RequestShellApprovalAsync(command, workingDir, context);

    public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null)
        => Resolve(context).RequestResourceApprovalAsync(kind, operation, target, context);

    internal IApprovalService ResolveForContext(ApprovalContext? context)
        => context != null && channelServices.TryGetValue(context.Source, out var svc) ? svc : fallback;

    private IApprovalService Resolve(ApprovalContext? context)
        => ResolveForContext(context);
}
