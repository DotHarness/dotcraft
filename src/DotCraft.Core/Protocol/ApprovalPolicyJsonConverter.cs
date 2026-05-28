using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Protocol;

/// <summary>
/// Serializes <see cref="ApprovalPolicy"/> as wire strings: default, autoApprove, interrupt.
/// </summary>
public sealed class ApprovalPolicyJsonConverter : JsonConverter<ApprovalPolicy>
{
    public override ApprovalPolicy Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected string for ApprovalPolicy.");

        var s = reader.GetString();
        return s switch
        {
            "default" or "Default" => ApprovalPolicy.Default,
            "autoApprove" or "AutoApprove" => ApprovalPolicy.AutoApprove,
            "interrupt" or "Interrupt" => ApprovalPolicy.Interrupt,
            _ => throw new JsonException($"Unknown ApprovalPolicy: {s}")
        };
    }

    public override void Write(Utf8JsonWriter writer, ApprovalPolicy value, JsonSerializerOptions options)
    {
        var str = value switch
        {
            ApprovalPolicy.Default => "default",
            ApprovalPolicy.AutoApprove => "autoApprove",
            ApprovalPolicy.Interrupt => "interrupt",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };
        writer.WriteStringValue(str);
    }
}
