using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

internal enum NativeSubAgentGuidanceChange
{
    None,
    Added,
    Replaced,
    Removed
}

internal static class NativeSubAgentGuidance
{
    private static readonly ChatRole DeveloperRole = new("developer");

    public static NativeSubAgentGuidanceChange Reconcile(
        SessionThread thread,
        IList<ChatMessage> history,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(history);

        if (thread.Source.SubAgent is not { } source
            || !string.Equals(
                source.RuntimeType,
                NativeSubAgentRuntime.RuntimeTypeName,
                StringComparison.OrdinalIgnoreCase))
        {
            return NativeSubAgentGuidanceChange.None;
        }

        var desired = !enabled || string.IsNullOrWhiteSpace(thread.Configuration?.RoleInstructions)
            ? null
            : thread.Configuration.RoleInstructions.Trim();
        var existingIndex = -1;
        for (var i = 0; i < history.Count; i++)
        {
            if (history[i].Role == DeveloperRole)
            {
                existingIndex = i;
                break;
            }
        }

        if (existingIndex < 0)
        {
            if (desired == null)
                return NativeSubAgentGuidanceChange.None;

            history.Add(new ChatMessage(DeveloperRole, desired));
            return NativeSubAgentGuidanceChange.Added;
        }

        if (desired == null)
        {
            history.RemoveAt(existingIndex);
            return NativeSubAgentGuidanceChange.Removed;
        }

        if (string.Equals(history[existingIndex].Text, desired, StringComparison.Ordinal))
            return NativeSubAgentGuidanceChange.None;

        history[existingIndex] = new ChatMessage(DeveloperRole, desired);
        return NativeSubAgentGuidanceChange.Replaced;
    }
}
