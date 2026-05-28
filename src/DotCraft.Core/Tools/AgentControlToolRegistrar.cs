using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
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
        string? subAgentModel = null)
    {
        var agentTools = new AgentTools(subAgentCoordinator, subAgentRoles, maxSubAgentDepth, subAgentModel);
        AddIfAllowed(tools, context, nameof(AgentTools.SpawnAgent), () => AIFunctionFactory.Create(agentTools.SpawnAgent));
        AddIfAllowed(tools, context, nameof(AgentTools.SendInput), () => AIFunctionFactory.Create(agentTools.SendInput));
        AddIfAllowed(tools, context, nameof(AgentTools.WaitAgent), () => AIFunctionFactory.Create(agentTools.WaitAgent));
        AddIfAllowed(tools, context, nameof(AgentTools.ResumeAgent), () => AIFunctionFactory.Create(agentTools.ResumeAgent));
        AddIfAllowed(tools, context, nameof(AgentTools.CloseAgent), () => AIFunctionFactory.Create(agentTools.CloseAgent));
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
