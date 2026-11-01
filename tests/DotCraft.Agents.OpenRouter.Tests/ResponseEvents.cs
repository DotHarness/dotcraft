using System.Text.Json.Nodes;

namespace DotCraft.Tests.OpenRouter;

internal static class ResponseEvents
{
    internal static string Build(JsonObject response)
    {
        var events = new List<JsonObject>();
        var created = (JsonObject)response.DeepClone();
        created["output"] = new JsonArray(); created["status"] = "in_progress";
        events.Add(new() { ["type"] = "response.created", ["response"] = created });
        var index = 0;
        foreach (var item in response["output"]!.AsArray().OfType<JsonObject>())
        {
            var added = (JsonObject)item.DeepClone();
            if (item["type"]?.GetValue<string>() == "function_call") added["arguments"] = "";
            events.Add(new() { ["type"] = "response.output_item.added", ["output_index"] = index, ["item"] = added });
            if (item["type"]?.GetValue<string>() == "message")
                events.Add(new() { ["type"] = "response.output_text.delta", ["output_index"] = index,
                    ["item_id"] = item["id"]?.DeepClone(), ["content_index"] = 0,
                    ["delta"] = item["content"]?[0]?["text"]?.DeepClone() });
            if (item["type"]?.GetValue<string>() == "function_call")
                events.Add(new() { ["type"] = "response.function_call_arguments.delta", ["output_index"] = index,
                    ["item_id"] = item["id"]?.DeepClone(), ["delta"] = item["arguments"]?.DeepClone() });
            events.Add(new() { ["type"] = "response.output_item.done", ["output_index"] = index, ["item"] = item.DeepClone() });
            index++;
        }
        events.Add(new() { ["type"] = "response.completed", ["response"] = response });
        for (var sequence = 0; sequence < events.Count; sequence++) events[sequence]["sequence_number"] = sequence;
        return string.Concat(events.Select(value => "data: " + value.ToJsonString() + "\n\n"));
    }
}
