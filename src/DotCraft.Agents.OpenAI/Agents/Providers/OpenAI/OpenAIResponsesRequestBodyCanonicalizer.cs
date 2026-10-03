using System.Buffers;
using System.Text;
using System.Text.Json;
using DotCraft.Auth.OpenAI;

namespace DotCraft.Agents;

/// <summary>Rewrites Responses requests while retaining untouched values as raw UTF-8 slices.</summary>
internal static class OpenAIResponsesRequestBodyCanonicalizer
{
    private const string ClientMetadataField = "client_metadata";

    internal readonly record struct OAuthRewriteResult(
        ReadOnlyMemory<byte>? Body,
        bool InstallationIdMismatch);

    internal static ReadOnlyMemory<byte>? Canonicalize(ReadOnlyMemory<byte> json)
    {
        if (!TryParseTopLevelObject(json, out var body) || !body.HadDuplicateTopLevelKeys)
            return null;

        return body.ToUtf8Json();
    }

    internal static ReadOnlyMemory<byte>? NormalizeTopLevelObject(ReadOnlyMemory<byte> json)
    {
        if (!TryParseTopLevelObject(json, out var body))
            return null;

        return body.ToUtf8Json();
    }

    internal static string? NormalizeTopLevelObject(string json)
    {
        var normalized = NormalizeTopLevelObject(Encoding.UTF8.GetBytes(json));
        return normalized is { } bytes ? Encoding.UTF8.GetString(bytes.Span) : null;
    }

    internal static OAuthRewriteResult RewriteOAuthRequest(
        ReadOnlyMemory<byte> json,
        IReadOnlyDictionary<string, string> metadataValues)
    {
        if (!TryParseTopLevelObject(json, out var body))
            return default;

        var changed = body.HadDuplicateTopLevelKeys;
        changed |= body.RemoveRawValue("max_output_tokens");
        CanonicalTopLevelJsonObject metadata;
        bool metadataChanged;
        if (body.TryGetRawValue(ClientMetadataField, out var rawMetadata)
            && TryParseTopLevelObject(rawMetadata, out var parsedMetadata))
        {
            metadata = parsedMetadata;
            metadataChanged = metadata.HadDuplicateTopLevelKeys;
        }
        else
        {
            metadata = new CanonicalTopLevelJsonObject(2);
            metadataChanged = true;
        }

        var installationIdMismatch = false;
        foreach (var pair in metadataValues)
        {
            if (metadata.TryGetRawValue(pair.Key, out var existingRaw)
                && TryReadString(existingRaw, out var existing))
            {
                if (string.Equals(existing, pair.Value, StringComparison.Ordinal))
                    continue;

                if (pair.Key == OpenAIAuthConstants.InstallationIdHeader)
                    installationIdMismatch = true;
            }

            metadata.SetRawValue(pair.Key, JsonSerializer.SerializeToUtf8Bytes(pair.Value));
            metadataChanged = true;
        }

        if (metadataChanged)
        {
            body.SetRawValue(ClientMetadataField, metadata.ToUtf8Json());
            changed = true;
        }

        if (!changed)
            return default;

        return new OAuthRewriteResult(body.ToUtf8Json(), installationIdMismatch);
    }

    private static bool TryParseTopLevelObject(
        ReadOnlyMemory<byte> json,
        out CanonicalTopLevelJsonObject body)
    {
        body = null!;
        try
        {
            var reader = new Utf8JsonReader(json.Span);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return false;

            var parsed = new CanonicalTopLevelJsonObject(json.Length);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    if (reader.Read())
                        return false;
                    body = parsed;
                    return true;
                }

                var name = reader.GetString()!;
                if (!reader.Read())
                    return false;
                var start = (int)reader.TokenStartIndex;
                reader.Skip();
                parsed.AddParsedProperty(name, json.Slice(start, (int)reader.BytesConsumed - start));
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadString(ReadOnlyMemory<byte> rawJson, out string? value)
    {
        var reader = new Utf8JsonReader(rawJson.Span);
        reader.Read();
        value = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        return value != null;
    }

    private sealed class CanonicalTopLevelJsonObject(int sourceLength)
    {
        private readonly List<JsonPropertyEntry> _entries = [];
        private int _capacity = sourceLength;

        public bool HadDuplicateTopLevelKeys { get; private set; }

        public void AddParsedProperty(string name, ReadOnlyMemory<byte> rawValue)
        {
            var index = FindIndex(name);
            if (index >= 0)
            {
                _entries.RemoveAt(index);
                HadDuplicateTopLevelKeys = true;
            }
            _entries.Add(new JsonPropertyEntry(name, rawValue));
        }

        public bool TryGetRawValue(string name, out ReadOnlyMemory<byte> rawValue)
        {
            var index = FindIndex(name);
            rawValue = index >= 0 ? _entries[index].RawValue : default;
            return index >= 0;
        }

        public void SetRawValue(string name, ReadOnlyMemory<byte> rawValue)
        {
            var index = FindIndex(name);
            var entry = new JsonPropertyEntry(name, rawValue);
            if (index >= 0)
                _entries[index] = entry;
            else
                _entries.Add(entry);
            _capacity += rawValue.Length + Encoding.UTF8.GetByteCount(name) + 4;
        }

        public bool RemoveRawValue(string name)
        {
            var index = FindIndex(name);
            if (index < 0)
                return false;

            _entries.RemoveAt(index);
            return true;
        }

        public ReadOnlyMemory<byte> ToUtf8Json()
        {
            var buffer = new ArrayBufferWriter<byte>(_capacity);
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                foreach (var entry in _entries)
                {
                    writer.WritePropertyName(entry.Name);
                    writer.WriteRawValue(entry.RawValue.Span, skipInputValidation: true);
                }
                writer.WriteEndObject();
            }
            return buffer.WrittenMemory;
        }

        private int FindIndex(string name)
        {
            for (var i = 0; i < _entries.Count; i++)
                if (string.Equals(_entries[i].Name, name, StringComparison.Ordinal))
                    return i;
            return -1;
        }
    }

    private readonly record struct JsonPropertyEntry(string Name, ReadOnlyMemory<byte> RawValue);
}
