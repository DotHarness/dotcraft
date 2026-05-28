using System.Runtime.CompilerServices;
using System.Text.Json;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using OpenAIAssistantChatMessage = OpenAI.Chat.AssistantChatMessage;
using OpenAIChatCompletionOptions = OpenAI.Chat.ChatCompletionOptions;

namespace DotCraft.Agents;

/// <summary>
/// Preserves deep thinking metadata on assistant tool-call turns.
/// </summary>
internal sealed class DeepThinkingChatClient(
    IChatClient innerClient,
    AppConfig config,
    string? model,
    string? endpoint,
    AppConfig.ReasoningConfig? reasoningConfig = null)
    : DelegatingChatClient(innerClient)
{
    private readonly bool _enabled = ModelThinkingAdapterCatalog.ShouldApplyDeepThinking(config, endpoint, model);
    private readonly AppConfig.ReasoningConfig _reasoningConfig = reasoningConfig ?? config.Reasoning;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var prepared = Prepare(messages, options);
        return await base.GetResponseAsync(prepared.Messages, prepared.Options, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var prepared = Prepare(messages, options);
        await foreach (var update in base.GetStreamingResponseAsync(prepared.Messages, prepared.Options, cancellationToken))
            yield return update;
    }

    internal PreparedChatRequest Prepare(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        if (!_enabled)
            return new PreparedChatRequest(messages as IReadOnlyList<ChatMessage> ?? messages.ToList(), options);

        var original = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        List<ChatMessage>? rewritten = null;
        for (var i = 0; i < original.Count; i++)
        {
            var message = original[i];
            var prepared = PrepareMessage(message);
            if (!ReferenceEquals(prepared, message))
            {
                rewritten ??= [.. original.Take(i)];
                rewritten.Add(prepared);
            }
            else
            {
                rewritten?.Add(message);
            }
        }

        return new PreparedChatRequest(rewritten ?? original, PrepareOptions(options));
    }

    internal static bool ShouldApply(string? endpoint, string? model)
    {
        return ModelThinkingAdapterCatalog.ShouldApplyDeepThinking(endpoint, model);
    }

    private ChatOptions? PrepareOptions(ChatOptions? options)
    {
        var reasoning = options?.Reasoning ?? _reasoningConfig.ToOptions();
        if (reasoning == null)
            return options;

        var prepared = options?.Clone() ?? new ChatOptions();
        var existingFactory = prepared.RawRepresentationFactory;
        prepared.RawRepresentationFactory = client =>
        {
            var raw = existingFactory?.Invoke(client) ?? new OpenAIChatCompletionOptions();
            if (raw is OpenAIChatCompletionOptions openAIOptions)
            {
#pragma warning disable SCME0001
                openAIOptions.Patch.Set(
                    "$.thinking"u8,
                    BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(new { type = "enabled" })));
#pragma warning restore SCME0001
            }

            return raw;
        };
        return prepared;
    }

    private static ChatMessage PrepareMessage(ChatMessage message)
    {
        if (message.Role != ChatRole.Assistant ||
            message.Contents.OfType<FunctionCallContent>().FirstOrDefault() is null)
        {
            return message;
        }

        var reasoning = string.Concat(ReasoningContentHelper.EnumerateTexts(message.Contents));
        if (string.IsNullOrEmpty(reasoning))
            return message;

        var prepared = new ChatMessage(message.Role, message.Contents)
        {
            AdditionalProperties = message.AdditionalProperties,
            AuthorName = message.AuthorName,
            CreatedAt = message.CreatedAt,
            MessageId = message.MessageId,
            RawRepresentation = CreateAssistantRawRepresentation(message, reasoning)
        };
        return prepared;
    }

    private static OpenAIAssistantChatMessage CreateAssistantRawRepresentation(
        ChatMessage message,
        string reasoning)
    {
        var content = string.Concat(message.Contents.OfType<TextContent>().Select(static text => text.Text));
        var assistantMessage = new OpenAIAssistantChatMessage(content);

#pragma warning disable SCME0001
        assistantMessage.Patch.Set(
            "$.reasoning_content"u8,
            BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(reasoning)));
        assistantMessage.Patch.Set(
            "$.tool_calls"u8,
            BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(CreateOpenAIToolCalls(message))));
#pragma warning restore SCME0001

        return assistantMessage;
    }

    private static object[] CreateOpenAIToolCalls(ChatMessage message) =>
        message.Contents
            .OfType<FunctionCallContent>()
            .Select(static call => new
            {
                id = call.CallId,
                type = "function",
                function = new
                {
                    name = call.Name,
                    arguments = SerializeToolCallArguments(call.Arguments)
                }
            })
            .Cast<object>()
            .ToArray();

    private static string SerializeToolCallArguments(object? arguments) =>
        arguments == null
            ? "{}"
            : JsonSerializer.Serialize(arguments);

    internal sealed record PreparedChatRequest(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options);
}
