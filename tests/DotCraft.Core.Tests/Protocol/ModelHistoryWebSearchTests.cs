using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class ModelHistoryWebSearchTests
{
    [Fact]
    public void Codec_RoundTripsSearchContentsAndNestedOutputPropertiesThroughJson()
    {
        var message = new ChatMessage(ChatRole.Assistant,
        [
            new WebSearchToolCallContent("ws_search")
            {
                Queries = ["first query", "second query"],
                RawRepresentation = new object(),
                AdditionalProperties = new() { ["callMeta"] = "search request" }
            },
            new WebSearchToolResultContent("ws_search")
            {
                RawRepresentation = new object(),
                Outputs =
                [
                    new TextContent("result summary") { RawRepresentation = new object() },
                    new UriContent("https://example.invalid/result", "text/html")
                    {
                        RawRepresentation = new object(),
                        AdditionalProperties = new() { ["title"] = "Search result" }
                    }
                ],
                AdditionalProperties = new()
                {
                    ["replay"] = JsonSerializer.Deserialize<JsonElement>("""{"version":1,"item":{"id":"ws_search","action":{"type":"search"}}}""")
                }
            }
        ]);
        var json = ModelHistoryJson.Encode(message).ToJsonString();
        var restored = ModelHistoryJson.Decode(JsonNode.Parse(json)!);

        Assert.DoesNotContain("rawRepresentation", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$type", json, StringComparison.Ordinal);
        Assert.Equal(ChatRole.Assistant, restored.Role);
        Assert.Equal(2, restored.Contents.Count);
        var call = Assert.IsType<WebSearchToolCallContent>(restored.Contents[0]);
        Assert.Equal("ws_search", call.CallId);
        Assert.Equal(["first query", "second query"], call.Queries);
        Assert.Equal("search request", call.AdditionalProperties!["callMeta"]);
        var result = Assert.IsType<WebSearchToolResultContent>(restored.Contents[1]);
        Assert.Equal(call.CallId, result.CallId);
        Assert.Collection(result.Outputs!,
            output => Assert.Equal("result summary", Assert.IsType<TextContent>(output).Text),
            output =>
            {
                var uri = Assert.IsType<UriContent>(output);
                Assert.Equal("https://example.invalid/result", uri.Uri.ToString());
                Assert.Equal("text/html", uri.MediaType);
                Assert.Equal("Search result", uri.AdditionalProperties!["title"]);
            });
        var replay = Assert.IsType<JsonElement>(result.AdditionalProperties!["replay"]);
        Assert.Equal(1, replay.GetProperty("version").GetInt32());
        Assert.Equal("ws_search", replay.GetProperty("item").GetProperty("id").GetString());
        Assert.Equal("search", replay.GetProperty("item").GetProperty("action").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Codec_PreservesNullAndEmptySearchCollections(bool empty)
    {
        var message = new ChatMessage(ChatRole.Assistant,
        [
            new WebSearchToolCallContent("ws_partial") { Queries = empty ? [] : null },
            new WebSearchToolResultContent("ws_partial") { Outputs = empty ? [] : null }
        ]);

        var restored = ModelHistoryJson.Decode(ModelHistoryJson.Encode(message));

        var call = Assert.IsType<WebSearchToolCallContent>(restored.Contents[0]);
        var result = Assert.IsType<WebSearchToolResultContent>(restored.Contents[1]);
        if (empty)
        {
            Assert.Empty(call.Queries!);
            Assert.Empty(result.Outputs!);
        }
        else
        {
            Assert.Null(call.Queries);
            Assert.Null(result.Outputs);
        }
    }

    [Theory]
    [InlineData("\"outputs\":[null]")]
    [InlineData("\"outputs\":[{\"kind\":\"unknown\",\"payload\":{}}]")]
    public void Codec_RejectsMalformedSearchOutputs(string outputProperty)
    {
        var json = JsonNode.Parse($$"""
            {
              "schemaVersion":1,
              "role":"assistant",
              "contents":[{
                "kind":"web_search_result",
                "payload":{"callId":"ws_invalid",{{outputProperty}},"additionalProperties":null}
              }]
            }
            """)!;

        Assert.Throws<JsonException>(() => ModelHistoryJson.Decode(json));
    }
}
