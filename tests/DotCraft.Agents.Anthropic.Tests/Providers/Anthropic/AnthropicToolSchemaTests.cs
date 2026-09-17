using System.Text.Json;
using Anthropic.Models.Beta.Messages;
using DotCraft.Agents;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class AnthropicToolSchemaTests
{
    [Fact]
    public void A_deferred_tool_loses_the_schema_shapes_Anthropic_refuses()
    {
        var schema = Schema("""
            {
              "type": "object",
              "oneOf": [{ "required": ["a"] }, { "required": ["b"] }],
              "properties": { "a": { "type": "string" }, "b": { "type": "string" } },
              "unevaluatedProperties": false
            }
            """);

        var input = InputSchemaOf(AnthropicDeferredToolLoadingChatClient.CreateDeferredTool(Tool(schema)));

        Assert.False(input.TryGetProperty("oneOf", out _));
        Assert.True(input.TryGetProperty("anyOf", out _));
        Assert.False(input.TryGetProperty("unevaluatedProperties", out _));
    }

    [Fact]
    public void What_the_schema_loses_is_recorded_in_the_description()
    {
        var schema = Schema("""
            { "type": "object", "properties": { "a": { "type": "string" } }, "minProperties": 2 }
            """);

        var input = InputSchemaOf(AnthropicDeferredToolLoadingChatClient.CreateDeferredTool(Tool(schema)));

        Assert.Contains("minProperties", input.GetProperty("description").GetString()!, StringComparison.Ordinal);
    }

    private static JsonElement InputSchemaOf(AITool tool)
    {
        var betaTool = Assert.IsType<BetaTool>(
            Assert.IsType<BetaToolUnion>(tool.GetService(typeof(BetaToolUnion))).Value);
        return JsonSerializer.SerializeToElement(betaTool.InputSchema);
    }

    private static JsonElement Schema(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static AITool Tool(JsonElement schema) =>
        AIFunctionFactory.CreateDeclaration("Probe", "A probe.", schema);
}
