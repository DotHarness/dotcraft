using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DotCraft.Agents;

public static class AgentHostingServiceCollectionExtensions
{
    /// <summary>
    /// Adds a custom AI agent to the host application builder with the specified name, instructions, chat client, and options.
    /// </summary>
    /// <param name="builder">The host application builder to configure.</param>
    /// <param name="name">The name of the agent.</param>
    /// <param name="chatClient">The chat client which the agent will use for inference.</param>
    /// <param name="options">Configuration options that control the agent's behavior. </param>
    /// <returns>The configured host application builder.</returns>
    public static IHostedAgentBuilder AddAIAgent(
        this IHostApplicationBuilder builder, 
        string name, 
        IChatClient chatClient, 
        ChatClientAgentOptions options)
    {
        return builder.Services.AddAIAgent(name, (sp, agentName) =>
        {
            // Get tools registered for this agent
            var tools = sp.GetKeyedServices<AITool>(name).ToList();
            
            // Clone the provided options or create new ones
            var agentOptions = options?.Clone() ?? new ChatClientAgentOptions();
            
            // Ensure ChatOptions is initialized
            agentOptions.ChatOptions ??= new ChatOptions();
            if (tools.Count > 0)
            {
                agentOptions.ChatOptions.Tools = [..tools];
            }
            
            agentOptions.Name = agentName;
            agentOptions.UseProvidedChatClientAsIs = true;
            return new ChatClientAgent(chatClient, agentOptions);
        });
    }
}