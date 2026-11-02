using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Core;
using Anthropic.Models.Beta.Messages;
using DotCraft.Configuration;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using OpenAI.Responses;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using NativeMessageParams = Anthropic.Models.Beta.Messages.MessageCreateParams;

#pragma warning disable OPENAI001, SCME0001

namespace DotCraft.Agents;

internal sealed class OpenRouterChatClient(IChatClient inner, EffectiveModelRuntime runtime,
    OpenRouterRoutingPreferences routing, Action<decimal?> completed) : DelegatingChatClient(inner)
{
    internal const string ReasoningDetailsKey = "openrouter.reasoning_details";

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (runtime.Protocol == "openai-responses")
        {
            var updates = new List<ChatResponseUpdate>();
            await foreach (var update in GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false)) updates.Add(update);
            var response = updates.ToAgentResponse();
            if (updates.Select(update => ProviderReportedCost.TryGet(update.AdditionalProperties)).LastOrDefault(cost => cost is not null) is { } cost)
                ProviderReportedCost.Attach(response, cost);
            return response;
        }
        var result = await base.GetResponseAsync(PrepareMessages(messages), Prepare(options), cancellationToken).ConfigureAwait(false);
        var metadata = new OpenRouterResponseMetadata();
        var plaintext = metadata.Read(result.RawRepresentation);
        if (result.Messages.LastOrDefault() is { } message)
        {
            AddPlaintextReasoning(message.Contents, plaintext);
            if (metadata.Reasoning.Count > 0) message.Contents.Add(metadata.ReasoningContent());
        }
        if (metadata.Cost is { } amount) ProviderReportedCost.Attach(result, amount);
        completed(metadata.Cost);
        return result;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var metadata = new OpenRouterResponseMetadata();
        string? messageId = null;
        await foreach (var update in base.GetStreamingResponseAsync(PrepareMessages(messages), Prepare(options), cancellationToken).ConfigureAwait(false))
        {
            messageId = update.MessageId ?? messageId;
            AddPlaintextReasoning(update.Contents, metadata.Read(update.RawRepresentation));
            yield return update;
        }
        completed(metadata.Cost);
        if (metadata.Reasoning.Count > 0 || metadata.Cost is not null)
        {
            var update = new ChatResponseUpdate { Role = ChatRole.Assistant, MessageId = messageId };
            if (metadata.Reasoning.Count > 0) update.Contents.Add(metadata.ReasoningContent());
            if (metadata.Cost is { } amount) ProviderReportedCost.Attach(update, amount);
            yield return update;
        }
    }

    private static void AddPlaintextReasoning(IList<AIContent> contents, string? text)
    {
        if (!string.IsNullOrEmpty(text) && !contents.OfType<TextReasoningContent>().Any(content => !string.IsNullOrEmpty(content.Text)))
            contents.Add(new TextReasoningContent(text));
    }

    private ChatOptions Prepare(ChatOptions? options)
    {
        options = PrepareReasoning(options)?.Clone() ?? new();
        var factory = options.RawRepresentationFactory;
        var effort = ProviderReasoningOptions.Resolve(options);
        options.RawRepresentationFactory = client =>
        {
            var raw = factory?.Invoke(client);
            if (runtime.Protocol == "anthropic")
            {
                var native = raw as NativeMessageParams ?? new NativeMessageParams
                {
                    Model = runtime.Model, MaxTokens = options.MaxOutputTokens ?? runtime.MaxOutputTokens ?? AnthropicClientProvider.DefaultMaxOutputTokens,
                    Messages = []
                };
                if (routing.IsEmpty) return native;
                var body = native.RawBodyData.ToDictionary();
                body["provider"] = JsonSerializer.SerializeToElement(routing, OpenRouterJson.Options);
                return NativeMessageParams.FromRawUnchecked(native.RawHeaderData, native.RawQueryData, body);
            }
            if (runtime.Protocol == "openai-responses")
            {
                var native = raw as CreateResponseOptions ?? new CreateResponseOptions();
                PatchShared(ref native.Patch);
                if (runtime.ModelMetadata?.Reasoning is { SupportedEfforts.Count: 0 })
                {
                    native.ReasoningOptions ??= new ResponseReasoningOptions();
                    native.ReasoningOptions.ReasoningEffortLevel = null;
                    if (options.Reasoning?.Output is { } output && output != ReasoningOutput.None)
                        native.ReasoningOptions.ReasoningSummaryVerbosity ??= output == ReasoningOutput.Full
                            ? ResponseReasoningSummaryVerbosity.Detailed : ResponseReasoningSummaryVerbosity.Auto;
                }
                return native;
            }
            var chat = raw as ChatCompletionOptions ?? new ChatCompletionOptions();
            PatchShared(ref chat.Patch);
            chat.Patch.Set("$.usage.include"u8, true);
            chat.Patch.Remove("$.reasoning_effort"u8);
            if (effort is { } selected)
            {
                var reasoning = selected == ProviderReasoningEffort.None ? new OpenRouterReasoningRequest(Enabled: false)
                    : runtime.ModelMetadata?.Reasoning is { SupportedEfforts.Count: 0 } ? new OpenRouterReasoningRequest(Enabled: true)
                    : new OpenRouterReasoningRequest(Effort: selected.ToToken());
                chat.Patch.Set("$.reasoning"u8, BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(reasoning, OpenRouterJson.Options)));
            }
            return chat;
        };
        return options;
    }

    private void PatchShared(ref JsonPatch patch)
    {
        if (!routing.IsEmpty) patch.Set("$.provider"u8, BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(routing, OpenRouterJson.Options)));
        if (runtime.Model.StartsWith("anthropic/", StringComparison.Ordinal)
            && ProviderPipelineOptionsScope.Current is { PromptCachingEnabled: true } caching)
        {
            var cache = new OpenRouterCacheControl("ephemeral", caching.PromptCacheTtl is "5m" or "1h" ? caching.PromptCacheTtl : null);
            patch.Set("$.cache_control"u8, BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(cache, OpenRouterJson.Options)));
        }
    }

    private IEnumerable<ChatMessage> PrepareMessages(IEnumerable<ChatMessage> messages)
    {
        if (runtime.Protocol != "openai-chat-completions") return messages;
        return messages.Select(message =>
        {
            var details = message.Contents.Select(content => content.AdditionalProperties?.GetValueOrDefault(ReasoningDetailsKey)).OfType<string>().LastOrDefault();
            if (message.Role != ChatRole.Assistant || details is null) return message;
            var native = new[] { message }.AsOpenAIChatMessages().OfType<AssistantChatMessage>().Single();
            native.Patch.Set("$.reasoning_details"u8, BinaryData.FromString(details));
            native.Patch.Remove("$.reasoning_content"u8);
            var prepared = message.Clone();
            prepared.RawRepresentation = native;
            return prepared;
        });
    }

    private ChatOptions? PrepareReasoning(ChatOptions? options)
    {
        options = ProviderReasoningOptions.ApplyDefaults(options, ProviderPipelineOptionsScope.Current);
        if (runtime.ModelMetadata?.Reasoning is not { } reasoning)
        {
            if (options is null) return null;
            var plain = ProviderReasoningOptions.WithoutMetadata(options);
            plain.Reasoning = null;
            return plain;
        }
        if (options?.Reasoning is null)
        {
            options = options?.Clone() ?? new();
            var enabled = !reasoning.SupportsDisable || (ProviderPipelineOptionsScope.Current?.ReasoningEnabled ?? reasoning.DefaultEnabled);
            ProviderReasoningOptions.Apply(options, true, enabled ? reasoning.DefaultEffort : ProviderReasoningEffort.None, ReasoningOutput.Full);
        }
        if (ProviderReasoningOptions.Resolve(options) == ProviderReasoningEffort.None && !reasoning.SupportsDisable)
            throw new ArgumentException($"Model '{runtime.Model}' requires reasoning.");
        return options;
    }
}

internal sealed class OpenRouterResponseMetadata
{
    internal List<JsonObject> Reasoning { get; } = [];
    internal decimal? Cost { get; private set; }

    internal TextReasoningContent ReasoningContent() => new(string.Empty)
    {
        AdditionalProperties = new()
        {
            ["dotcraft.reasoning.group"] = "openrouter.replay",
            [OpenRouterChatClient.ReasoningDetailsKey] = JsonSerializer.Serialize(Reasoning)
        }
    };

    internal string? Read(object? raw)
    {
        switch (raw)
        {
            case ChatCompletion completion:
                return Read(ref completion.Patch, "$.choices[0].message.reasoning_details"u8, "$.choices[0].message.reasoning"u8);
            case StreamingChatCompletionUpdate update:
                return Read(ref update.Patch, "$.choices[0].delta.reasoning_details"u8, "$.choices[0].delta.reasoning"u8);
            case StreamingResponseCompletedUpdate update:
                Read(ref update.Patch);
                Read(ref update.Response.Patch);
                break;
            case ResponseResult result:
                Read(ref result.Patch);
                break;
            case BetaMessage message:
                Read(message.RawData);
                break;
            case BetaRawMessageStreamEvent { Value: JsonModel message }:
                Read(message.RawData);
                break;
        }
        return null;
    }

    private string? Read(ref JsonPatch patch, ReadOnlySpan<byte> detailsPath = default, ReadOnlySpan<byte> textPath = default)
    {
        if (patch.TryGetValue("$.usage.cost"u8, out decimal cost)
            || patch.TryGetValue("$.openrouter_metadata.usage.cost"u8, out cost)) Cost = cost;
        var text = !textPath.IsEmpty && patch.TryGetValue(textPath, out string? value) ? value : null;
        if (detailsPath.IsEmpty || !patch.TryGetJson(detailsPath, out var data)) return text;
        using var details = JsonDocument.Parse(data);
        if (details.RootElement.ValueKind != JsonValueKind.Array) return text;
        var detailText = new StringBuilder();
        foreach (var detail in details.RootElement.EnumerateArray())
        {
            if (detail.ValueKind != JsonValueKind.Object) continue;
            var index = detail.TryGetProperty("index", out var position) && position.ValueKind != JsonValueKind.Null
                ? position.GetInt32() : Reasoning.Count;
            var existing = Reasoning.FirstOrDefault(item => item["index"]?.GetValue<int>() == index);
            if (existing is null)
            {
                existing = JsonSerializer.SerializeToNode(detail)!.AsObject();
                existing["index"] = index;
                Reasoning.Add(existing);
            }
            else
            {
                foreach (var property in detail.EnumerateObject())
                {
                    if (property.Name is "text" or "summary" or "data" && property.Value.ValueKind == JsonValueKind.String)
                        existing[property.Name] = (existing[property.Name]?.GetValue<string>() ?? string.Empty) + property.Value.GetString();
                    else existing[property.Name] = JsonSerializer.SerializeToNode(property.Value);
                }
            }
            var field = existing["type"]?.GetValue<string>() switch
            { "reasoning.text" => "text", "reasoning.summary" => "summary", _ => null };
            if (field is not null && detail.TryGetProperty(field, out var chunk) && chunk.ValueKind == JsonValueKind.String)
                detailText.Append(chunk.GetString());
        }
        return string.IsNullOrEmpty(text) ? detailText.ToString() : text;
    }

    private void Read(IReadOnlyDictionary<string, JsonElement> data)
    {
        if (data.TryGetValue("usage", out var usage) && usage.Deserialize<OpenRouterResponseUsage>(OpenRouterJson.Options)?.Cost is { } cost)
            Cost = cost;
        else if (data.TryGetValue("openrouter_metadata", out var metadata)
            && metadata.Deserialize<OpenRouterMetadata>(OpenRouterJson.Options)?.Usage?.Cost is { } reported) Cost = reported;
    }
}
