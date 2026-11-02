using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Xunit;

#pragma warning disable OPENAI001

namespace DotCraft.Tests.Agents;

public sealed partial class OpenAIResponsesToolSearchChatClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReasoningDisplay_PreservesPartBoundariesWithoutChangingReplay(bool completionOnly)
    {
        var native = new JsonObject
        {
            ["type"] = "reasoning", ["id"] = "rs_display", ["encrypted_content"] = "opaque",
            ["summary"] = DisplayReasoningParts("summary_text", "", "**First**", "", "**Second**"),
            ["content"] = DisplayReasoningParts("reasoning_text", "", "", "", " Inspect inputs ", "", "*Keep* markers")
        };
        var events = DisplayReasoningEvents(native, completionOnly);
        const string expected = "**First**\n\n**Second**\n\n Inspect inputs \n\n*Keep* markers";

        using var streamingClient = CreateClient(new FakeChatClient(new ChatResponse([])),
            new FakeToolSearchTransport(events));
        var updates = await CollectStreamingAsync(streamingClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "inspect")]));
        var fragments = updates.SelectMany(update => update.Contents).OfType<TextReasoningContent>()
            .Select(fragment => fragment.Text).Where(text => !string.IsNullOrEmpty(text)).ToArray();
        string[] expectedFragments = completionOnly
            ? [expected]
            : ["**Fi", "rst**", "\n\n**Sec", "ond**", "\n\n Inspect", " inputs ", "\n\n*Keep* ", "markers"];
        Assert.Equal(expectedFragments, fragments);

        var response = updates.ToAgentResponse();
        var message = Assert.Single(response.Messages);
        Assert.Equal(expected, Assert.Single(message.Contents.OfType<TextReasoningContent>()).Text);

        var codec = new ModelHistoryCodec();
        using var request = JsonDocument.Parse(CreateRequestJson("test",
            [codec.Decode(codec.Encode(message, "turn_display"))], null));
        AssertReplay(native, Assert.Single(request.RootElement.GetProperty("input").EnumerateArray()));
    }

    private static JsonArray DisplayReasoningParts(string type, params string[] texts) =>
        new(texts.Select(text => (JsonNode)new JsonObject { ["type"] = type, ["text"] = text }).ToArray());

    private static StreamingResponseUpdate[] DisplayReasoningEvents(JsonObject native, bool completionOnly)
    {
        var events = new List<StreamingResponseUpdate>();
        var sequence = 0;
        if (!completionOnly)
        {
            foreach (var field in new[] { "summary", "content" })
            {
                var parts = (JsonArray)native[field]!;
                for (var index = 0; index < parts.Count; index++)
                {
                    var text = parts[index]!["text"]!.GetValue<string>();
                    if (field == "summary") AddPartEvent("added");
                    AddDelta(index, "");
                    if (text.Length > 0)
                    {
                        var half = text.Length / 2;
                        AddDelta(index, text[..half]);
                        AddDelta(index == 0 ? 1 : 0, "");
                        AddDelta(index, text[half..]);
                    }
                    events.Add(CreateStreamingUpdate(new JsonObject
                    {
                        ["type"] = field == "summary" ? "response.reasoning_summary_text.done" : "response.reasoning_text.done",
                        ["sequence_number"] = ++sequence, ["output_index"] = 0, ["item_id"] = "rs_display",
                        [field == "summary" ? "summary_index" : "content_index"] = index, ["text"] = text
                    }.ToJsonString()));
                    if (field == "summary") AddPartEvent("done");

                    void AddPartEvent(string phase) => events.Add(CreateStreamingUpdate(new JsonObject
                    {
                        ["type"] = $"response.reasoning_summary_part.{phase}", ["sequence_number"] = ++sequence,
                        ["output_index"] = 0, ["item_id"] = "rs_display", ["summary_index"] = index,
                        ["part"] = parts[index]!.DeepClone()
                    }.ToJsonString()));

                    void AddDelta(int partIndex, string delta)
                    {
                        events.Add(field == "summary"
                            ? new StreamingResponseReasoningSummaryTextDeltaUpdate
                            {
                                SequenceNumber = ++sequence, OutputIndex = 0, ItemId = "rs_display",
                                SummaryIndex = partIndex, Delta = delta
                            }
                            : new StreamingResponseReasoningTextDeltaUpdate
                            {
                                SequenceNumber = ++sequence, OutputIndex = 0, ItemId = "rs_display",
                                ContentIndex = partIndex, Delta = delta
                            });
                    }
                }
            }
        }
        events.Add(CreateStreamingUpdate(new JsonObject
        {
            ["type"] = "response.output_item.done", ["sequence_number"] = ++sequence,
            ["output_index"] = 0, ["item"] = native.DeepClone()
        }.ToJsonString()));
        return events.ToArray();
    }
}
