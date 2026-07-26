using System.Text.Json.Nodes;

namespace DotCraft.AppServerTestClient;

internal static class SmokeModelPreference
{
    public static JsonObject Create(string model) => new()
    {
        ["model"] = model,
        ["reasoning"] = new JsonObject
        {
            ["enabled"] = false,
            ["effort"] = "medium",
            ["output"] = "full"
        },
        ["speed"] = "standard",
        ["contextWindow"] = new JsonObject { ["mode"] = "default" }
    };
}
