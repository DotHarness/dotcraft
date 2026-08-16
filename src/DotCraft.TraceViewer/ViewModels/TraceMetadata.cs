using System.Text.Encodings.Web;
using System.Text.Json;

namespace DotCraft.TraceViewer.ViewModels;

internal sealed class TraceMetadata
{
    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private readonly Dictionary<string, string> _values;

    private TraceMetadata(Dictionary<string, string> values, string formattedJson)
    {
        _values = values;
        FormattedJson = formattedJson;
    }

    public string FormattedJson { get; }

    public string? Get(params string[] names)
    {
        foreach (var name in names)
        {
            if (_values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    public IReadOnlyList<DetailFieldItem> Promote(params (string Label, string[] Names)[] fields) =>
        fields.Select(field => new DetailFieldItem(field.Label, Get(field.Names) ?? string.Empty))
            .Where(static field => field.Value.Length > 0)
            .ToArray();

    public static TraceMetadata Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new TraceMetadata(new(StringComparer.OrdinalIgnoreCase), string.Empty);

        try
        {
            using var document = JsonDocument.Parse(json);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                    values[property.Name] = Format(property.Value, indented: false);
            }

            return new TraceMetadata(values, Format(document.RootElement, indented: true));
        }
        catch (JsonException)
        {
            return new TraceMetadata(new(StringComparer.OrdinalIgnoreCase), json);
        }
    }

    private static string Format(JsonElement value, bool indented) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null => string.Empty,
        _ => JsonSerializer.Serialize(value, indented ? IndentedJsonOptions : CompactJsonOptions),
    };
}
