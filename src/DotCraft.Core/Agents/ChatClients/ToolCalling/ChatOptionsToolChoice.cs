using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAIChatCompletionOptions = OpenAI.Chat.ChatCompletionOptions;

namespace DotCraft.Agents;

internal static class ChatOptionsToolChoice
{
    public static void DisableOpenAIToolChoice(ChatOptions options)
    {
        var existingFactory = options.RawRepresentationFactory;
        options.RawRepresentationFactory = client =>
        {
            var raw = existingFactory?.Invoke(client) ?? new OpenAIChatCompletionOptions();
            if (raw is OpenAIChatCompletionOptions openAIOptions)
            {
#pragma warning disable SCME0001
                openAIOptions.Patch.Set(
                    "$.tool_choice"u8,
                    BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes("none")));
#pragma warning restore SCME0001
            }

            return raw;
        };
    }
}
