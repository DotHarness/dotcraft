using System.Buffers;
using System.Text;
using System.Text.Json;

namespace DotCraft.Agents;

/// <summary>
/// Rewrites OpenAI Responses request bodies into a single canonical top-level object.
/// </summary>
internal static class OpenAIResponsesRequestBodyCanonicalizer
{
    private const string ClientMetadataField = "client_metadata";

    internal static string? Canonicalize(string json)
    {
        if (!TryParseTopLevelObject(json, out var body))
            return null;

        return body.HadDuplicateTopLevelKeys ? body.ToJsonString() : null;
    }

    internal static string? AddInstallationIdMetadata(
        string json,
        string installationIdHeader,
        string installationId)
    {
        if (string.IsNullOrWhiteSpace(installationIdHeader) ||
            string.IsNullOrWhiteSpace(installationId) ||
            !TryParseTopLevelObject(json, out var body))
        {
            return null;
        }

        var changed = body.HadDuplicateTopLevelKeys;
        if (body.TryGetRawValue(ClientMetadataField, out var rawMetadata) &&
            TryParseTopLevelObject(rawMetadata, out var metadata))
        {
            if (metadata.TryGetRawValue(installationIdHeader, out _))
                return changed ? body.ToJsonString() : null;

            metadata.SetRawValue(installationIdHeader, JsonSerializer.Serialize(installationId));
            body.SetRawValue(ClientMetadataField, metadata.ToJsonString());
            return body.ToJsonString();
        }

        body.SetRawValue(
            ClientMetadataField,
            BuildSingleStringPropertyObject(installationIdHeader, installationId));
        return body.ToJsonString();
    }

    private static bool TryParseTopLevelObject(
        string json,
        out CanonicalTopLevelJsonObject body)
    {
        body = null!;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            var entries = new List<JsonPropertyEntry>();
            var hadDuplicate = false;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var previous = entries.FindIndex(entry => string.Equals(entry.Name, property.Name, StringComparison.Ordinal));
                if (previous >= 0)
                {
                    entries.RemoveAt(previous);
                    hadDuplicate = true;
                }

                entries.Add(new JsonPropertyEntry(property.Name, property.Value.GetRawText()));
            }

            body = new CanonicalTopLevelJsonObject(entries, hadDuplicate);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string BuildSingleStringPropertyObject(string propertyName, string value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString(propertyName, value);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private sealed class CanonicalTopLevelJsonObject(
        List<JsonPropertyEntry> entries,
        bool hadDuplicateTopLevelKeys)
    {
        public bool HadDuplicateTopLevelKeys { get; } = hadDuplicateTopLevelKeys;

        public bool TryGetRawValue(string name, out string rawValue)
        {
            var index = FindIndex(name);
            if (index < 0)
            {
                rawValue = string.Empty;
                return false;
            }

            rawValue = entries[index].RawValue;
            return true;
        }

        public void SetRawValue(string name, string rawValue)
        {
            var index = FindIndex(name);
            var entry = new JsonPropertyEntry(name, rawValue);
            if (index >= 0)
                entries[index] = entry;
            else
                entries.Add(entry);
        }

        public string ToJsonString()
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                foreach (var entry in entries)
                {
                    writer.WritePropertyName(entry.Name);
                    writer.WriteRawValue(entry.RawValue);
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        private int FindIndex(string name) =>
            entries.FindIndex(entry => string.Equals(entry.Name, name, StringComparison.Ordinal));
    }

    private sealed record JsonPropertyEntry(string Name, string RawValue);
}
