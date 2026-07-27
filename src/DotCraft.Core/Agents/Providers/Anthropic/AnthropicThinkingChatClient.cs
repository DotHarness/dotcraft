using System.Runtime.CompilerServices;
using Anthropic.Models.Messages;
using Anthropic.Services;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using AnthropicBetaDisplay = Anthropic.Models.Beta.Messages.Display;
using AnthropicBetaEffort = Anthropic.Models.Beta.Messages.Effort;
using AnthropicBetaMessageCreateParams = Anthropic.Models.Beta.Messages.MessageCreateParams;
using AnthropicBetaOutputConfig = Anthropic.Models.Beta.Messages.BetaOutputConfig;
using AnthropicBetaThinkingConfigAdaptive = Anthropic.Models.Beta.Messages.BetaThinkingConfigAdaptive;
using AnthropicBetaThinkingConfigDisabled = Anthropic.Models.Beta.Messages.BetaThinkingConfigDisabled;
using AnthropicBetaThinkingConfigParam = Anthropic.Models.Beta.Messages.BetaThinkingConfigParam;
using AnthropicDisplay = Anthropic.Models.Messages.Display;
using AnthropicEffort = Anthropic.Models.Messages.Effort;
using AnthropicMessageCreateParams = Anthropic.Models.Messages.MessageCreateParams;

namespace DotCraft.Agents;

internal sealed class AnthropicThinkingChatClient(
    IChatClient innerClient,
    AppConfig config,
    string? model,
    string? endpoint,
    int? defaultMaxOutputTokens = null,
    AppConfig.ReasoningConfig? reasoningConfig = null,
    bool useDefaultReasoning = true)
    : DelegatingChatClient(innerClient)
{
    private const string FromReasoningEffort = "fromReasoningEffort";
    private const string FromReasoningOutput = "fromReasoningOutput";

    private readonly ModelThinkingAdapterCatalog.AnthropicThinkingAdapterData? _adapter =
        ModelThinkingAdapterCatalog.ResolveAnthropicThinkingAdapter(config, endpoint, model);
    private readonly AppConfig.ReasoningConfig _reasoningConfig = reasoningConfig ?? config.Reasoning;
    private readonly bool _useDefaultReasoning = useDefaultReasoning;
    private readonly string? _model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
    private readonly int _defaultMaxOutputTokens =
        defaultMaxOutputTokens is > 0 ? defaultMaxOutputTokens.Value : AnthropicClientProvider.DefaultMaxOutputTokens;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return await base.GetResponseAsync(messages, PrepareOptions(options), cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(messages, PrepareOptions(options), cancellationToken))
            yield return update;
    }

    internal ChatOptions? PrepareOptions(ChatOptions? options)
    {
        var reasoning = options?.Reasoning ?? (_useDefaultReasoning ? _reasoningConfig.ToOptions() : null);
        if (_adapter == null || reasoning == null)
            return options;

        var prepared = options?.Clone() ?? new ChatOptions();
        var existingFactory = prepared.RawRepresentationFactory;
        prepared.RawRepresentationFactory = client =>
        {
            var raw = existingFactory?.Invoke(client);
            if (raw is AnthropicBetaMessageCreateParams betaParams)
            {
                return CreateBetaParams(
                    betaParams,
                    prepared,
                    _adapter,
                    reasoning);
            }

            if (IsAnthropicBetaClient(client))
            {
                return CreateBetaParams(
                    null,
                    prepared,
                    _adapter,
                    reasoning);
            }

            if (raw != null && raw is not AnthropicMessageCreateParams)
                return raw;

            return CreateParams(
                raw as AnthropicMessageCreateParams,
                prepared,
                _adapter,
                reasoning);
        };

        return prepared;
    }

    private AnthropicMessageCreateParams CreateParams(
        AnthropicMessageCreateParams? existing,
        ChatOptions options,
        ModelThinkingAdapterCatalog.AnthropicThinkingAdapterData adapter,
        ReasoningOptions reasoning)
    {
        var thinking = CreateThinking(adapter, reasoning);
        var outputConfig = CreateOutputConfig(existing?.OutputConfig, adapter, reasoning);
        if (existing != null)
        {
            return new AnthropicMessageCreateParams(existing)
            {
                Thinking = thinking ?? existing.Thinking,
                OutputConfig = outputConfig ?? existing.OutputConfig
            };
        }

        var model = string.IsNullOrWhiteSpace(options.ModelId) ? _model : options.ModelId.Trim();
        return new AnthropicMessageCreateParams
        {
            Model = model ?? string.Empty,
            MaxTokens = options.MaxOutputTokens is > 0 ? options.MaxOutputTokens.Value : _defaultMaxOutputTokens,
            Messages = [],
            Thinking = thinking,
            OutputConfig = outputConfig
        };
    }

    private AnthropicBetaMessageCreateParams CreateBetaParams(
        AnthropicBetaMessageCreateParams? existing,
        ChatOptions options,
        ModelThinkingAdapterCatalog.AnthropicThinkingAdapterData adapter,
        ReasoningOptions reasoning)
    {
        var thinking = CreateBetaThinking(adapter, reasoning);
        var outputConfig = CreateBetaOutputConfig(existing?.OutputConfig, adapter, reasoning);
        if (existing != null)
        {
            return new AnthropicBetaMessageCreateParams(existing)
            {
                Thinking = thinking ?? existing.Thinking,
                OutputConfig = outputConfig ?? existing.OutputConfig
            };
        }

        var model = string.IsNullOrWhiteSpace(options.ModelId) ? _model : options.ModelId.Trim();
        return new AnthropicBetaMessageCreateParams
        {
            Model = model ?? string.Empty,
            MaxTokens = options.MaxOutputTokens is > 0 ? options.MaxOutputTokens.Value : _defaultMaxOutputTokens,
            Messages = [],
            Thinking = thinking,
            OutputConfig = outputConfig
        };
    }

    private static ThinkingConfigParam? CreateThinking(
        ModelThinkingAdapterCatalog.AnthropicThinkingAdapterData adapter,
        ReasoningOptions reasoning)
    {
        var type = adapter.ThinkingType?.Trim();
        if (string.IsNullOrWhiteSpace(type))
            return null;

        if (string.Equals(type, "adaptive", StringComparison.OrdinalIgnoreCase))
        {
            var display = ResolveDisplay(adapter.ThinkingDisplay, reasoning);
            return display.HasValue
                ? new ThinkingConfigAdaptive { Display = display.Value }
                : new ThinkingConfigAdaptive();
        }

        if (string.Equals(type, "disabled", StringComparison.OrdinalIgnoreCase))
            return new ThinkingConfigDisabled();

        return null;
    }

    private static OutputConfig? CreateOutputConfig(
        OutputConfig? existing,
        ModelThinkingAdapterCatalog.AnthropicThinkingAdapterData adapter,
        ReasoningOptions reasoning)
    {
        var effort = ResolveEffort(adapter, reasoning);
        if (!effort.HasValue)
            return null;

        return existing == null
            ? new OutputConfig { Effort = effort.Value }
            : new OutputConfig(existing) { Effort = effort.Value };
    }

    private static AnthropicBetaThinkingConfigParam? CreateBetaThinking(
        ModelThinkingAdapterCatalog.AnthropicThinkingAdapterData adapter,
        ReasoningOptions reasoning)
    {
        var type = adapter.ThinkingType?.Trim();
        if (string.IsNullOrWhiteSpace(type))
            return null;

        if (string.Equals(type, "adaptive", StringComparison.OrdinalIgnoreCase))
        {
            var display = ResolveBetaDisplay(adapter.ThinkingDisplay, reasoning);
            return display.HasValue
                ? new AnthropicBetaThinkingConfigAdaptive { Display = display.Value }
                : new AnthropicBetaThinkingConfigAdaptive();
        }

        if (string.Equals(type, "disabled", StringComparison.OrdinalIgnoreCase))
            return new AnthropicBetaThinkingConfigDisabled();

        return null;
    }

    private static AnthropicBetaOutputConfig? CreateBetaOutputConfig(
        AnthropicBetaOutputConfig? existing,
        ModelThinkingAdapterCatalog.AnthropicThinkingAdapterData adapter,
        ReasoningOptions reasoning)
    {
        var effort = ResolveBetaEffort(adapter, reasoning);
        if (!effort.HasValue)
            return null;

        return existing == null
            ? new AnthropicBetaOutputConfig { Effort = effort.Value }
            : new AnthropicBetaOutputConfig(existing) { Effort = effort.Value };
    }

    private static AnthropicDisplay? ResolveDisplay(string? value, ReasoningOptions reasoning)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (string.Equals(value, FromReasoningOutput, StringComparison.OrdinalIgnoreCase))
        {
            return reasoning.Output == ReasoningOutput.None
                ? AnthropicDisplay.Omitted
                : AnthropicDisplay.Summarized;
        }

        if (string.Equals(value, "summarized", StringComparison.OrdinalIgnoreCase))
            return AnthropicDisplay.Summarized;

        if (string.Equals(value, "omitted", StringComparison.OrdinalIgnoreCase))
            return AnthropicDisplay.Omitted;

        return null;
    }

    private static AnthropicBetaDisplay? ResolveBetaDisplay(string? value, ReasoningOptions reasoning)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (string.Equals(value, FromReasoningOutput, StringComparison.OrdinalIgnoreCase))
        {
            return reasoning.Output == ReasoningOutput.None
                ? AnthropicBetaDisplay.Omitted
                : AnthropicBetaDisplay.Summarized;
        }

        if (string.Equals(value, "summarized", StringComparison.OrdinalIgnoreCase))
            return AnthropicBetaDisplay.Summarized;

        if (string.Equals(value, "omitted", StringComparison.OrdinalIgnoreCase))
            return AnthropicBetaDisplay.Omitted;

        return null;
    }

    private static AnthropicEffort? ResolveEffort(
        ModelThinkingAdapterCatalog.AnthropicThinkingAdapterData adapter,
        ReasoningOptions reasoning)
    {
        var value = adapter.OutputConfigEffort;
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (string.Equals(value, FromReasoningEffort, StringComparison.OrdinalIgnoreCase))
        {
            if (reasoning.Effort.HasValue
                && adapter.OutputConfigEffortMap.TryGetValue(reasoning.Effort.Value.ToString(), out var mapped))
            {
                return ResolveFixedEffort(mapped);
            }

            return reasoning.Effort switch
            {
                ReasoningEffort.Low => AnthropicEffort.Low,
                ReasoningEffort.Medium => AnthropicEffort.Medium,
                ReasoningEffort.High => AnthropicEffort.High,
                ReasoningEffort.ExtraHigh => AnthropicEffort.Xhigh,
                _ => null
            };
        }

        return ResolveFixedEffort(value);
    }

    private static AnthropicBetaEffort? ResolveBetaEffort(
        ModelThinkingAdapterCatalog.AnthropicThinkingAdapterData adapter,
        ReasoningOptions reasoning)
    {
        var value = adapter.OutputConfigEffort;
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (string.Equals(value, FromReasoningEffort, StringComparison.OrdinalIgnoreCase))
        {
            if (reasoning.Effort.HasValue
                && adapter.OutputConfigEffortMap.TryGetValue(reasoning.Effort.Value.ToString(), out var mapped))
            {
                return ResolveFixedBetaEffort(mapped);
            }

            return reasoning.Effort switch
            {
                ReasoningEffort.Low => AnthropicBetaEffort.Low,
                ReasoningEffort.Medium => AnthropicBetaEffort.Medium,
                ReasoningEffort.High => AnthropicBetaEffort.High,
                ReasoningEffort.ExtraHigh => AnthropicBetaEffort.Xhigh,
                _ => null
            };
        }

        return ResolveFixedBetaEffort(value);
    }

    private static AnthropicEffort? ResolveFixedEffort(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "low" => AnthropicEffort.Low,
            "medium" => AnthropicEffort.Medium,
            "high" => AnthropicEffort.High,
            "xhigh" => AnthropicEffort.Xhigh,
            "max" => AnthropicEffort.Max,
            _ => null
        };
    }

    private static bool IsAnthropicBetaClient(IChatClient client) =>
        client.GetService(typeof(IBetaService)) is IBetaService;

    private static AnthropicBetaEffort? ResolveFixedBetaEffort(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "low" => AnthropicBetaEffort.Low,
            "medium" => AnthropicBetaEffort.Medium,
            "high" => AnthropicBetaEffort.High,
            "xhigh" => AnthropicBetaEffort.Xhigh,
            "max" => AnthropicBetaEffort.Max,
            _ => null
        };
    }
}
