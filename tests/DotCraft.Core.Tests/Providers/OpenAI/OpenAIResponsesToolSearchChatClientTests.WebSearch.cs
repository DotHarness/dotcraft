using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Xunit;

#pragma warning disable OPENAI001, MEAI001

namespace DotCraft.Tests.Agents;

public sealed partial class OpenAIResponsesToolSearchChatClientTests
{
    [Theory]
    [InlineData("{\"type\":\"search\",\"query\":\"weather\",\"queries\":[\"weather\",\"forecast\"]}", false)]
    [InlineData("{\"type\":\"search\",\"query\":\"weather\",\"queries\":[\"weather\",\"forecast\"]}", true)]
    [InlineData("{\"type\":\"open_page\",\"url\":\"https://example.invalid\"}", false)]
    [InlineData("{\"type\":\"find_in_page\",\"url\":\"https://example.invalid\",\"pattern\":\"forecast\"}", false)]
    [InlineData(null, false)]
    public async Task WebSearchReplay_PreservesOneNativeItemThroughStreamingAndJsonHistory(
        string? actionJson, bool progress)
    {
        var native = NativeWebSearch("ws_search", actionJson);
        using var client = CreateClient(new FakeChatClient(new ChatResponse([])),
            new FakeToolSearchTransport(WebSearchEvents(native, 0, progress)));

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "search")]);

        var message = Assert.Single(response.Messages);
        Assert.Equal(ChatRole.Assistant, message.Role);
        Assert.Null(message.MessageId);
        var result = Assert.Single(message.Contents.OfType<WebSearchToolResultContent>());
        Assert.True(result.AdditionalProperties!.ContainsKey(ResponsesWebSearchMetadata.Key));
        var call = Assert.Single(message.Contents.OfType<WebSearchToolCallContent>());
        Assert.False(call.AdditionalProperties?.ContainsKey(ResponsesWebSearchMetadata.Key) == true);

        var restored = ModelHistoryJson.Decode(ModelHistoryJson.Encode(message));
        using var request = JsonDocument.Parse(CreateRequestJson("gpt-test", [restored], null));
        var replay = Assert.Single(request.RootElement.GetProperty("input").EnumerateArray());
        AssertWebSearchReplay(native, replay);
        Assert.DoesNotContain(ResponsesWebSearchMetadata.Key, request.RootElement.GetRawText());
        Assert.DoesNotContain("Unsupported content", request.RootElement.GetRawText());
    }

    [Fact]
    public async Task WebSearchReplay_AdjacentSearchesPreserveTheirOrder()
    {
        var first = NativeWebSearch("ws_first", "{\"type\":\"search\",\"query\":\"first\"}");
        var second = NativeWebSearch("ws_second", "{\"type\":\"open_page\",\"url\":\"https://example.invalid\"}");
        using var client = CreateClient(new FakeChatClient(new ChatResponse([])),
            new FakeToolSearchTransport([
                .. WebSearchEvents(first, 0, true),
                .. WebSearchEvents(second, 1, true)
            ]));
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "search")]);
        var restored = response.Messages.Select(message => ModelHistoryJson.Decode(ModelHistoryJson.Encode(message)));

        using var request = JsonDocument.Parse(CreateRequestJson("gpt-test", restored, null));

        var items = request.RootElement.GetProperty("input").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        AssertWebSearchReplay(first, items[0]);
        AssertWebSearchReplay(second, items[1]);
    }

    [Fact]
    public async Task WebSearchReplay_ProgressWithoutResultDoesNotCreateSyntheticOutput()
    {
        using var client = CreateClient(new FakeChatClient(new ChatResponse([])),
            new FakeToolSearchTransport(WebSearchProgress("ws_interrupted", 0)));
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "search")]);
        var message = Assert.Single(response.Messages);
        Assert.Equal(ChatRole.Assistant, message.Role);
        Assert.Single(message.Contents.OfType<WebSearchToolCallContent>());
        Assert.Empty(message.Contents.OfType<WebSearchToolResultContent>());
        var restored = ModelHistoryJson.Decode(ModelHistoryJson.Encode(message));

        using var request = JsonDocument.Parse(CreateRequestJson("gpt-test", [restored], null));

        Assert.Empty(request.RootElement.GetProperty("input").EnumerateArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WebSearchReplay_AfterLocalToolResultRemainsAssistantAuthored(bool lite)
    {
        var native = NativeWebSearch("ws_after_tool", "{\"type\":\"search\",\"query\":\"weather\"}");
        using var provider = CreateUsageClient(lite,
            [CreateFunctionCallDoneUpdate(1, 0, "fc_lookup", "call_lookup", "lookup")],
            WebSearchEvents(native, 0, true));
        using var loop = new StreamingFunctionInvokingChatClient(provider);
        var executions = 0;
        var tool = AIFunctionFactory.Create(() => { executions++; return "value"; }, name: "lookup");

        var response = await loop.GetResponseAsync([new ChatMessage(ChatRole.User, "look up and search")],
            new ChatOptions { Tools = [tool] });

        var message = Assert.Single(response.Messages,
            candidate => candidate.Contents.OfType<WebSearchToolResultContent>().Any());
        Assert.Equal(ChatRole.Assistant, message.Role);
        Assert.Equal(1, executions);
        Assert.Single(response.Messages.SelectMany(candidate => candidate.Contents).OfType<FunctionCallContent>());
        var restored = response.Messages.Select(candidate => ModelHistoryJson.Decode(ModelHistoryJson.Encode(candidate)));
        using var request = JsonDocument.Parse(CreateRequestJson("gpt-test", restored, null));
        var search = Assert.Single(request.RootElement.GetProperty("input").EnumerateArray(),
            item => item.GetProperty("type").GetString() == "web_search_call");
        AssertWebSearchReplay(native, search);
    }

    [Fact]
    public void WebSearchReplay_InMemoryNativeResultDoesNotNeedAnEnvelope()
    {
        var native = NativeWebSearch("ws_raw", "{\"type\":\"search\",\"query\":\"weather\"}");
        var raw = ModelReaderWriter.Read<ResponseItem>(BinaryData.FromString(native.ToJsonString()), ModelReaderWriterOptions.Json)!;
        var result = new WebSearchToolResultContent("ws_raw") { RawRepresentation = raw };

        using var request = JsonDocument.Parse(CreateRequestJson("gpt-test",
            [new ChatMessage(ChatRole.Assistant, [new WebSearchToolCallContent("ws_raw"), result])], null));

        AssertWebSearchReplay(native, Assert.Single(request.RootElement.GetProperty("input").EnumerateArray()));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"version\":2,\"item\":{\"type\":\"web_search_call\"}}")]
    [InlineData("{\"version\":1,\"item\":{\"type\":\"function_call\"}}")]
    [InlineData("{\"version\":1,\"item\":{\"type\":\"web_search_call\",\"id\":\"ws_other\"}}")]
    [InlineData("{\"version\":1,\"item\":{\"type\":\"web_search_call\",\"action\":\"secret\"}}")]
    [InlineData("{\"version\":1,\"item\":{\"type\":\"web_search_call\",\"encrypted_content\":42}}")]
    public void WebSearchReplay_InvalidMetadataDoesNotFallBackToRawOrExposeItsContents(string metadata)
    {
        var raw = ModelReaderWriter.Read<ResponseItem>(
            BinaryData.FromString(NativeWebSearch("ws_search", null).ToJsonString()), ModelReaderWriterOptions.Json)!;
        var result = new WebSearchToolResultContent("ws_search")
        {
            RawRepresentation = raw,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ResponsesWebSearchMetadata.Key] = JsonSerializer.Deserialize<JsonElement>(metadata)
            }
        };

        var error = Assert.Throws<InvalidDataException>(() => CreateRequestJson("gpt-test",
            [new ChatMessage(ChatRole.Assistant, [result])], null));

        Assert.StartsWith("responses_web_search_metadata_invalid:", error.Message);
        Assert.DoesNotContain("secret", error.ToString());
    }

    [Fact]
    public void WebSearchReplay_ResultWithoutReplayDataDoesNotGuessFromOutputs()
    {
        var result = new WebSearchToolResultContent("ws_missing")
        {
            Outputs = [new UriContent(new Uri("https://example.invalid"), "text/html")]
        };

        var error = Assert.Throws<InvalidDataException>(() => CreateRequestJson("gpt-test",
            [new ChatMessage(ChatRole.Assistant, [result])], null));

        Assert.StartsWith("responses_web_search_metadata_invalid:", error.Message);
        Assert.DoesNotContain("https://example.invalid", error.ToString());
    }

    private static JsonObject NativeWebSearch(string id, string? actionJson) => new()
    {
        ["type"] = "web_search_call",
        ["id"] = id,
        ["status"] = "completed",
        ["action"] = actionJson is null ? null : JsonNode.Parse(actionJson),
        ["encrypted_content"] = "opaque-search-state"
    };

    private static StreamingResponseUpdate[] WebSearchEvents(JsonObject item, int outputIndex, bool progress)
    {
        var done = CreateStreamingUpdate(new JsonObject
        {
            ["type"] = "response.output_item.done",
            ["sequence_number"] = outputIndex * 10 + 3,
            ["output_index"] = outputIndex,
            ["item"] = item.DeepClone()
        }.ToJsonString());
        return progress ? [WebSearchProgress(item["id"]!.GetValue<string>(), outputIndex), done] : [done];
    }

    private static StreamingResponseUpdate WebSearchProgress(string id, int outputIndex) =>
        CreateStreamingUpdate(new JsonObject
        {
            ["type"] = "response.web_search_call.in_progress",
            ["sequence_number"] = outputIndex * 10 + 1,
            ["output_index"] = outputIndex,
            ["item_id"] = id
        }.ToJsonString());

    private static void AssertWebSearchReplay(JsonObject expected, JsonElement actual)
    {
        var raw = ModelReaderWriter.Read<ResponseItem>(BinaryData.FromString(expected.ToJsonString()), ModelReaderWriterOptions.Json)!;
        var normalized = JsonNode.Parse(ModelReaderWriter.Write(raw).ToString());
        Assert.True(JsonNode.DeepEquals(normalized, JsonNode.Parse(actual.GetRawText())), actual.GetRawText());
    }
}
