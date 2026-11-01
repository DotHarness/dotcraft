using System.Text.Json.Nodes;

namespace DotCraft.Tests.OpenRouter;

internal static class ProtocolEvents
{
    internal static string Build(string protocol, string responseJson)
    {
        var response = JsonNode.Parse(responseJson)!.AsObject();
        if (protocol == "openai-responses") return ResponseEvents.Build(response);
        if (protocol == "openai-chat-completions")
        {
            var message = response["choices"]![0]!["message"]!.AsObject();
            var tool = message["tool_calls"]![0]!.AsObject();
            var start = new JsonObject { ["role"] = "assistant", ["reasoning_details"] = message["reasoning_details"]!.DeepClone(),
                ["tool_calls"] = new JsonArray(new JsonObject { ["index"] = 0, ["id"] = tool["id"]!.DeepClone(),
                    ["type"] = "function", ["function"] = new JsonObject { ["name"] = "probe", ["arguments"] = "" } }) };
            var delta = new JsonObject { ["tool_calls"] = new JsonArray(new JsonObject { ["index"] = 0,
                ["function"] = new JsonObject { ["arguments"] = "{}" } }) };
            start["reasoning_details"]![0]!["data"] = "opaque-";
            delta["reasoning_details"] = new JsonArray(new JsonObject { ["index"] = 0, ["data"] = "reasoning-推理" });
            return Chunk(start, null) + Chunk(delta, "tool_calls") + "data: " + new JsonObject
            { ["id"] = "chatcmpl_1", ["object"] = "chat.completion.chunk", ["created"] = 1,
                ["choices"] = new JsonArray(), ["usage"] = response["usage"]!.DeepClone() }.ToJsonString() + "\n\ndata: [DONE]\n\n";
        }
        var events = new List<JsonObject>
        {
            new() { ["type"] = "message_start", ["message"] = new JsonObject { ["id"] = "msg_1", ["type"] = "message",
                ["role"] = "assistant", ["model"] = "anthropic/claude-opus-5.5", ["content"] = new JsonArray(),
                ["usage"] = new JsonObject { ["input_tokens"] = 5, ["output_tokens"] = 0 } } }
        };
        var index = 0;
        foreach (var block in response["content"]!.AsArray().OfType<JsonObject>())
        {
            var start = (JsonObject)block.DeepClone();
            if (block["type"]!.GetValue<string>() == "thinking") { start["thinking"] = ""; start["signature"] = ""; }
            events.Add(new() { ["type"] = "content_block_start", ["index"] = index, ["content_block"] = start });
            if (block["type"]!.GetValue<string>() == "thinking")
            {
                events.Add(Delta(index, "thinking_delta", "thinking", "inspect"));
                events.Add(Delta(index, "signature_delta", "signature", "opaque-signature"));
            }
            else events.Add(Delta(index, "input_json_delta", "partial_json", "{}"));
            events.Add(new() { ["type"] = "content_block_stop", ["index"] = index++ });
        }
        events.Add(new() { ["type"] = "message_delta", ["delta"] = new JsonObject { ["stop_reason"] = "tool_use" },
            ["usage"] = new JsonObject { ["output_tokens"] = 2 } });
        events.Add(new() { ["type"] = "message_stop", ["openrouter_metadata"] = response["openrouter_metadata"]?.DeepClone() });
        return string.Concat(events.Select(item => "event: " + item["type"]!.GetValue<string>() + "\ndata: " + item.ToJsonString() + "\n\n"));
    }

    private static JsonObject Delta(int index, string type, string field, string value) => new()
    { ["type"] = "content_block_delta", ["index"] = index, ["delta"] = new JsonObject { ["type"] = type, [field] = value } };
    private static string Chunk(JsonObject delta, string? finish) => "data: " + new JsonObject
    { ["id"] = "chatcmpl_1", ["object"] = "chat.completion.chunk", ["created"] = 1, ["model"] = "anthropic/claude-opus-5.5",
        ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = delta, ["finish_reason"] = finish }) }.ToJsonString() + "\n\n";
}
