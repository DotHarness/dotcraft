using System.Text.Json.Nodes;
using DotCraft.Plugins;
using Xunit;

namespace DotCraft.Tests.Plugins;

public sealed class PluginFunctionSchemaValidatorTests
{
    [Fact]
    public void NullableGeneratedToolProperties_AcceptConcreteAndNullValues()
    {
        var schema = JsonNode.Parse(
            """
            {
              "type": "object",
              "properties": {
                "kind": { "type": ["string", "null"] },
                "enabled": { "type": ["boolean", "null"] },
                "dependencies": {
                  "type": ["array", "null"],
                  "items": { "type": "string" }
                }
              }
            }
            """)!.AsObject();

        Assert.True(PluginFunctionSchemaValidator.TryValidateSchema(schema, out var schemaMessage), schemaMessage);
        Assert.True(
            PluginFunctionSchemaValidator.TryValidateArguments(
                schema,
                new JsonObject
                {
                    ["kind"] = "review",
                    ["enabled"] = true,
                    ["dependencies"] = new JsonArray("task_1")
                },
                out var concreteMessage),
            concreteMessage);
        Assert.True(
            PluginFunctionSchemaValidator.TryValidateArguments(
                schema,
                new JsonObject
                {
                    ["kind"] = null,
                    ["enabled"] = null,
                    ["dependencies"] = null
                },
                out var nullMessage),
            nullMessage);
    }

    [Fact]
    public void NullableGeneratedToolProperty_RejectsValueOutsideUnion()
    {
        var schema = JsonNode.Parse(
            """
            {
              "type": "object",
              "properties": {
                "kind": { "type": ["string", "null"] }
              }
            }
            """)!.AsObject();

        Assert.False(
            PluginFunctionSchemaValidator.TryValidateArguments(
                schema,
                new JsonObject { ["kind"] = 42 },
                out var message));
        Assert.Contains("string, null", message, StringComparison.Ordinal);
    }
}
