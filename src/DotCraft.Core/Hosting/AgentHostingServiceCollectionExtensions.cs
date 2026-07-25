using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DotCraft.Agents;

public static class AgentHostingServiceCollectionExtensions
{
    /// <summary>
    /// Registers a keyed DotCraft agent runtime while retaining MEAI chat and tool abstractions.
    /// </summary>
    public static IHostApplicationBuilder AddAIAgent(
        this IHostApplicationBuilder builder,
        string name,
        IChatClient chatClient,
        ChatOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(chatClient);

        builder.Services.AddKeyedSingleton<ChatClientAgent>(name, (sp, _) =>
        {
            var tools = sp.GetKeyedServices<AITool>(name).ToList();
            var chatOptions = options?.Clone() ?? new ChatOptions();
            if (tools.Count > 0)
                chatOptions.Tools = [.. tools];
            return new ChatClientAgent(chatClient, chatOptions, name: name);
        });

        return builder;
    }
}
