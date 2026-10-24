using DotCraft.Tools;

namespace DotCraft.Core.Tests.Tools;

internal static class TurnToolInvocation
{
    public static ToolInvocationContext For(TurnToolServices? services) => new(
        "thread",
        "turn",
        "call",
        ToolInvocationAudience.Model,
        default,
        default,
        default,
        1,
        DateTimeOffset.UtcNow,
        TurnServices: services);
}
