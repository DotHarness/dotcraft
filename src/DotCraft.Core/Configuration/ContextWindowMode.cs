using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Configuration;

/// <summary>
/// Context-window mode for workspace defaults and per-thread configuration.
/// </summary>
[JsonConverter(typeof(ContextWindowModeJsonConverter))]
public enum ContextWindowMode
{
    /// <summary>
    /// Use normal configured compaction behavior, including the inferred-window cap.
    /// </summary>
    Default,

    /// <summary>
    /// Use the explicit model-catalog context window when it is larger than the configured default.
    /// </summary>
    Max
}

/// <summary>
/// Reads/writes stable camelCase context-window mode names.
/// </summary>
public sealed class ContextWindowModeJsonConverter : JsonConverter<ContextWindowMode>
{
    public override ContextWindowMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numeric))
        {
            return Enum.IsDefined(typeof(ContextWindowMode), numeric)
                ? (ContextWindowMode)numeric
                : throw new JsonException($"Unknown context-window mode numeric value: {numeric}.");
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (TryParse(value, out var mode))
                return mode;

            throw new JsonException($"Unknown context-window mode value: '{value}'.");
        }

        throw new JsonException("Context-window mode must be a string or number.");
    }

    public override void Write(Utf8JsonWriter writer, ContextWindowMode value, JsonSerializerOptions options)
        => writer.WriteStringValue(ToJsonValue(value));

    public static bool TryParse(string? value, out ContextWindowMode mode)
    {
        var normalized = Normalize(value);
        mode = normalized switch
        {
            "default" => ContextWindowMode.Default,
            "max" or "maximum" => ContextWindowMode.Max,
            _ => default
        };

        return normalized is "default" or "max" or "maximum";
    }

    public static string ToJsonValue(ContextWindowMode value) => value switch
    {
        ContextWindowMode.Default => "default",
        ContextWindowMode.Max => "max",
        _ => value.ToString()
    };

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Concat(value.Trim().Where(ch => ch is not '-' and not '_' and not ' '))
            .ToLowerInvariant();
    }
}
