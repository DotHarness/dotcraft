using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// Enforces operational-mode tool restrictions without changing model-visible tool schemas.
/// </summary>
public sealed class ModeToolPolicy(AgentModeManager modeManager)
{
    /// <summary>
    /// Built-in tool names that Plan (read-only) mode hard-denies. Exposed so the tool catalog
    /// (<c>tool/list</c>, spec Section 18A) can annotate Plan-mode availability from a single source
    /// of truth. Tools that are only conditionally restricted in Plan mode (for example shell
    /// <c>Exec</c>) are intentionally not listed here.
    /// </summary>
    public static readonly IReadOnlySet<string> PlanDeniedToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "WriteFile",
        "EditFile",
        "WriteStdin",
        "imagegen",
        "UpdateTodos",
        "TodoWrite",
        GoalToolNames.GetGoal,
        GoalToolNames.CreateGoal,
        GoalToolNames.UpdateGoal
    };

    /// <summary>
    /// Evaluates whether a function call is allowed in the current mode.
    /// </summary>
    public ModeToolPolicyDecision Evaluate(FunctionInvocationContext context)
    {
        var toolName = context.Function.Name;
        if (modeManager.CurrentMode == AgentMode.Agent)
        {
            if (string.Equals(toolName, "CreatePlan", StringComparison.OrdinalIgnoreCase))
                return DenyAgentMode(toolName, "Agent mode does not allow CreatePlan.");

            return ModeToolPolicyDecision.Allow;
        }

        if (modeManager.CurrentMode != AgentMode.Plan)
            return ModeToolPolicyDecision.Allow;

        if (PlanDeniedToolNames.Contains(toolName))
            return DenyPlanMode(toolName, $"Plan mode does not allow {toolName}.");

        if (string.Equals(toolName, "Exec", StringComparison.OrdinalIgnoreCase))
        {
            var command = TryGetStringArgument(context.Arguments, "command");
            var shell = TryGetStringArgument(context.Arguments, "shell");
            if (!ReadOnlyShellClassifier.IsReadOnly(command, shell, out var reason))
                return DenyPlanMode(toolName, reason);
        }

        return ModeToolPolicyDecision.Allow;
    }

    private static ModeToolPolicyDecision DenyPlanMode(string toolName, string reason) =>
        ModeToolPolicyDecision.DenyRecoverable(
            $"""
MODE_POLICY_DENIED
Tool: {toolName}
CurrentMode: Plan
AllowedActionProfile: ReadOnlyObserve
Reason: {reason}
NextAllowedActions: Read files, search the workspace, run non-mutating observation commands, or call CreatePlan.
""");

    private static ModeToolPolicyDecision DenyAgentMode(string toolName, string reason) =>
        ModeToolPolicyDecision.DenyRecoverable(
            $"""
MODE_POLICY_DENIED
Tool: {toolName}
CurrentMode: Agent
AllowedActionProfile: FullWorkspace
Reason: {reason}
NextAllowedActions: Continue execution, or use TodoWrite/UpdateTodos to track task progress.
""");

    private static string? TryGetStringArgument(AIFunctionArguments arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value))
            return null;

        return value?.ToString();
    }
}
