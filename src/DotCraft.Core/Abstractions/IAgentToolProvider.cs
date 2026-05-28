using Microsoft.Extensions.AI;

namespace DotCraft.Abstractions;

/// <summary>
/// Provides tools for the AI agent.
/// Implementations can contribute channel-specific or feature-specific tools.
/// </summary>
public interface IAgentToolProvider
{
    /// <summary>
    /// Gets the priority of this provider for tool registration ordering.
    /// Lower priority providers are processed first.
    /// Default priority is 100. Core tools should have lower priorities.
    /// </summary>
    int Priority => 100;

    /// <summary>
    /// Creates the tools provided by this provider.
    /// </summary>
    /// <param name="context">The context containing dependencies for tool creation.</param>
    /// <returns>A collection of AI tools.</returns>
    IEnumerable<AITool> CreateTools(ToolProviderContext context);
}
