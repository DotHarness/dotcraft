using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Xunit;

#pragma warning disable OPENAI001, MEAI001, SCME0001

namespace DotCraft.Tests.Agents;

public sealed partial class OpenAIResponsesLiteChatClientTests
{
    [Fact]
    public void LiteRequestShape_DescribesTheEffectiveWireInputAndTools()
    {
        var history = JsonNode.Parse("""
            [
              {"id":"msg_first","type":"message","role":"user","content":[{"type":"input_text","text":"第一行\n\"café\" \\ <tag> & 😀"}]},
              {"id":"msg_second","type":"message","role":"user","content":[{"type":"input_text","text":"second"}]}
            ]
            """)!.AsArray();
        using var client = new TestChatClient();
        var request = OpenAIResponsesLiteRequestMapper.CreateResponseRequest(
            "gpt-test",
            [],
            new ChatOptions
            {
                MaxOutputTokens = 123,
                Tools =
                [
                    AIFunctionFactory.Create((string value) => value, name: "lookup",
                        description: "检索 “café”\n\"phrase\"")
                ],
                RawRepresentationFactory = _ =>
                {
                    var options = new CreateResponseOptions();
                    options.Patch.Set("$.client_metadata"u8, BinaryData.FromString(
                        """{"caller":"保留 \"原样\""}"""));
                    return options;
                }
            },
            history, canonicalItemIdentity: null, client, "install-1", supportsFreeformTools: true);

        using var document = JsonDocument.Parse(request.WireBody);
        var root = document.RootElement;
        var input = root.GetProperty("input");
        var tools = input[0].GetProperty("tools");
        Assert.Equal("第一行\n\"café\" \\ <tag> & 😀", input[1].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("保留 \"原样\"", root.GetProperty("client_metadata").GetProperty("caller").GetString());
        Assert.False(root.TryGetProperty("max_output_tokens", out _));

        var shape = request.Shape;
        var inputBytes = Encoding.UTF8.GetBytes(input.GetRawText());
        Assert.Equal(inputBytes.Length, shape.InputBytes);
        Assert.Equal(Hash(inputBytes), shape.InputHash);
        Assert.Equal(Hash(Encoding.UTF8.GetBytes(tools.GetRawText())), shape.ToolsHash);
        Assert.Equal(input.EnumerateArray().Select(HashWithoutItemId).ToArray(), shape.InputItemHashes);
        Assert.Equal(1, shape.InputItemIdGeneratedCount);
        Assert.Equal(123, shape.MaxOutputTokensRequested);
        Assert.False(shape.MaxOutputTokensPresentAfterOAuthRewrite);
        Assert.True(shape.MaxOutputTokensRemovedByOAuthRewrite);
    }

    [Fact]
    public void LiteWireBody_UsesTheLastDuplicateInput()
    {
        var body = OpenAIResponsesLiteRequestMapper.BuildWireBody(BinaryData.FromString(
            """{"input":[{"id":"msg_discarded"}],"input":[{"id":"msg_kept"}]}"""),
            installationId: null);

        using var document = JsonDocument.Parse(body);
        var input = Assert.Single(document.RootElement.EnumerateObject(), property => property.NameEquals("input")).Value;
        Assert.Equal("msg_kept", Assert.Single(input.EnumerateArray()).GetProperty("id").GetString());
    }

    private static string HashWithoutItemId(JsonElement item)
    {
        var fields = item.EnumerateObject().Where(static property => property.Name != "id")
            .ToDictionary(static property => property.Name, static property => property.Value);
        return Hash(JsonSerializer.SerializeToUtf8Bytes(fields));
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
