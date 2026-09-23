using System.Text.Json.Nodes;
using DotCraft.Agents;

namespace DotCraft.Context.WorldState;

internal sealed class ModeSection : IWorldStateSection
{
    public const string SectionId = "mode";

    private const string ReplacementNotice =
        "This mode guidance replaces all previously provided mode guidance.";

    private const string TransitionBody =
        "You have exited plan mode for this turn. You now have full workspace access subject to the normal approval policy.";

    public string Id => SectionId;

    public JsonNode? Snapshot(WorldStateContext context) =>
        new JsonObject
        {
            ["mode"] = Mode(context).ToString(),
            ["planActive"] = context.HasActivePlan
        };

    public string? RenderDiff(WorldStateContext context, PreviousSectionState previous)
    {
        var mode = Mode(context);
        var transition = HasPlanToAgentTransition(context, mode, previous);
        var modeLines = new List<string> { $"CurrentMode: {mode}" };
        if (transition)
            modeLines.Add("ModeTransition: PlanToAgent");
        if (context.HasActivePlan)
            modeLines.Add("Plan: Active");

        var sections = new List<string> { "## Mode\n" + string.Join("\n", modeLines) };
        if (transition)
            sections.Add("## Mode Transition\n" + TransitionBody);

        var action = BuildModeAction(mode, transition, context.HasActivePlan);
        if (previous.MayBeShown)
            action = $"{ReplacementNotice}\n\n{action}";
        sections.Add("## Mode Action\n" + action);

        return string.Join("\n\n", sections);
    }

    private static AgentMode Mode(WorldStateContext context) =>
        context.ModeManager?.CurrentMode ?? AgentMode.Agent;

    /// <summary>
    /// A stored baseline decides the transition on its own; the in-memory one-shot only answers for a
    /// thread whose baseline was never established or was reset.
    /// </summary>
    private static bool HasPlanToAgentTransition(
        WorldStateContext context,
        AgentMode mode,
        PreviousSectionState previous)
    {
        if (mode != AgentMode.Agent)
            return false;

        if (previous.TryGetKnown(out var value))
        {
            return value is JsonObject values
                && values.TryGetPropertyValue("mode", out var previousMode)
                && string.Equals(previousMode?.GetValue<string>(), nameof(AgentMode.Plan), StringComparison.Ordinal);
        }

        return context.ModeManager?.JustSwitchedFromPlan == true;
    }

    private static string BuildModeAction(AgentMode mode, bool transition, bool hasActivePlan)
    {
        if (mode == AgentMode.Plan)
        {
            return
"""
Plan mode is active. You must not edit files, run mutating commands, change configuration, commit, push, or otherwise modify the workspace. Do not call goal tools in Plan mode. Read, search, reason, and call CreatePlan when the implementation plan is ready. When a user decision is needed for the plan, use `RequestUserInput` if available instead of plain text questions.
""";
        }

        if (transition)
        {
            return
"""
Follow the active or approved plan. Before starting each planned task, update its progress when task tracking is active. After completing each task, mark it completed. Use workspace-changing tools when appropriate and continue until the planned work is complete or blocked. Do not call CreatePlan in Agent mode.
""";
        }

        if (hasActivePlan)
        {
            return
"""
Agent mode is active and a saved plan is available. Follow the plan when it applies, keep task progress current for non-trivial work, and use workspace-changing tools when appropriate under the normal approval policy. Do not call CreatePlan in Agent mode.
""";
        }

        return
"""
Agent mode is active. You may use read, write, shell, and other configured tools when appropriate under the normal approval policy. Use task tools only when the work genuinely benefits from structured tracking. Do not call CreatePlan in Agent mode.
""";
    }
}
