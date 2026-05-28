using DotCraft.Protocol;
using DotCraft.Security;

namespace DotCraft.Plugins;

/// <summary>
/// Captures active turn context required by plugin function wrappers.
/// </summary>
public sealed class PluginFunctionExecutionContext
{
    public required string ThreadId { get; init; }

    public required string TurnId { get; init; }

    public required string OriginChannel { get; init; }

    public string? ChannelContext { get; init; }

    public string? SenderId { get; init; }

    public string? GroupId { get; init; }

    public required string WorkspacePath { get; init; }

    public required bool RequireApprovalOutsideWorkspace { get; init; }

    public required IApprovalService ApprovalService { get; init; }

    public PathBlacklist? PathBlacklist { get; init; }

    public required SessionTurn Turn { get; init; }

    public required Func<int> NextItemSequence { get; init; }

    public required Action<SessionItem> EmitItemStarted { get; init; }

    public required Action<SessionItem> EmitItemCompleted { get; init; }

    public ISessionService? SessionService { get; init; }
}

/// <summary>
/// Async-local holder for the active plugin function execution context.
/// </summary>
public static class PluginFunctionExecutionScope
{
    private static readonly AsyncLocal<PluginFunctionExecutionContext?> CurrentContext = new();

    public static PluginFunctionExecutionContext? Current => CurrentContext.Value;

    public static IDisposable Set(PluginFunctionExecutionContext context)
    {
        CurrentContext.Value = context;
        return new ScopeHandle();
    }

    private sealed class ScopeHandle : IDisposable
    {
        public void Dispose() => CurrentContext.Value = null;
    }
}
