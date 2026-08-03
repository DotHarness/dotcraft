using System.Buffers;
using System.Text;
using System.Text.Json;
using DotCraft.Sessions;

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

    internal static string? NormalizeTopLevelObject(string json)
    {
        if (!TryParseTopLevelObject(json, out var body))
            return null;

        return body.ToJsonString();
    }

    internal static string? RemoveTopLevelFields(string json, params string[] fieldNames)
    {
        if (fieldNames.Length == 0 || !TryParseTopLevelObject(json, out var body))
            return null;

        var normalizedFieldNames = fieldNames
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .ToHashSet(StringComparer.Ordinal);
        if (normalizedFieldNames.Count == 0)
            return null;

        var changed = body.HadDuplicateTopLevelKeys;
        foreach (var fieldName in normalizedFieldNames)
            changed |= body.RemoveRawValue(fieldName);

        return changed ? body.ToJsonString() : null;
    }

    internal static string? AddInstallationIdMetadata(
        string json,
        string installationIdHeader,
        string installationId)
    {
        if (string.IsNullOrWhiteSpace(installationIdHeader) ||
            string.IsNullOrWhiteSpace(installationId))
        {
            return null;
        }

        return AddClientMetadata(
            json,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [installationIdHeader] = installationId
            });
    }

    internal static string? AddClientMetadata(
        string json,
        IReadOnlyDictionary<string, string> metadataValues)
    {
        if (metadataValues.Count == 0 || !TryParseTopLevelObject(json, out var body))
            return null;

        var normalizedValues = metadataValues
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(
                static pair => pair.Key.Trim(),
                static pair => pair.Value.Trim(),
                StringComparer.Ordinal);
        if (normalizedValues.Count == 0)
            return null;

        var changed = body.HadDuplicateTopLevelKeys;
        if (body.TryGetRawValue(ClientMetadataField, out var rawMetadata) &&
            TryParseTopLevelObject(rawMetadata, out var metadata))
        {
            changed |= metadata.HadDuplicateTopLevelKeys;
            foreach (var pair in normalizedValues)
            {
                if (metadata.TryGetRawValue(pair.Key, out var existingRaw) &&
                    TryReadString(existingRaw, out var existing) &&
                    string.Equals(existing, pair.Value, StringComparison.Ordinal))
                {
                    continue;
                }

                metadata.SetRawValue(pair.Key, JsonSerializer.Serialize(pair.Value));
                changed = true;
            }

            if (!changed)
                return null;

            body.SetRawValue(ClientMetadataField, metadata.ToJsonString());
            return body.ToJsonString();
        }

        body.SetRawValue(ClientMetadataField, BuildStringPropertyObject(normalizedValues));
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

    private static string BuildStringPropertyObject(IReadOnlyDictionary<string, string> values)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var pair in values)
                writer.WriteString(pair.Key, pair.Value);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static bool TryReadString(string rawJson, out string? value)
    {
        value = null;
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (document.RootElement.ValueKind != JsonValueKind.String)
                return false;

            value = document.RootElement.GetString();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
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

        public bool RemoveRawValue(string name)
        {
            var index = FindIndex(name);
            if (index < 0)
                return false;

            entries.RemoveAt(index);
            return true;
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
