using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;

#pragma warning disable OPENAI001

namespace DotCraft.Agents;

internal static class ChatGptResponsesCompactEligibility
{
    public static bool IsEligible(
        Configuration.EffectiveModelRuntime runtime,
        HistoryMode historyMode,
        int providerHistorySchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return runtime.IsChatGptOAuth
               && runtime.IsOpenAIResponses
               && historyMode == HistoryMode.Server
               && providerHistorySchemaVersion == ProviderHistorySchema.CurrentSchemaVersion;
    }
}

internal static class ChatGptResponsesCompactRequestBuilder
{
    public static ChatGptResponsesCompactRequest Build(
        string model,
        ProviderCompactionInput input,
        IReadOnlyList<ChatMessage> neutralHistory,
        ChatOptions? options,
        IChatClient? rawRepresentationClient = null)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model must be configured.", nameof(model));
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(neutralHistory);

        var canonicalInput = JsonSerializer.SerializeToNode(
                                 input.Items,
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
        var compact = JsonSerializer.Deserialize<ChatGptResponsesCompactRequest>(
                          ordinaryBody.ToMemory().Span,
                          ChatGptResponsesCompactJson.Options)
                      ?? throw new InvalidDataException(
                          "provider_compaction_invalid_request: Responses mapper produced an empty request body.");

        return compact with
        {
            Model = model.Trim(),
            Input = input.Items.Select(static item => item.Clone()).ToList(),
            Instructions = string.IsNullOrWhiteSpace(compact.Instructions)
                ? null
                : compact.Instructions,
            Tools = compact.Tools is { Count: > 0 } ? compact.Tools : null
        };
    }
}

internal sealed class ChatGptResponsesCompactBackend(
    string model,
    IChatGptResponsesCompactTransport transport,
    Func<long, CompactionThreshold> evaluateThreshold,
    IChatClient? rawRepresentationClient = null) : ICompactionBackend
{
    public string Id => CompactionBackendIds.ChatGptResponsesCompact;

    public async Task<CompactionExecutionResult> ExecuteAsync(
        CompactionExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var bridge = request.ProviderBridge
            ?? throw new InvalidOperationException(
                "provider_compaction_unavailable: Active provider history does not support native compaction.");
        var input = await bridge.CaptureCompactionInputAsync(
                request.Phase,
                request.NeutralHistory,
                request.Options,
                cancellationToken)
            .ConfigureAwait(false);
        var before = evaluateThreshold(request.InputTokenHint);
        if (input.Items.Count == 0)
        {
            var skipped = new CompactionStatus(
                request.Trigger == CompactionTrigger.Auto && before.AboveBlocking
                    ? CompactionOutcome.Failed
                    : CompactionOutcome.Skipped,
                ToInt(request.InputTokenHint),
                ToInt(request.InputTokenHint),
                before,
                before,
                FailureReason: "provider_compaction_empty_input");
            return new CompactionExecutionResult(skipped, Id, Replacement: null);
        }

        var body = ChatGptResponsesCompactRequestBuilder.Build(
            model,
            input,
            request.NeutralHistory,
            request.Options,
            rawRepresentationClient);
        var response = await transport.CompactAsync(body, cancellationToken).ConfigureAwait(false);
        var output = ValidateOutput(response);
        var estimatedTokensAfter = bridge.EstimateNativeContextTokens(
            new ProviderNativeSnapshot(
                output,
                input.CoveredMessageCount,
                input.CoveredThroughTurnId),
            pendingTail: [],
            request.Options);
        var after = evaluateThreshold(estimatedTokensAfter);
        var status = new CompactionStatus(
            CompactionOutcome.Partial,
            ToInt(request.InputTokenHint),
            ToInt(estimatedTokensAfter),
            before,
            after);
        return new CompactionExecutionResult(
            status,
            Id,
            new CompactionReplacement.ProviderNative(
                ProviderHistorySchema.OpenAIResponsesProtocol,
                output,
                input.CoveredMessageCount,
                input.CoveredThroughTurnId,
                estimatedTokensAfter));
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

    private static int ToInt(long value) =>
        (int)Math.Clamp(value, 0, int.MaxValue);
}
