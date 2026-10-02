namespace DotCraft.Tools;

internal static class ToolInvocationScope
{
    private static readonly AsyncLocal<ToolInvocationContext?> Value = new();

    public static ToolInvocationContext? Current => Value.Value;

    public static IDisposable Enter(ToolInvocationContext context)
    {
        var previous = Value.Value;
        Value.Value = context;
        return new Handle(previous);
    }

    private sealed class Handle(ToolInvocationContext? previous) : IDisposable
    {
        public void Dispose() => Value.Value = previous;
    }
}
