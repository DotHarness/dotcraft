using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.AppServer;
using Xunit;

namespace DotCraft.Core.Tests.Protocol.AppServer;

public sealed class McpElicitationSchemaValidatorTests
{
    [Fact]
    public void SupportsCurrentMcpPrimitiveAndEnumSchemas()
    {
        var schema = JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "email": { "type": "string", "format": "email", "minLength": 3 },
            "count": { "type": "integer", "minimum": 1, "maximum": 5 },
            "color": { "type": "string", "oneOf": [
              { "const": "#f00", "title": "Red" },
              { "const": "#0f0", "title": "Green" }
            ]},
            "tags": { "type": "array", "minItems": 1, "maxItems": 2, "items": {
              "anyOf": [
                { "const": "a", "title": "Alpha" },
                { "const": "b", "title": "Beta" }
              ]
            }}
          },
          "required": ["email", "tags"]
        }
        """)!.AsObject();

        Assert.True(McpElicitationSchemaValidator.TryValidateSchema(schema, out var normalized));
        Assert.True(McpElicitationSchemaValidator.TryValidateContent(normalized, new Dictionary<string, JsonElement>
        {
            ["email"] = Element("\"user@example.com\""),
            ["count"] = Element("3"),
            ["color"] = Element("\"#f00\""),
            ["tags"] = Element("[\"a\",\"b\"]")
        }));
    }

    [Theory]
    [InlineData("{\"type\":\"object\",\"properties\":{\"nested\":{\"type\":\"object\",\"properties\":{}}}}")]
    [InlineData("{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"string\",\"unknown\":true}}}")]
    [InlineData("{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"array\",\"items\":{\"type\":\"object\"}}}}")]
    public void RejectsUnsupportedSchemaShapes(string json)
    {
        Assert.False(McpElicitationSchemaValidator.TryValidateSchema(JsonNode.Parse(json)!.AsObject(), out _));
    }

    [Fact]
    public void RejectsInvalidAcceptedContent()
    {
        var schema = JsonNode.Parse("""
        { "type": "object", "properties": {
            "choice": { "type": "string", "enum": ["a", "b"] }
          }, "required": ["choice"] }
        """)!.AsObject();

        Assert.True(McpElicitationSchemaValidator.TryValidateSchema(schema, out var normalized));
        Assert.False(McpElicitationSchemaValidator.TryValidateContent(normalized, new Dictionary<string, JsonElement>
        {
            ["choice"] = Element("\"c\"")
        }));
        Assert.False(McpElicitationSchemaValidator.TryValidateContent(normalized, new Dictionary<string, JsonElement>()));
    }

    private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
