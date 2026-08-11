using System.Text.Json;
using System.Text.Json.Serialization;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

internal sealed class ModelReasoningEffortJsonConverter : JsonConverter<ModelReasoningEffort>
{
    public override ModelReasoningEffort Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (TryParse(value, out var effort))
                return effort;

            throw new JsonException($"Unknown reasoning effort value: '{value}'.");
        }

        throw new JsonException("Reasoning effort must be a string.");
    }

    public override void Write(Utf8JsonWriter writer, ModelReasoningEffort value, JsonSerializerOptions options)
        => writer.WriteStringValue(ToJsonValue(value));

    private static bool TryParse(string? value, out ModelReasoningEffort effort)
    {
        var normalized = Normalize(value);
        effort = normalized switch
        {
            "none" => ModelReasoningEffort.None,
            "low" => ModelReasoningEffort.Low,
            "medium" => ModelReasoningEffort.Medium,
            "high" => ModelReasoningEffort.High,
            "extrahigh" or "xhigh" => ModelReasoningEffort.ExtraHigh,
            "ultra" => ModelReasoningEffort.Ultra,
            _ => default
        };

        return normalized is "none" or "low" or "medium" or "high" or "extrahigh" or "xhigh" or "ultra";
    }

    private static string ToJsonValue(ModelReasoningEffort value) => value switch
    {
        ModelReasoningEffort.None => "none",
        ModelReasoningEffort.Low => "low",
        ModelReasoningEffort.Medium => "medium",
        ModelReasoningEffort.High => "high",
        ModelReasoningEffort.ExtraHigh => "extraHigh",
        ModelReasoningEffort.Ultra => "ultra",
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

internal sealed class ReasoningOutputJsonConverter : JsonConverter<ReasoningOutput>
{
    public override ReasoningOutput Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (TryParse(value, out var output))
                return output;

            throw new JsonException($"Unknown reasoning output value: '{value}'.");
        }

        throw new JsonException("Reasoning output must be a string.");
    }

    public override void Write(Utf8JsonWriter writer, ReasoningOutput value, JsonSerializerOptions options)
        => writer.WriteStringValue(ToJsonValue(value));

    private static bool TryParse(string? value, out ReasoningOutput output)
    {
        var normalized = Normalize(value);
        output = normalized switch
        {
            "none" or "omitted" => ReasoningOutput.None,
            "summary" or "summarized" => ReasoningOutput.Summary,
            "full" => ReasoningOutput.Full,
            _ => default
        };

        return normalized is "none" or "omitted" or "summary" or "summarized" or "full";
    }

    private static string ToJsonValue(ReasoningOutput value) => value switch
    {
        ReasoningOutput.None => "none",
        ReasoningOutput.Summary => "summary",
        ReasoningOutput.Full => "full",
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
