using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic.Core;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using BetaMessageCreateParams = Anthropic.Models.Beta.Messages.MessageCreateParams;

namespace DotCraft.Agents;

internal sealed class AnthropicDeferredToolLoadingChatClient(
    IChatClient innerClient,
    string? model,
    int? defaultMaxOutputTokens = null)
    : DelegatingChatClient(innerClient)
{
    internal const string ToolSearchBetaHeader = "advanced-tool-use-2025-11-20";

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
        var marker = options?.Tools?.OfType<AnthropicToolSearchTool>().FirstOrDefault();
        if (marker == null)
            return options;

        var prepared = options!.Clone();
        var tools = prepared.Tools?.ToList() ?? [];
        var existingNames = tools
            .Select(static tool => tool.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in marker.Registry.GetActivatedToolNames())
        {
            if (!marker.Registry.Entries.TryGetValue(name, out var entry))
                continue;
            if (!existingNames.Add(entry.Tool.Name))
                continue;

            tools.Add(CreateDeferredTool(entry.Tool));
        }

        prepared.Tools = tools;
        PatchRawRepresentationFactory(prepared);
        return prepared;
    }

    internal static AITool CreateDeferredTool(AITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        var betaTool = new BetaTool
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = CreateInputSchema(tool),
            DeferLoading = true,
            Strict = false
        };
        return new BetaToolUnion(betaTool).AsAITool();
    }

    private void PatchRawRepresentationFactory(ChatOptions options)
    {
        var existingFactory = options.RawRepresentationFactory;
        options.RawRepresentationFactory = client =>
        {
            var raw = existingFactory?.Invoke(client);
            var existing = raw as BetaMessageCreateParams;
            var prepared = existing == null
                ? new BetaMessageCreateParams
                {
                    Model = string.IsNullOrWhiteSpace(options.ModelId)
                        ? _model ?? string.Empty
                        : options.ModelId.Trim(),
                    MaxTokens = options.MaxOutputTokens is > 0
                        ? options.MaxOutputTokens.Value
                        : _defaultMaxOutputTokens,
                    Messages = []
                }
                : new BetaMessageCreateParams(existing);

            return prepared with { Betas = AddToolSearchBetaHeader(prepared.Betas) };
        };
    }

    private static IReadOnlyList<ApiEnum<string, AnthropicBeta>> AddToolSearchBetaHeader(
        IReadOnlyList<ApiEnum<string, AnthropicBeta>>? existing)
    {
        var values = existing?
            .Select(static beta => beta.Raw())
            .Where(static beta => !string.IsNullOrWhiteSpace(beta))
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];

        if (!values.Contains(ToolSearchBetaHeader, StringComparer.Ordinal))
            values.Add(ToolSearchBetaHeader);

        return values
            .Select(static beta => (ApiEnum<string, AnthropicBeta>)beta)
            .ToArray();
    }

    private static InputSchema CreateInputSchema(AITool tool)
    {
        var schema = GetJsonSchema(tool);
        if (schema.ValueKind != JsonValueKind.Object)
            return new InputSchema();

        var rawData = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in schema.EnumerateObject())
            rawData[property.Name] = property.Value;

        return new InputSchema(rawData);
    }

    private static JsonElement GetJsonSchema(AITool tool) =>
        tool is AIFunction function && function.JsonSchema.ValueKind != JsonValueKind.Undefined
            ? function.JsonSchema
            : JsonSerializer.SerializeToElement(new { type = "object" });
}
