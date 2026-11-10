using System.Text.Json.Nodes;

namespace DotCraft.McpTestServer;

public static class McpFixtureProtocol
{
    public static JsonObject? Respond(JsonObject request, bool legacy = false)
    {
        var method = request["method"]?.GetValue<string>();
        if (method?.StartsWith("notifications/", StringComparison.Ordinal) == true)
            return null;
        var result = method switch
        {
            "server/discover" when !legacy => new JsonObject
            {
                ["type"] = "complete", ["supportedVersions"] = new JsonArray("2026-07-28"),
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = "mcp-input-fixture", ["version"] = "1" },
                ["ttlMs"] = 0, ["cacheScope"] = "private"
            },
            "initialize" => new JsonObject
            {
                ["protocolVersion"] = request["params"]?["protocolVersion"]?.DeepClone(),
                ["serverInfo"] = new JsonObject { ["name"] = "mcp-input-fixture", ["version"] = "1" },
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() }
            },
            "tools/list" => new JsonObject
            {
                ["tools"] = new JsonArray(new JsonObject
                {
                    ["name"] = "choose", ["description"] = "Choose an answer",
                    ["inputSchema"] = new JsonObject
                    {
                        ["type"] = "object", ["properties"] = new JsonObject
                        {
                            ["owner"] = new JsonObject { ["type"] = "string" },
                            ["count"] = new JsonObject { ["type"] = "integer" }
                        },
                        ["required"] = new JsonArray("owner")
                    }
                })
            },
            "tools/call" => ToolResult(request["params"]!.AsObject()),
            _ => null
        };
        return result is null
            ? new JsonObject
            {
                ["jsonrpc"] = "2.0", ["id"] = request["id"]?.DeepClone(),
                ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Method not found" }
            }
            : Result(request, result);
    }

    public static JsonObject Result(JsonObject request, JsonObject result) => new()
    {
        ["jsonrpc"] = "2.0", ["id"] = request["id"]?.DeepClone(), ["result"] = result
    };

    public static JsonObject Input(string message) => new()
    {
        ["mode"] = "form", ["message"] = message,
        ["requestedSchema"] = new JsonObject
        {
            ["type"] = "object", ["properties"] = new JsonObject { ["answer"] = new JsonObject { ["type"] = "string" } },
            ["required"] = new JsonArray("answer")
        }
    };

    private static JsonObject ToolResult(JsonObject parameters)
    {
        var owner = parameters["arguments"]!["owner"]!.GetValue<string>();
        var count = parameters["arguments"]?["count"]?.GetValue<int>() ?? 1;
        if (parameters["inputResponses"] is JsonObject responses)
        {
            if (parameters["requestState"]?.GetValue<string>() != owner)
                throw new InvalidOperationException("Continuation state belongs to another call.");
            var answers = new JsonArray(responses.Select(pair => pair.Value?["content"]?["answer"]?.DeepClone()).ToArray());
            return new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = $"{owner}:{answers.ToJsonString()}" }),
                ["structuredContent"] = new JsonObject
                {
                    ["owner"] = owner, ["answers"] = answers,
                    ["clientCapabilities"] = parameters["_meta"]?["io.modelcontextprotocol/clientCapabilities"]?.DeepClone()
                }
            };
        }
        var inputs = new JsonObject();
        for (var index = 0; index < count; index++)
            inputs[$"input-{index}"] = new JsonObject { ["method"] = "elicitation/create", ["params"] = Input($"{owner}:{index}") };
        return new JsonObject { ["resultType"] = "input_required", ["requestState"] = owner, ["inputRequests"] = inputs };
    }
}
