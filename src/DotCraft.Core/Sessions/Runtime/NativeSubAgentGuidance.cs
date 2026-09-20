using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

internal enum NativeSubAgentGuidanceChange
{
    None,
    Added,
    Replaced
}

/// <summary>
/// Materializes native SubAgent role guidance as a thread context item. The child's generated base
/// instructions stay identical to its parent's, so the pair can share a provider-side cache prefix.
/// </summary>
internal static class NativeSubAgentGuidance
{
    private static readonly ChatRole DeveloperRole = new("developer");

    private const string RoleFraming =
        """
        ## SubAgent Context

        You are a SubAgent. Complete the assigned task and return concise, concrete results to the parent agent, who owns the final synthesis.

        Rules:
        - Stay within the assigned task and role.
        - If a listed tool is denied for your role, follow the returned reason and do not retry the call.
        - Final response should summarize findings, actions, changed files if any, and validation performed.
        """;

    public static NativeSubAgentGuidanceChange Reconcile(
        SessionThread thread,
        IList<ChatMessage> history,
        ThreadContextCarrier carrier,
        IEnumerable<ISubAgentGuidanceProvider>? providers = null)
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

        var desired = BuildGuidance(thread, providers);
        var existingIndex = FindExistingIndex(history);

        if (existingIndex < 0)
        {
            history.Add(ThreadContextItems.Create(carrier, ThreadContextItems.SubAgentRoleKind, desired));
            return NativeSubAgentGuidanceChange.Added;
        }

        if (string.Equals(ThreadContextItems.ReadText(history[existingIndex]), desired, StringComparison.Ordinal))
            return NativeSubAgentGuidanceChange.None;

        history[existingIndex] = ThreadContextItems.Create(
            carrier,
            ThreadContextItems.SubAgentRoleKind,
            desired);
        return NativeSubAgentGuidanceChange.Replaced;
    }

    private static string BuildGuidance(
        SessionThread thread,
        IEnumerable<ISubAgentGuidanceProvider>? providers)
    {
        var roleInstructions = thread.Configuration?.RoleInstructions?.Trim();
        var guidance = string.IsNullOrWhiteSpace(roleInstructions)
            ? RoleFraming
            : $"{RoleFraming}\n\n## Role Instructions\n\n{roleInstructions}";
        if (providers == null)
            return guidance;

        foreach (var provider in providers)
        {
            var additional = provider.GetGuidance(thread)?.Trim();
            if (!string.IsNullOrWhiteSpace(additional))
                guidance += $"\n\n{additional}";
        }
        return guidance;
    }

    private static int FindExistingIndex(IList<ChatMessage> history)
    {
        for (var i = 0; i < history.Count; i++)
        {
            if (ThreadContextItems.IsKind(history[i], ThreadContextItems.SubAgentRoleKind))
                return i;
        }

        // Threads persisted before context items carried a kind marker stored role guidance as the
        // first unmarked developer message.
        for (var i = 0; i < history.Count; i++)
        {
            if (history[i].Role == DeveloperRole && ThreadContextItems.GetKind(history[i]) == null)
                return i;
        }

        return -1;
    }
}
