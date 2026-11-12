using System.Text.Json.Nodes;

namespace DotCraft.McpTestServer;

internal static class EnvironmentMcpFixture
{
    public static async Task RunAsync()
    {
        while (await Console.In.ReadLineAsync() is { } line)
        {
            var request = JsonNode.Parse(line)!.AsObject();
            var response = request["method"]?.GetValue<string>() switch
            {
                "tools/list" => McpFixtureProtocol.Result(request, new JsonObject
                {
                    ["tools"] = new JsonArray(new JsonObject
                    {
                        ["name"] = "environment",
                        ["inputSchema"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject
                            {
                                ["names"] = new JsonObject
                                {
                                    ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }
                                }
                            },
                            ["required"] = new JsonArray("names")
                        }
                    })
                }),
                "tools/call" => ReadEnvironment(request),
                _ => McpFixtureProtocol.Respond(request)
            };
            if (response is not null)
                await Console.Out.WriteLineAsync(response.ToJsonString());
        }
    }

    private static JsonObject ReadEnvironment(JsonObject request)
    {
        var values = new JsonObject();
        foreach (var name in request["params"]!["arguments"]!["names"]!.AsArray())
        {
            var key = name!.GetValue<string>();
            values[key] = Environment.GetEnvironmentVariable(key);
        }
        return McpFixtureProtocol.Result(request, new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = values.ToJsonString() }),
            ["structuredContent"] = values
        });
    }
}
