using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal sealed class OpenRouterChatClient(IChatClient inner, EffectiveModelRuntime runtime, Action<decimal?> completed) : DelegatingChatClient(inner)
{
    internal const string ReasoningDetailsKey = "openrouter.reasoning_details";
    internal const string CostKey = "openrouter.costUsd";
    internal static readonly AsyncLocal<OpenRouterCall?> Active = new();

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var history = messages.ToList();
        options = Prepare(options);
        var call = new OpenRouterCall(history, options);
        var previous = Active.Value;
        Active.Value = call;
        try
        {
            var response = await base.GetResponseAsync(history, options, cancellationToken).ConfigureAwait(false);
            if (call.Reasoning.Count > 0 && response.Messages.LastOrDefault() is { } message)
                message.Contents.Add(call.ReasoningContent());
            if (call.Cost is { } cost) (response.AdditionalProperties ??= new())[CostKey] = cost;
            completed(call.Cost);
            return response;
        }
        finally { Active.Value = previous; }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var history = messages.ToList();
        options = Prepare(options);
        var call = new OpenRouterCall(history, options);
        var previous = Active.Value;
        Active.Value = call;
        string? messageId = null;
        try
        {
            await foreach (var update in base.GetStreamingResponseAsync(history, options, cancellationToken).ConfigureAwait(false))
            {
                messageId = update.MessageId ?? messageId;
                yield return update;
            }
            completed(call.Cost);
            if (call.Reasoning.Count > 0 || call.Cost is not null)
            {
                var update = new ChatResponseUpdate { Role = ChatRole.Assistant, MessageId = messageId };
                if (call.Reasoning.Count > 0) update.Contents.Add(call.ReasoningContent());
                if (call.Cost is { } cost) (update.AdditionalProperties ??= new())[CostKey] = cost;
                yield return update;
            }
        }
        finally { Active.Value = previous; }
    }

    private ChatOptions? Prepare(ChatOptions? options)
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
        if (ProviderReasoningOptions.Resolve(options) == ProviderReasoningEffort.None
            && runtime.ModelMetadata?.Reasoning is { SupportsDisable: false })
            throw new ArgumentException($"Model '{runtime.Model}' requires reasoning.");
        return options;
    }
}

internal sealed class OpenRouterCall(IReadOnlyList<ChatMessage> history, ChatOptions? options)
{
    internal IReadOnlyList<ChatMessage> History => history;
    internal ChatOptions? Options => options;
    internal List<JsonObject> Reasoning { get; } = [];
    internal decimal? Cost { get; private set; }

    internal TextReasoningContent ReasoningContent() => new(string.Empty)
    {
        AdditionalProperties = new()
        {
            ["dotcraft.reasoning.group"] = "openrouter.replay",
            [OpenRouterChatClient.ReasoningDetailsKey] = new JsonArray(Reasoning.Select(item => item.DeepClone()).ToArray()).ToJsonString()
        }
    };

    internal void Read(OpenRouterResponse body)
    {
        var usage = body.Usage ?? body.Response?.Usage;
        var cost = usage?.Cost ?? body.OpenRouterMetadata?.Usage?.Cost;
        if (cost is { } amount) Cost = amount;
        foreach (var choice in body.Choices)
        {
            var message = choice.Delta ?? choice.Message;
            foreach (var detail in message?.ReasoningDetails ?? [])
            {
                var index = detail.TryGetProperty("index", out var position) && position.ValueKind != JsonValueKind.Null
                    ? position.GetInt32() : Reasoning.Count;
                var existing = Reasoning.FirstOrDefault(item => item["index"]?.GetValue<int>() == index);
                if (existing is null) { var copy = JsonSerializer.SerializeToNode(detail)!.AsObject(); copy["index"] = index; Reasoning.Add(copy); continue; }
                foreach (var property in detail.EnumerateObject())
                {
                    if (property.Name is "text" or "summary" or "data" && property.Value.ValueKind == JsonValueKind.String)
                        existing[property.Name] = (existing[property.Name]?.GetValue<string>() ?? string.Empty) + property.Value.GetString();
                    else existing[property.Name] = JsonSerializer.SerializeToNode(property.Value);
                }
            }
        }
    }
}
