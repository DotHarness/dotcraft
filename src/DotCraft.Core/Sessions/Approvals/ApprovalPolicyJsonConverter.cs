using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Sessions;

/// <summary>
/// Serializes <see cref="ApprovalPolicy"/> as wire strings: default, prompt, autoApprove, deny.
/// <c>interrupt</c> is the former name of <c>deny</c> and is still read from persisted
/// sessions and configuration files.
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
            "prompt" or "Prompt" => ApprovalPolicy.Prompt,
            "autoApprove" or "AutoApprove" => ApprovalPolicy.AutoApprove,
            "deny" or "Deny" or "interrupt" or "Interrupt" => ApprovalPolicy.Deny,
            _ => throw new JsonException($"Unknown ApprovalPolicy: {s}")
        };
    }

    public override void Write(Utf8JsonWriter writer, ApprovalPolicy value, JsonSerializerOptions options)
    {
        var str = value switch
        {
            ApprovalPolicy.Default => "default",
            ApprovalPolicy.Prompt => "prompt",
            ApprovalPolicy.AutoApprove => "autoApprove",
            ApprovalPolicy.Deny => "deny",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };
        writer.WriteStringValue(str);
    }
}
