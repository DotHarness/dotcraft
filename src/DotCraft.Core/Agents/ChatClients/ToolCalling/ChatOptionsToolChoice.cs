using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal static class ChatOptionsToolChoice
{
    public static void DisableOpenAIToolChoice(ChatOptions options)
    {
        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties["dotcraft.tool_choice"] = "none";
    }
}
