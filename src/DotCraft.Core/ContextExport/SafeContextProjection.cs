using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotCraft.ContextExport;

internal static partial class SafeContextProjection
{
    private static readonly string[] SensitiveKeyFragments =
    [
        "token", "secret", "password", "passwd", "credential", "authorization", "cookie",
        "private_key", "privatekey", "apikey", "api_key", "access_key", "refresh_token"
    ];

    public static string RedactJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;

        try
        {
            using var document = JsonDocument.Parse(json);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                WriteElement(writer, document.RootElement, null);
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return RedactText(json);
        }
    }

    public static string RedactText(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var redacted = BearerTokenRegex().Replace(value, "$1[redacted]");
        redacted = SecretAssignmentRegex().Replace(redacted, "$1[redacted]");
        return redacted;
    }

    public static string RedactOrOmit(string value, ContextExportToolResultMode mode)
    {
        return mode == ContextExportToolResultMode.None ? "[redacted]" : RedactText(value);
    }

    private static void WriteElement(Utf8JsonWriter writer, JsonElement element, string? propertyName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    if (IsSensitiveKey(property.Name))
                        writer.WriteStringValue("[redacted]");
                    else
                        WriteElement(writer, property.Value, property.Name);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteElement(writer, item, propertyName);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool IsSensitiveKey(string key)
    {
        var normalized = key.Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
        return SensitiveKeyFragments.Any(normalized.Contains);
    }

    [GeneratedRegex(@"(?i)(\bBearer\s+)[^\s,;]+")]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"(?i)(\b(?:token|secret|password|passwd|api[_-]?key|authorization|cookie)\s*[=:]\s*)[^\s,;]+")]
    private static partial Regex SecretAssignmentRegex();
}
