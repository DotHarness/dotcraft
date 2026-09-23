using System.Globalization;
using System.Text.Json;

namespace DotCraft.SessionImport;

internal static class JsonFields
{
    public static JsonElement ReadProperty(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    public static string? ReadString(this JsonElement element, string name) =>
        element.ReadProperty(name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    public static bool ReadBool(this JsonElement element, string name) =>
        element.ReadProperty(name).ValueKind == JsonValueKind.True;

    public static bool TryReadProperty(this JsonElement element, string name, JsonValueKind kind, out JsonElement value)
    {
        value = element.ReadProperty(name);
        if (value.ValueKind == kind)
            return true;

        value = default;
        return false;
    }

    public static long? ReadInt64(this JsonElement element, string name) =>
        element.ReadProperty(name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var number)
            ? number
            : null;

    public static DateTimeOffset? ReadTimestamp(this JsonElement record)
    {
        if (record.ReadString("timestamp") is { } text
            && DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        return record.ReadInt64("timestamp_ms") is { } milliseconds
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : null;
    }
}
