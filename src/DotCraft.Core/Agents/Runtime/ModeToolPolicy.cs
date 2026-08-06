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
            if (!PlanModeShellClassifier.IsReadOnly(command, shell, out var reason))
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

internal static class PlanModeShellClassifier
{
    private static readonly HashSet<string> PowerShellReadOnlyCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "Get-ChildItem",
        "gci",
        "dir",
        "ls",
        "Get-Content",
        "gc",
        "cat",
        "type",
        "Select-String",
        "sls",
        "Measure-Object",
        "measure",
        "Resolve-Path",
        "Test-Path"
    };

    private static readonly HashSet<string> UnixReadOnlyCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "ls",
        "find",
        "grep",
        "rg"
    };

    public static bool IsReadOnly(string? command, string? shell, out string reason)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            reason = "Plan mode shell calls require a command that can be classified as read-only.";
            return false;
        }

        if (ContainsAmbiguousShellSyntax(command))
        {
            reason = "Plan mode allows only simple read-only shell commands; command chains, pipes, redirection, and substitution are denied.";
            return false;
        }

        var tokens = Tokenize(command);
        if (tokens.Count == 0)
        {
            reason = "Plan mode shell calls require a command that can be classified as read-only.";
            return false;
        }

        var executable = tokens[0];
        if (string.Equals(executable, "git", StringComparison.OrdinalIgnoreCase))
            return IsReadOnlyGit(tokens, out reason);

        if (IsPowerShellShell(shell) || PowerShellReadOnlyCommands.Contains(executable))
        {
            if (PowerShellReadOnlyCommands.Contains(executable))
            {
                reason = "";
                return true;
            }
        }

        if (UnixReadOnlyCommands.Contains(executable))
        {
            reason = "";
            return true;
        }

        if (string.Equals(executable, "sed", StringComparison.OrdinalIgnoreCase)
            && tokens.Skip(1).Any(t => string.Equals(t, "-n", StringComparison.Ordinal)))
        {
            reason = "";
            return true;
        }

        reason = $"Plan mode denied shell command '{executable}' because it is not in the read-only allow list.";
        return false;
    }

    private static bool IsReadOnlyGit(IReadOnlyList<string> tokens, out string reason)
    {
        if (tokens.Count < 2)
        {
            reason = "Plan mode allows only explicit read-only git subcommands.";
            return false;
        }

        var subcommand = tokens[1];
        if (subcommand is "status" or "diff" or "log" or "show")
        {
            reason = "";
            return true;
        }

        if (subcommand == "branch"
            && tokens.Count == 3
            && string.Equals(tokens[2], "--show-current", StringComparison.Ordinal))
        {
            reason = "";
            return true;
        }

        reason = $"Plan mode denied git {subcommand} because only read-only git subcommands are allowed.";
        return false;
    }

    private static bool IsPowerShellShell(string? shell) =>
        !string.IsNullOrWhiteSpace(shell)
        && (shell.Contains("powershell", StringComparison.OrdinalIgnoreCase)
            || shell.Contains("pwsh", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAmbiguousShellSyntax(string command)
    {
        var inSingle = false;
        var inDouble = false;
        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];
            if (c == '\'' && !inDouble)
                inSingle = !inSingle;
            else if (c == '"' && !inSingle)
                inDouble = !inDouble;
            else if (!inSingle && !inDouble)
            {
                if (c is '|' or ';' or '<' or '>')
                    return true;
                if (c == '&')
                    return true;
                if (c == '`')
                    return true;
                if (c == '$' && i + 1 < command.Length && command[i + 1] == '(')
                    return true;
            }
        }

        return inSingle || inDouble;
    }

    private static List<string> Tokenize(string command)
    {
        var tokens = new List<string>();
        var current = new List<char>();
        var inSingle = false;
        var inDouble = false;

        foreach (var c in command)
        {
            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
                continue;
            }

            if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inSingle && !inDouble)
            {
                Flush();
                continue;
            }

            current.Add(c);
        }

        Flush();
        return tokens;

        void Flush()
        {
            if (current.Count == 0)
                return;
            tokens.Add(new string(current.ToArray()));
            current.Clear();
        }
    }
}
