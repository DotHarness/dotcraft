using System.Text.Json.Nodes;

namespace DotCraft.McpTestServer;

internal static class StatefulMcpFixture
{
    public static async Task RunAsync()
    {
        var session = Guid.NewGuid().ToString("N");
        var value = 0;
        while (await Console.In.ReadLineAsync() is { } line)
        {
            var request = JsonNode.Parse(line)!.AsObject();
            var method = request["method"]?.GetValue<string>();
            if (request["id"] is null) continue;
            JsonObject? result = method switch
            {
                "server/discover" => new JsonObject
                {
                    ["type"] = "complete", ["supportedVersions"] = new JsonArray("2026-07-28"),
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "stateful-fixture", ["version"] = "1" },
                    ["ttlMs"] = 0, ["cacheScope"] = "private"
                },
                "initialize" => new JsonObject
                {
                    ["protocolVersion"] = request["params"]?["protocolVersion"]?.DeepClone(),
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "stateful-fixture", ["version"] = "1" }
                },
                "tools/list" => new JsonObject
                {
                    ["tools"] = new JsonArray(new JsonObject
                    {
                        ["name"] = "state", ["description"] = "Read or update session state",
                        ["inputSchema"] = new JsonObject
                        {
                            ["type"] = "object", ["properties"] = new JsonObject
                            {
                                ["value"] = new JsonObject { ["type"] = "integer" }
                            }
                        }
                    })
                },
                "tools/call" => State(),
                _ => null
            };
            var response = result is null
                ? new JsonObject
                {
                    ["jsonrpc"] = "2.0", ["id"] = request["id"]!.DeepClone(),
                    ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Method not found" }
                }
                : McpFixtureProtocol.Result(request, result);
            await Console.Out.WriteLineAsync(response.ToJsonString());

            JsonObject State()
            {
                if (request["params"]?["arguments"]?["value"] is JsonValue next)
                    value = next.GetValue<int>();
                return new JsonObject
                {
                    ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = $"{session}:{value}" }),
                    ["structuredContent"] = new JsonObject
                    {
                        ["session"] = session, ["value"] = value, ["pid"] = Environment.ProcessId
                    }
                };
            }
        }
    }
}
