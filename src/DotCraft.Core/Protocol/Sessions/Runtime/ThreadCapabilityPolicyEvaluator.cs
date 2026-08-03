using DotCraft.Agents;
using DotCraft.Protocol.AppServer;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using System.Text.Json.Nodes;

namespace DotCraft.Sessions;

/// <summary>
/// Evaluates profile-shaped thread capability policy for tool discovery and invocation.
/// </summary>
internal sealed class ThreadCapabilityPolicyEvaluator(ThreadConfiguration config, AgentRuntimeContext context)
{
    private const string PolicyDeniedCode = "PROFILE_TOOL_POLICY_DENIED";
    private const string TeamsChannelName = "teams";
    private const string SkillViewToolName = "SkillView";
    private const string SkillManageToolName = "SkillManage";

    private static readonly HashSet<string> TeamsReservedToolNames = new(StringComparer.Ordinal)
    {
        "CreateMissionPlan",
        "AssignTask",
        "ListTeamMembers",
        "ReadMissionState",
        "ReadMemberStatus",
        "SendMessage",
        "ReportProgress",
        "PublishArtifact",
        "MarkTaskDone",
        "MarkMissionDone"
    };

    /// <summary>
    /// Returns true when a tool may be exposed to the model for this thread.
    /// </summary>
    public bool AllowsTool(AITool tool) =>
        AllowsTool(tool, out _);

    /// <summary>Returns true when source-qualified policy permits model exposure.</summary>
    public bool AllowsRegistrationExposure(ToolRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        return registration.Definition.Id.Kind != ToolSourceKind.Mcp
               || AllowsMcpRegistration(registration, out _);
    }

    /// <summary>
    /// Evaluates a model tool call before the concrete function is resolved.
    /// This catches stale calls to tools that were hidden by policy.
    /// </summary>
    public ModeToolPolicyDecision EvaluateCall(FunctionCallContent call)
    {
        var toolName = call.Name;
        if (!AllowsToolName(toolName, isRuntimeReserved: IsRuntimeReservedToolName(toolName) || IsTeamsReservedToolName(toolName), out var reason))
            return Deny(toolName, reason);

        var arguments = new AIFunctionArguments(call.Arguments);
        if (!AllowsSkillInvocation(toolName, arguments, out reason))
            return Deny(toolName, reason);

        return ModeToolPolicyDecision.Allow;
    }

    /// <summary>
    /// Evaluates a concrete function invocation after the runtime has resolved the function.
    /// </summary>
    public ModeToolPolicyDecision EvaluateInvocation(FunctionInvocationContext invocation)
    {
        if (!AllowsTool(invocation.Function, out var reason))
            return Deny(invocation.Function.Name, reason);

        if (!AllowsSkillInvocation(invocation.Function.Name, invocation.Arguments, out reason))
            return Deny(invocation.Function.Name, reason);

        return ModeToolPolicyDecision.Allow;
    }

    /// <summary>Evaluates the source-qualified registration at the common dispatcher boundary.</summary>
    public ToolDispatchDecision EvaluateRegistration(ToolRegistration registration, JsonObject arguments)
    {
        var name = registration.Definition.Name.Name;
        if (string.Equals(config.Mode, "plan", StringComparison.OrdinalIgnoreCase))
        {
            if (ModeToolPolicy.PlanDeniedToolNames.Contains(name))
                return ToolDispatchDecision.Deny(ToolErrorCodes.Unauthorized, $"Plan mode does not allow {name}.");
            if (string.Equals(name, "Exec", StringComparison.OrdinalIgnoreCase)
                && !PlanModeShellClassifier.IsReadOnly(
                    arguments["command"]?.GetValue<string>(),
                    arguments["shell"]?.GetValue<string>(),
                    out var shellReason))
            {
                return ToolDispatchDecision.Deny(ToolErrorCodes.Unauthorized, shellReason);
            }
        }
        else if (string.Equals(config.Mode, "agent", StringComparison.OrdinalIgnoreCase)
                 && string.Equals(name, "CreatePlan", StringComparison.OrdinalIgnoreCase))
        {
            return ToolDispatchDecision.Deny(ToolErrorCodes.Unauthorized, "Agent mode does not allow CreatePlan.");
        }

        var reserved = IsTeamsReservedToolName(name) || IsRuntimeReservedToolName(name);
        if (!AllowsToolName(name, reserved, out var reason))
            return ToolDispatchDecision.Deny(ToolErrorCodes.Unauthorized, reason);

        if (registration.Definition.Id.Kind == ToolSourceKind.Mcp && config.McpPolicy is not null)
        {
            if (!AllowsMcpRegistration(registration, out var mcpReason))
                return ToolDispatchDecision.Deny(ToolErrorCodes.Unauthorized, mcpReason);
        }

        if (registration.Definition.Id.Kind == ToolSourceKind.PluginNative
            && config.PluginPolicy is { } plugin)
        {
            var source = registration.Definition.Provenance.SourceId;
            if (MatchesAny(source, plugin.Deny, allowWildcards: false)
                || MatchesAny(name, plugin.Deny, allowWildcards: true)
                || MatchesAny(registration.Definition.Name.ToString(), plugin.Deny, allowWildcards: true))
                return ToolDispatchDecision.Deny(ToolErrorCodes.Unauthorized, "The thread plugin policy denies this plugin, app, or tool.");
            if (plugin.Allow != null && !MatchesAny(source, plugin.Allow, allowWildcards: false))
                return ToolDispatchDecision.Deny(ToolErrorCodes.Unauthorized, "The thread plugin policy does not allow this plugin or app.");
        }

        var invocationArguments = new AIFunctionArguments(arguments.ToDictionary(
            static pair => pair.Key,
            static pair => (object?)pair.Value,
            StringComparer.Ordinal));
        if (!AllowsSkillInvocation(name, invocationArguments, out reason))
            return ToolDispatchDecision.Deny(ToolErrorCodes.Unauthorized, reason);

        return ToolDispatchDecision.Allow;
    }

    private bool AllowsTool(AITool tool, out string reason)
    {
        var toolName = tool.Name;
        var isRuntimeReserved = IsRuntimeReservedToolName(toolName);

        if (IsTeamsReservedTool(tool))
        {
            reason = string.Empty;
            return true;
        }

        if (!AllowsToolName(toolName, isRuntimeReserved, out reason))
            return false;

        if (!AllowsMcpTool(tool, out reason))
            return false;

        if (!AllowsPluginOrAppTool(tool, out reason))
            return false;

        if (!AllowsSkillDiscovery(toolName, out reason))
            return false;

        reason = string.Empty;
        return true;
    }

    private bool AllowsToolName(string toolName, bool isRuntimeReserved, out string reason)
    {
        if (MatchesAny(toolName, config.ToolPolicy?.Deny, allowWildcards: false))
        {
            reason = "The thread tool policy denies this tool.";
            return false;
        }

        if (MatchesAny(toolName, config.ToolDenyList, allowWildcards: false))
        {
            reason = "The thread legacy tool deny-list denies this tool.";
            return false;
        }

        if (!isRuntimeReserved
            && config.ToolPolicy?.Allow != null
            && !MatchesAny(toolName, config.ToolPolicy.Allow, allowWildcards: false))
        {
            reason = "The thread tool policy does not allow this tool.";
            return false;
        }

        if (!isRuntimeReserved
            && HasLegacyAllowList(config.ToolAllowList)
            && !MatchesAny(toolName, config.ToolAllowList, allowWildcards: false))
        {
            reason = "The thread legacy tool allow-list does not allow this tool.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool AllowsMcpTool(AITool tool, out string reason)
    {
        var policy = config.McpPolicy;
        if (policy == null)
        {
            reason = string.Empty;
            return true;
        }

        var toolName = tool.Name;
        var selector = CanonicalToolIdentityMetadataResolver.TryGet(tool, out var canonicalName, out _)
            ? ToCanonicalSelector(canonicalName)
            : toolName;
        var serverName = ResolveMcpServerName(toolName);
        var isKnownMcpTool = !string.IsNullOrWhiteSpace(serverName)
                             || toolName.StartsWith("mcp__", StringComparison.Ordinal);

        if (!string.IsNullOrWhiteSpace(serverName))
        {
            if (policy.Servers != null && !MatchesAny(serverName!, policy.Servers, allowWildcards: false))
            {
                reason = "The thread MCP policy does not allow this MCP server.";
                return false;
            }
        }

        if (MatchesAny(selector, policy.Tools?.Deny, allowWildcards: true))
        {
            reason = "The thread MCP policy denies this MCP tool.";
            return false;
        }

        if (isKnownMcpTool
            && policy.Tools?.Allow != null
            && !MatchesAny(selector, policy.Tools.Allow, allowWildcards: true))
        {
            reason = "The thread MCP policy does not allow this MCP tool.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool AllowsPluginOrAppTool(AITool tool, out string reason)
    {
        var policy = config.PluginPolicy;
        if (policy == null)
        {
            reason = string.Empty;
            return true;
        }

        var toolName = tool.Name;
        var sourceId = ResolvePluginOrAppSourceId(tool);
        if (!string.IsNullOrWhiteSpace(sourceId))
        {
            if (MatchesAny(sourceId!, policy.Deny, allowWildcards: false))
            {
                reason = "The thread plugin policy denies this plugin or app.";
                return false;
            }

            if (policy.Allow != null && !MatchesAny(sourceId!, policy.Allow, allowWildcards: false))
            {
                reason = "The thread plugin policy does not allow this plugin or app.";
                return false;
            }
        }

        if (MatchesAny(toolName, policy.Deny, allowWildcards: true))
        {
            reason = "The thread plugin policy denies this tool name.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool AllowsSkillDiscovery(string toolName, out string reason)
    {
        var policy = config.SkillsPolicy;
        if (policy == null)
        {
            reason = string.Empty;
            return true;
        }

        if (string.Equals(toolName, SkillManageToolName, StringComparison.Ordinal)
            && policy.AllowManage == false)
        {
            reason = "The thread skills policy disables skill management.";
            return false;
        }

        if (string.Equals(toolName, SkillViewToolName, StringComparison.Ordinal)
            && policy.Allow is { Length: 0 })
        {
            reason = "The thread skills policy allows no skills to be read.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool AllowsSkillInvocation(string toolName, AIFunctionArguments arguments, out string reason)
    {
        var policy = config.SkillsPolicy;
        if (policy == null)
        {
            reason = string.Empty;
            return true;
        }

        if (string.Equals(toolName, SkillManageToolName, StringComparison.Ordinal))
        {
            if (policy.AllowManage == false)
            {
                reason = "The thread skills policy disables skill management.";
                return false;
            }

            var managedSkillName = TryGetStringArgument(arguments, "name");
            if (!string.IsNullOrWhiteSpace(managedSkillName)
                && !AllowsSkillName(managedSkillName!, policy, out reason))
            {
                return false;
            }
        }

        if (string.Equals(toolName, SkillViewToolName, StringComparison.Ordinal))
        {
            var skillName = TryGetStringArgument(arguments, "name");
            if (string.IsNullOrWhiteSpace(skillName))
            {
                reason = string.Empty;
                return true;
            }

            return AllowsSkillName(skillName!, policy, out reason);
        }

        reason = string.Empty;
        return true;
    }

    private static bool AllowsSkillName(string skillName, ThreadSkillsPolicy policy, out string reason)
    {
        if (MatchesAny(skillName, policy.Deny, allowWildcards: false))
        {
            reason = "The thread skills policy denies this skill.";
            return false;
        }

        if (policy.Allow != null && !MatchesAny(skillName, policy.Allow, allowWildcards: false))
        {
            reason = "The thread skills policy does not allow this skill.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool IsTeamsReservedTool(AITool tool) =>
        string.Equals(config.TeamsPolicy?.ReservedTools, "keep", StringComparison.OrdinalIgnoreCase)
        && string.Equals(context.CurrentOriginChannel, TeamsChannelName, StringComparison.OrdinalIgnoreCase)
        && TeamsReservedToolNames.Contains(tool.Name);

    private bool IsTeamsReservedToolName(string toolName) =>
        string.Equals(config.TeamsPolicy?.ReservedTools, "keep", StringComparison.OrdinalIgnoreCase)
        && string.Equals(context.CurrentOriginChannel, TeamsChannelName, StringComparison.OrdinalIgnoreCase)
        && TeamsReservedToolNames.Contains(toolName);

    private string? ResolveMcpServerName(string toolName)
    {
        if (context.McpClientManager?.ToolServerMap.TryGetValue(toolName, out var serverName) == true)
            return serverName;

        return null;
    }

    private static string? ResolvePluginOrAppSourceId(AITool tool)
        => null;

    private static bool IsRuntimeReservedToolName(string toolName) =>
        string.Equals(toolName, NativeToolSearchTool.ToolName, StringComparison.Ordinal);

    private static bool HasLegacyAllowList(string[]? values) =>
        values?.Any(value => !string.IsNullOrWhiteSpace(value)) == true;

    private static bool MatchesAny(string value, string[]? patterns, bool allowWildcards)
    {
        if (patterns == null)
            return false;

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            var trimmed = pattern.Trim();
            if (allowWildcards && MatchesWildcard(value, trimmed))
                return true;

            if (string.Equals(value, trimmed, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private bool AllowsMcpRegistration(ToolRegistration registration, out string reason)
    {
        var policy = config.McpPolicy;
        if (policy == null)
        {
            reason = string.Empty;
            return true;
        }

        var server = registration.Definition.Id.SourceId;
        if (policy.Servers != null && !MatchesAny(server, policy.Servers, allowWildcards: false))
        {
            reason = "The thread MCP policy does not allow this MCP server.";
            return false;
        }

        var selector = ToCanonicalSelector(registration.Definition.Name);
        if (MatchesAny(selector, policy.Tools?.Deny, allowWildcards: true))
        {
            reason = "The thread MCP policy denies this MCP tool.";
            return false;
        }

        if (policy.Tools?.Allow != null
            && !MatchesAny(selector, policy.Tools.Allow, allowWildcards: true))
        {
            reason = "The thread MCP policy does not allow this MCP tool.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static string ToCanonicalSelector(ToolName toolName) =>
        toolName.Namespace is null ? toolName.Name : $"{toolName.Namespace}/{toolName.Name}";

    private static bool MatchesWildcard(string value, string pattern)
    {
        if (!pattern.Contains('*', StringComparison.Ordinal))
            return string.Equals(value, pattern, StringComparison.Ordinal);

        var valueIndex = 0;
        var patternParts = pattern.Split('*');
        var firstPart = true;
        foreach (var part in patternParts)
        {
            if (part.Length == 0)
                continue;

            var index = value.IndexOf(part, valueIndex, StringComparison.Ordinal);
            if (index < 0)
                return false;

            if (firstPart && !pattern.StartsWith('*') && index != 0)
                return false;

            valueIndex = index + part.Length;
            firstPart = false;
        }

        var lastPart = patternParts.LastOrDefault(part => part.Length > 0);
        return pattern.EndsWith('*')
               || string.IsNullOrEmpty(lastPart)
               || value.EndsWith(lastPart, StringComparison.Ordinal);
    }

    private static string? TryGetStringArgument(AIFunctionArguments arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value))
            return null;

        return value?.ToString();
    }

    private static ModeToolPolicyDecision Deny(string toolName, string reason) =>
        ModeToolPolicyDecision.DenyRecoverable(
            $"""
{PolicyDeniedCode}
Tool: {toolName}
Reason: {reason}
""");
}
