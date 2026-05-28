using DotCraft.Security;

namespace DotCraft.Protocol;

/// <summary>
/// An IApprovalService wrapper that supports Turn-scoped overrides via AsyncLocal.
/// Injected into AgentFactory so that all tools call this service.
/// When SessionService runs a Turn, it calls SetOverride() to redirect approval
/// requests through the Turn's SessionApprovalService.
/// When no override is set (legacy code paths), requests delegate to the inner service.
/// </summary>
public sealed class SessionScopedApprovalService(IApprovalService inner) : IApprovalService
{
    private static readonly AsyncLocal<IApprovalService?> Override = new();
    private readonly IApprovalService _inner = inner;

    /// <summary>
    /// Sets the Turn-scoped approval service for the current async context.
    /// Dispose the returned handle to clear the override.
    /// </summary>
    public static IDisposable SetOverride(IApprovalService service)
    {
        Override.Value = service;
        return new OverrideScope();
    }

    public Task<bool> RequestFileApprovalAsync(
        string operation,
        string path,
        ApprovalContext? context = null) =>
        GetEffectiveService().RequestFileApprovalAsync(operation, path, context);

    public Task<bool> RequestShellApprovalAsync(
        string command,
        string? workingDir,
        ApprovalContext? context = null) =>
        GetEffectiveService().RequestShellApprovalAsync(command, workingDir, context);

    public Task<bool> RequestResourceApprovalAsync(
        string kind,
        string operation,
        string target,
        ApprovalContext? context = null) =>
        GetEffectiveService().RequestResourceApprovalAsync(kind, operation, target, context);

    internal IApprovalService GetEffectiveService()
        => Override.Value ?? _inner;

    private sealed class OverrideScope : IDisposable
    {
        public void Dispose() => Override.Value = null;
    }
}
