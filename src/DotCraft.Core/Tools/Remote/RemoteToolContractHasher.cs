using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace DotCraft.Tools;

/// <summary>Creates deterministic Remote Tool Host profile hashes for native tool definitions.</summary>
public static class RemoteToolContractHasher
{
    /// <summary>The first Remote Tool Host profile version.</summary>
    public const string ProfileVersion = "1";

    /// <summary>Computes the base64url SHA-256 hash of the stable remote contract fields.</summary>
    public static string Compute(ToolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("profileVersion", ProfileVersion);
            writer.WriteString("definitionId", definition.Id.ToString());
            writer.WriteString("name", definition.Name.ToString());
            writer.WriteString("description", definition.Description);
            writer.WritePropertyName("inputSchema");
            WriteCanonical(writer, definition.InputSchema);
            if (definition.OutputSchema is { } output)
            {
                writer.WritePropertyName("outputSchema");
                WriteCanonical(writer, output);
            }
            writer.WritePropertyName("annotations");
            writer.WriteStartObject();
            foreach (var (key, value) in definition.Annotations
                         .Where(static pair => pair.Key.StartsWith("dotcraft/rpc", StringComparison.Ordinal))
                         .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(key);
                WriteCanonical(writer, value);
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        var digest = SHA256.HashData(buffer.WrittenSpan);
        return Convert.ToBase64String(digest)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                value.WriteTo(writer);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
        }
    }
}
