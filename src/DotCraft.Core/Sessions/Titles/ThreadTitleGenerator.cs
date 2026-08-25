using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

internal sealed record ThreadTitleGenerationRequest(
    string ThreadId,
    string ProvisionalTitle,
    string UserMessage,
    string? MainProviderId,
    string? MainModel);

internal interface IThreadTitleGenerator
{
    Task<string?> GenerateAsync(
        ThreadTitleGenerationRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class ModelThreadTitleGenerator(
    ChatClientRegistry chatClientRegistry,
    Func<AppConfig> configProvider,
    TimeSpan? requestTimeout = null) : IThreadTitleGenerator
{
    private static readonly ChatResponseFormat TitleResponseFormat = CreateTitleResponseFormat();
    private readonly TimeSpan _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);

    public async Task<string?> GenerateAsync(
        ThreadTitleGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        var prompt = ThreadTitleText.BuildPrompt(request.UserMessage);
        if (prompt == null)
            return null;

        var config = configProvider();
        var mainRuntime = chatClientRegistry.ResolveMainRuntime(
            config,
            request.MainProviderId,
            request.MainModel);
        var titleRuntime = chatClientRegistry.ResolveSubAgentRuntime(
            config,
            mainRuntime.ProviderId,
            mainRuntime.Model);
        var lowReasoning = new AppConfig.ReasoningConfig
        {
            Enabled = true,
            Effort = ModelReasoningEffort.Low,
            Output = ReasoningOutput.None
        };
        var titleClient = ProviderChatClientAdapters.CreateRequestAdaptedClient(
            chatClientRegistry.GetChatClient(titleRuntime),
            config,
            titleRuntime,
            lowReasoning);
        var options = new ChatOptions
        {
            Tools = [],
            Reasoning = lowReasoning.ToOptions(),
            ResponseFormat = TitleResponseFormat
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_requestTimeout);
        var response = await titleClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, prompt)],
                options,
                timeoutCts.Token)
            .ConfigureAwait(false);
        return ThreadTitleText.ParseGeneratedTitle(response.Text);
    }

    private static ChatResponseFormat CreateTitleResponseFormat()
    {
        using var document = JsonDocument.Parse(
            $$"""
            {
              "type": "object",
              "properties": {
                "title": {
                  "type": "string",
                  "minLength": 1,
                  "maxLength": {{ThreadTitleText.MaxTitleCharacters}}
                }
              },
              "required": ["title"],
              "additionalProperties": false
            }
            """);
        return ChatResponseFormat.ForJsonSchema(
            document.RootElement.Clone(),
            schemaName: "thread_title");
    }
}

internal static class ThreadTitleText
{
    public const int MaxTitleCharacters = 36;
    public const int MaxPromptBytes = 960;

    private const string Instructions =
        "Generate a concise, single-line task title of at most 36 characters and under five words where possible. "
        + "Start with an imperative verb. Capitalize only the first word unless the user's language, proper nouns, "
        + "acronyms, or code terms require otherwise. Preserve ticket references exactly. Write in the user's language. "
        + "Do not use quotes, markdown, or trailing punctuation. Do not answer the request.";

    private static readonly char[] WrappingQuotes = ['"', '\'', '`', '“', '”', '‘', '’'];
    private static readonly char[] TrailingPunctuation = ['.', '?', '!', '。', '？', '！'];

    public static string? CreateProvisionalTitle(string text)
    {
        var normalized = CollapseWhitespace(text);
        return normalized.Length == 0 ? null : TruncateRunes(normalized, MaxTitleCharacters);
    }

    public static string? BuildPrompt(string userMessage)
    {
        var normalized = CollapseWhitespace(userMessage);
        if (normalized.Length == 0)
            return null;

        var prefix = Instructions + "\n\nUser prompt:\n";
        var remainingBytes = Math.Max(0, MaxPromptBytes - Encoding.UTF8.GetByteCount(prefix));
        var boundedMessage = TruncateUtf8(normalized, remainingBytes);
        return boundedMessage.Length == 0 ? null : prefix + boundedMessage;
    }

    public static string? ParseGeneratedTitle(string? response)
    {
        if (string.IsNullOrWhiteSpace(response) || !response.TrimStart().StartsWith('{'))
            return null;

        try
        {
            using var document = JsonDocument.Parse(response);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var properties = document.RootElement.EnumerateObject().ToArray();
            if (properties.Length != 1
                || !string.Equals(properties[0].Name, "title", StringComparison.Ordinal)
                || properties[0].Value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var normalized = CollapseWhitespace(properties[0].Value.GetString() ?? string.Empty)
                .Trim(WrappingQuotes)
                .TrimEnd(TrailingPunctuation)
                .TrimEnd();
            return normalized.Length == 0
                ? null
                : TruncateRunes(normalized, MaxTitleCharacters);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }
            builder.Append(rune.ToString());
        }
        return builder.ToString();
    }

    private static string TruncateRunes(string value, int maxRunes)
    {
        var builder = new StringBuilder(value.Length);
        var count = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (count++ >= maxRunes)
                break;
            builder.Append(rune.ToString());
        }
        return builder.ToString();
    }

    private static string TruncateUtf8(string value, int maxBytes)
    {
        var builder = new StringBuilder(value.Length);
        var bytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var runeBytes = rune.Utf8SequenceLength;
            if (bytes + runeBytes > maxBytes)
                break;
            builder.Append(rune.ToString());
            bytes += runeBytes;
        }
        return builder.ToString();
    }
}
