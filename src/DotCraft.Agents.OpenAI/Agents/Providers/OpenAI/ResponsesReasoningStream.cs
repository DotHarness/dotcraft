using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001, MEAI001

namespace DotCraft.Agents;

internal sealed class ResponsesReasoningStream
{
    private readonly Dictionary<int, string> _itemIdsByOutputIndex = [];
    private readonly Dictionary<int, ReasoningState> _reasoning = [];

    public async IAsyncEnumerable<StreamingResponseUpdate> TrackAsync(
        IAsyncEnumerable<StreamingResponseUpdate> updates,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            Observe(update);
            yield return update;
        }
    }

    public void Apply(ChatResponseUpdate update)
    {
        if (!TryGetReasoningEvent(
                update.RawRepresentation,
                out var outputIndex,
                out var eventItemId))
            return;

        // Reasoning after a tool result must start an Assistant message without promoting the item ID.
        update.Role = ChatRole.Assistant;

        var state = GetState(outputIndex);
        var hasItemId = TryResolveItemId(outputIndex, eventItemId, out var providerItemId);
        if (update.RawRepresentation is StreamingResponseOutputItemDoneUpdate)
        {
            if (!update.Contents.OfType<TextReasoningContent>().Any())
                update.Contents.Add(new TextReasoningContent(string.Empty));
            if (!state.HasText)
            {
                var native = (JsonObject)state.Envelope["item"]!;
                var text = string.Concat(new[] { "summary", "content" }
                    .SelectMany(key => (native[key] as JsonArray ?? []).OfType<JsonObject>())
                    .Select(part => part["text"]?.GetValue<string>()));
                update.Contents.OfType<TextReasoningContent>().First().Text = text;
            }
        }

        foreach (var reasoning in update.Contents.OfType<TextReasoningContent>())
        {
            state.HasText |= !string.IsNullOrEmpty(reasoning.Text);
            reasoning.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            reasoning.AdditionalProperties[ResponsesReasoningMetadata.Key] = state.Envelope;
            reasoning.AdditionalProperties[AgentResponseAggregation.ReasoningGroupKey] = state.Group;
            if (hasItemId)
                OpenAIResponsesItemIdentity.PreserveProviderItemId(reasoning, providerItemId);
        }
    }

    private void Observe(StreamingResponseUpdate update)
    {
        switch (update)
        {
            case StreamingResponseOutputItemAddedUpdate added:
                RecordItemId(added.OutputIndex, added.Item?.Id);
                if (added.Item is ReasoningResponseItem)
                    GetState(added.OutputIndex);
                break;
            case StreamingResponseOutputItemDoneUpdate done:
                if (!RecordItemId(done.OutputIndex, done.Item?.Id) && done.Item != null
                    && _itemIdsByOutputIndex.TryGetValue(done.OutputIndex, out var providerItemId))
                {
                    done.Item.Id = providerItemId;
                }
                if (done.Item is ReasoningResponseItem item)
                    GetState(done.OutputIndex).Envelope["item"] = ResponsesReasoningMetadata.ReadNative(item);
                break;
            case StreamingResponseReasoningSummaryPartAddedUpdate reasoning:
                RecordItemId(reasoning.OutputIndex, reasoning.ItemId);
                break;
            case StreamingResponseReasoningSummaryPartDoneUpdate reasoning:
                RecordItemId(reasoning.OutputIndex, reasoning.ItemId);
                break;
            case StreamingResponseReasoningSummaryTextDeltaUpdate reasoning:
                RecordItemId(reasoning.OutputIndex, reasoning.ItemId);
                AppendText(reasoning.OutputIndex, "summary", reasoning.SummaryIndex, "summary_text", reasoning.Delta);
                break;
            case StreamingResponseReasoningSummaryTextDoneUpdate reasoning:
                RecordItemId(reasoning.OutputIndex, reasoning.ItemId);
                break;
            case StreamingResponseReasoningTextDeltaUpdate reasoning:
                RecordItemId(reasoning.OutputIndex, reasoning.ItemId);
                AppendText(reasoning.OutputIndex, "content", reasoning.ContentIndex, "reasoning_text", reasoning.Delta);
                break;
            case StreamingResponseReasoningTextDoneUpdate reasoning:
                RecordItemId(reasoning.OutputIndex, reasoning.ItemId);
                break;
        }
    }

    private ReasoningState GetState(int outputIndex)
    {
        if (!_reasoning.TryGetValue(outputIndex, out var state))
            _reasoning.Add(outputIndex, state = new ReasoningState());
        if (_itemIdsByOutputIndex.TryGetValue(outputIndex, out var itemId))
            state.Envelope["item"]!["id"] = itemId;
        return state;
    }

    private void AppendText(int outputIndex, string field, int index, string type, string text)
    {
        var parts = (JsonArray)GetState(outputIndex).Envelope["item"]![field]!;
        while (parts.Count <= index)
            parts.Add(new JsonObject { ["type"] = type, ["text"] = string.Empty });
        var part = parts[index]!;
        part["text"] = part["text"]!.GetValue<string>() + text;
    }

    private sealed class ReasoningState
    {
        public JsonObject Envelope { get; } = ResponsesReasoningMetadata.CreateEnvelope();
        public string Group { get; } = Guid.NewGuid().ToString("N");
        public bool HasText { get; set; }
    }

    private static bool TryGetReasoningEvent(
        object? rawRepresentation,
        out int outputIndex,
        out string? providerItemId)
    {
        switch (rawRepresentation)
        {
            case StreamingResponseOutputItemAddedUpdate
            {
                Item: ReasoningResponseItem item
            } added:
                outputIndex = added.OutputIndex;
                providerItemId = item.Id;
                return true;
            case StreamingResponseOutputItemDoneUpdate
            {
                Item: ReasoningResponseItem item
            } done:
                outputIndex = done.OutputIndex;
                providerItemId = item.Id;
                return true;
            case StreamingResponseReasoningSummaryPartAddedUpdate reasoning:
                outputIndex = reasoning.OutputIndex;
                providerItemId = reasoning.ItemId;
                return true;
            case StreamingResponseReasoningSummaryPartDoneUpdate reasoning:
                outputIndex = reasoning.OutputIndex;
                providerItemId = reasoning.ItemId;
                return true;
            case StreamingResponseReasoningSummaryTextDeltaUpdate reasoning:
                outputIndex = reasoning.OutputIndex;
                providerItemId = reasoning.ItemId;
                return true;
            case StreamingResponseReasoningSummaryTextDoneUpdate reasoning:
                outputIndex = reasoning.OutputIndex;
                providerItemId = reasoning.ItemId;
                return true;
            case StreamingResponseReasoningTextDeltaUpdate reasoning:
                outputIndex = reasoning.OutputIndex;
                providerItemId = reasoning.ItemId;
                return true;
            case StreamingResponseReasoningTextDoneUpdate reasoning:
                outputIndex = reasoning.OutputIndex;
                providerItemId = reasoning.ItemId;
                return true;
            default:
                outputIndex = default;
                providerItemId = null;
                return false;
        }
    }

    private bool RecordItemId(int outputIndex, string? providerItemId)
    {
        if (!OpenAIResponsesItemIdentity.IsValid(providerItemId))
            return false;

        _itemIdsByOutputIndex[outputIndex] = providerItemId!.Trim();
        return true;
    }

    private bool TryResolveItemId(
        int outputIndex,
        string? providerItemId,
        out string resolvedItemId)
    {
        if (OpenAIResponsesItemIdentity.IsValid(providerItemId))
        {
            resolvedItemId = providerItemId!.Trim();
            return true;
        }

        return _itemIdsByOutputIndex.TryGetValue(outputIndex, out resolvedItemId!);
    }
}
