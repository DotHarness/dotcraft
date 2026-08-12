using DotCraft.Security;
using DotCraft.Sessions;

namespace DotCraft.Tools;

/// <summary>
/// Source-neutral access to the active Session turn for module-owned tool runtimes.
/// </summary>
public sealed record ToolHostExecutionContext(
    string ThreadId,
    string TurnId,
    string WorkspacePath,
    IApprovalService ApprovalService,
    ISessionService SessionService);

public static class ToolHostExecutionScope
{
    private static readonly AsyncLocal<ToolHostExecutionContext?> Value = new();

    public static ToolHostExecutionContext? Current => Value.Value;

    public static IDisposable Set(ToolHostExecutionContext context)
    {
        var previous = Value.Value;
        Value.Value = context;
        return new Handle(previous);
    }

    private sealed class Handle(ToolHostExecutionContext? previous) : IDisposable
    {
        public void Dispose() => Value.Value = previous;
    }
}
