using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Configuration;

/// <summary>Provider-neutral inference speed requested for model calls.</summary>
[JsonConverter(typeof(InferenceSpeedJsonConverter))]
public enum InferenceSpeed
{
    Standard,
    Fast
}

internal sealed class InferenceSpeedJsonConverter : JsonConverter<InferenceSpeed>
{
    public override InferenceSpeed Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Inference speed must be 'standard' or 'fast'.");

        return reader.GetString()?.Trim().ToLowerInvariant() switch
        {
            "standard" => InferenceSpeed.Standard,
            "fast" => InferenceSpeed.Fast,
            _ => throw new JsonException("Inference speed must be 'standard' or 'fast'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, InferenceSpeed value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value == InferenceSpeed.Fast ? "fast" : "standard");
}
