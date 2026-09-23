using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.SessionImport;

public sealed record SessionImportLedgerRecord
{
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("sourceId")]
    public required string SourceId { get; init; }

    [JsonPropertyName("sourcePath")]
    public string? SourcePath { get; init; }

    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    /// <summary>Missing after the ledger was rebuilt from thread metadata, until detection recomputes it.</summary>
    [JsonPropertyName("contentSha256")]
    public string? ContentSha256 { get; init; }

    [JsonPropertyName("sourceModifiedAt")]
    public DateTimeOffset? SourceModifiedAt { get; init; }

    [JsonPropertyName("importedAt")]
    public DateTimeOffset ImportedAt { get; init; }

    [JsonPropertyName("turnCount")]
    public int TurnCount { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }
}

public sealed class SessionImportLedgerDocument
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("lastSyncAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? LastSyncAt { get; set; }

    [JsonPropertyName("records")]
    public List<SessionImportLedgerRecord> Records { get; set; } = [];

    public SessionImportLedgerRecord? Find(string source, string sourceId) =>
        Records.LastOrDefault(record => IsSameSession(record, source, sourceId));

    public void Upsert(SessionImportLedgerRecord record)
    {
        Records.RemoveAll(existing => IsSameSession(existing, record.Source, record.SourceId));
        Records.Add(record);
    }

    private static bool IsSameSession(SessionImportLedgerRecord record, string source, string sourceId) =>
        string.Equals(record.Source, source, StringComparison.Ordinal)
        && string.Equals(record.SourceId, sourceId, StringComparison.OrdinalIgnoreCase);
}

public sealed class SessionImportLedger(string craftDataPath)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new UtcTimestampConverter() }
    };

    private readonly string _filePath = Path.Combine(craftDataPath, "imports", "sessions.json");

    /// <summary>Compares modification times at the millisecond precision the ledger stores.</summary>
    public static bool SameInstant(DateTimeOffset left, DateTimeOffset right) =>
        left.UtcTicks / TimeSpan.TicksPerMillisecond == right.UtcTicks / TimeSpan.TicksPerMillisecond;

    public SessionImportLedgerDocument? TryRead()
    {
        try
        {
            if (!File.Exists(_filePath))
                return null;
            var document = JsonSerializer.Deserialize<SessionImportLedgerDocument>(File.ReadAllBytes(_filePath), Options);
            if (document is not { Version: SessionImportLedgerDocument.CurrentVersion })
                return null;
            document.Records ??= [];
            return document.Records.Any(static record => record is null) ? null : document;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException
                                       or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    public void Write(SessionImportLedgerDocument document)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(tempPath, JsonSerializer.SerializeToUtf8Bytes(document, Options));
            File.Move(tempPath, _filePath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class UtcTimestampConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(SessionImportIdentity.FormatTimestamp(value));
    }
}
