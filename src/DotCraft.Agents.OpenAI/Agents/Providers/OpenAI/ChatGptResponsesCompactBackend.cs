using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;

#pragma warning disable OPENAI001

namespace DotCraft.Agents;

internal static class ChatGptResponsesCompactEligibility
{
    public static bool IsEligible(
        Configuration.EffectiveModelRuntime runtime,
        string historyMode,
        int providerHistorySchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return runtime.IsChatGptOAuth
               && runtime.IsOpenAIResponses
               && string.Equals(historyMode, "server", StringComparison.OrdinalIgnoreCase)
               && providerHistorySchemaVersion == OpenAIProviderHistory.SchemaVersion;
    }
}

internal static class ChatGptResponsesCompactRequestBuilder
{
    public static ChatGptResponsesCompactRequest Build(
        string model,
        ProviderNativeCompactionInput input,
        IReadOnlyList<ChatMessage> neutralHistory,
        ChatOptions? options,
        bool useResponsesLite,
        IChatClient? rawRepresentationClient = null)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model must be configured.", nameof(model));
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(neutralHistory);

        var canonicalInput = JsonSerializer.SerializeToNode(
                                 input.Items.Select(static item => item.Payload),
                                 ChatGptResponsesCompactJson.Options) as JsonArray
                             ?? throw new InvalidDataException(
                                 "provider_compaction_invalid_request: Native history must serialize as a JSON array.");

        var request = ResponsesToolSearchMapper.CreateResponseRequest(
            model.Trim(),
            neutralHistory,
            options,
            canonicalInput: canonicalInput,
            canonicalItemIdentity: OpenAIResponsesItemIdentityDiagnostics.FromInput(canonicalInput),
            rawRepresentationClient: rawRepresentationClient);
        var ordinaryBody = ModelReaderWriter.Write(request.Options);
        var wireBody = useResponsesLite
            ? OpenAIResponsesLiteRequestMapper.BuildCompactWireBody(ordinaryBody)
            : ordinaryBody;
        var compact = JsonSerializer.Deserialize<ChatGptResponsesCompactRequest>(
                          wireBody.ToMemory().Span,
                          ChatGptResponsesCompactJson.Options)
                      ?? throw new InvalidDataException(
                          "provider_compaction_invalid_request: Responses mapper produced an empty request body.");

        return compact with { Model = model.Trim() };
    }
}

internal sealed class OpenAIResponsesCompactor(
    string model,
    bool useResponsesLite,
    IChatGptResponsesCompactTransport transport,
    IChatClient? rawRepresentationClient = null) : IProviderNativeCompactor
{
    public async Task<ProviderNativeCompactionReplacement> CompactAsync(
        ProviderNativeCompactionInput input,
        IReadOnlyList<ChatMessage> neutralHistory,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(neutralHistory);
        if (input.Items.Count == 0)
            throw new InvalidOperationException("provider_compaction_empty_input");

        var body = ChatGptResponsesCompactRequestBuilder.Build(
            model,
            input,
            neutralHistory,
            options,
            useResponsesLite,
            rawRepresentationClient);
        var response = await transport.CompactAsync(body, cancellationToken).ConfigureAwait(false);
        var output = ValidateOutput(response);
        var estimatedTokensAfter = OpenAIResponsesNativeTokenEstimator.Estimate(output, [], options);
        var items = output
            .Select((item, index) => new ProviderHistoryItem($"compact-output:{index}", item))
            .ToArray();
        return new ProviderNativeCompactionReplacement(
            ModelProviderProtocols.OpenAIResponses,
            items,
            input.CoveredMessageCount,
            input.CoveredThroughTurnId,
            estimatedTokensAfter);
    }

    internal static IReadOnlyList<JsonElement> ValidateOutput(ChatGptResponsesCompactResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Output is not { Count: > 0 } output)
        {
            throw new InvalidDataException(
                "provider_compaction_invalid_response: Compact response must contain a non-empty output array.");
        }

        var items = new List<JsonElement>(output.Count);
        foreach (var item in output)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "provider_compaction_invalid_response: Compact output items must be JSON objects.");
            }
            items.Add(item.Clone());
        }
        return items;
    }
}
