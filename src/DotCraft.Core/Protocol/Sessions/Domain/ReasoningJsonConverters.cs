using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol;

internal sealed class ReasoningEffortJsonConverter : JsonConverter<ReasoningEffort>
{
    public override ReasoningEffort Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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

    public override void Write(Utf8JsonWriter writer, ReasoningEffort value, JsonSerializerOptions options)
        => writer.WriteStringValue(ToJsonValue(value));

    private static bool TryParse(string? value, out ReasoningEffort effort)
    {
        var normalized = Normalize(value);
        effort = normalized switch
        {
            "none" => ReasoningEffort.None,
            "low" => ReasoningEffort.Low,
            "medium" => ReasoningEffort.Medium,
            "high" => ReasoningEffort.High,
            "extrahigh" or "xhigh" => ReasoningEffort.ExtraHigh,
            _ => default
        };

        return normalized is "none" or "low" or "medium" or "high" or "extrahigh" or "xhigh";
    }

    private static string ToJsonValue(ReasoningEffort value) => value switch
    {
        ReasoningEffort.None => "none",
        ReasoningEffort.Low => "low",
        ReasoningEffort.Medium => "medium",
        ReasoningEffort.High => "high",
        ReasoningEffort.ExtraHigh => "extraHigh",
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
