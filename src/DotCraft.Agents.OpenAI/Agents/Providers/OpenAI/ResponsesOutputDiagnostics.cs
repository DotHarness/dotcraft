using System.ClientModel.Primitives;
using System.Text.Json;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace DotCraft.Agents;

internal sealed class ResponsesOutputDiagnostics
{
    internal sealed record Summary(string[] Types, int TextLength);

    private readonly SortedDictionary<int, ResponseItem> _items = new();

    public void Observe(StreamingResponseUpdate update)
    {
        if (update is StreamingResponseOutputItemDoneUpdate done && done.Item != null)
            _items[done.OutputIndex] = done.Item;
    }

    public Summary? Summarize(ResponseResult? response)
    {
        if (response == null)
            return null;
        try
        {
            var types = new List<string>();
            var length = 0;
            foreach (var item in _items.Count > 0 ? _items.Values.AsEnumerable() : response.OutputItems)
            {
                using var json = JsonDocument.Parse(ModelReaderWriter.Write(item).ToString());
                var root = json.RootElement;
                types.Add(root.GetProperty("type").GetString() ?? "unknown");
                if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in content.EnumerateArray())
                    {
                        if (part.TryGetProperty("type", out var type) && type.GetString() == "output_text"
                            && part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                            length += text.GetString()?.Length ?? 0;
                    }
                }
            }
            return new Summary(types.ToArray(), length);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
