using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Configuration;

/// <summary>Provider-neutral inference speed requested for model calls.</summary>
[JsonConverter(typeof(InferenceSpeedJsonConverter))]
public enum InferenceSpeed
{
    Standard,
    Fast,
    Ultrafast
}

internal sealed class InferenceSpeedJsonConverter : JsonConverter<InferenceSpeed>
{
    public override InferenceSpeed Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException(InvalidMessage);

        return reader.GetString()?.Trim().ToLowerInvariant() switch
        {
            "standard" => InferenceSpeed.Standard,
            "fast" => InferenceSpeed.Fast,
            "ultrafast" => InferenceSpeed.Ultrafast,
            _ => throw new JsonException(InvalidMessage)
        };
    }

    public override void Write(Utf8JsonWriter writer, InferenceSpeed value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            InferenceSpeed.Fast => "fast",
            InferenceSpeed.Ultrafast => "ultrafast",
            _ => "standard"
        });

    private const string InvalidMessage = "Inference speed must be 'standard', 'fast', or 'ultrafast'.";
}
