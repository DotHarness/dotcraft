using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.GeneratedTools.Core;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Registers DotCraft agent-control tools according to a context policy.
/// </summary>
public static class AgentControlToolRegistrar
{
    /// <summary>
    /// Adds the allowed DotCraft agent-control tools to the supplied tool list.
    /// </summary>
    public static void AddTools(
        ICollection<AITool> tools,
        ToolProviderContext context,
        SubAgentCoordinator subAgentCoordinator,
        IEnumerable<SubAgentRoleConfig>? subAgentRoles = null,
        int maxSubAgentDepth = 1,
        string? subAgentModel = null,
        SubAgentWaitAgentTimeoutOptions? waitAgentTimeoutOptions = null)
    {
        var agentTools = new AgentTools(subAgentCoordinator, subAgentRoles, maxSubAgentDepth, subAgentModel, waitAgentTimeoutOptions);
        AddIfAllowed(tools, context, nameof(AgentTools.SpawnAgent), () => GeneratedToolFunctions.AgentTools_SpawnAgent(agentTools));
        AddIfAllowed(tools, context, nameof(AgentTools.SendMessage), () => GeneratedToolFunctions.AgentTools_SendMessage(agentTools));
        AddIfAllowed(tools, context, nameof(AgentTools.FollowupTask), () => GeneratedToolFunctions.AgentTools_FollowupTask(agentTools));
        AddIfAllowed(tools, context, nameof(AgentTools.WaitAgent), () => GeneratedToolFunctions.AgentTools_WaitAgent(agentTools));
        AddIfAllowed(tools, context, nameof(AgentTools.ListAgents), () => GeneratedToolFunctions.AgentTools_ListAgents(agentTools));
        AddIfAllowed(tools, context, nameof(AgentTools.CloseAgent), () => GeneratedToolFunctions.AgentTools_CloseAgent(agentTools));
    }

    private static void AddIfAllowed(
        ICollection<AITool> tools,
        ToolProviderContext context,
        string toolName,
        Func<AITool> createTool)
    {
        if (AgentControlToolPolicy.Allows(context, toolName))
            tools.Add(createTool());
    }
}
