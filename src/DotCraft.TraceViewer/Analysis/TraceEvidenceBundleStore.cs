using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DotCraft.Tracing;

namespace DotCraft.TraceViewer.Analysis;

internal sealed class TraceEvidenceBundleStore(string analysisRoot)
{
    private const int ChunkCharacters = 1800;
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };
    private static readonly JsonSerializerOptions JsonLineOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly string[] LargeFieldNames =
        ["content", "toolArguments", "toolResult", "metadataJson", "finalSystemPrompt"];

    private readonly string _evidenceRoot = Path.Combine(Path.GetFullPath(analysisRoot), "evidence");

    public string Ensure(TraceSnapshot snapshot, CancellationToken cancellationToken = default) =>
        Prepare(snapshot, cancellationToken).Path;

    public TraceEvidenceBundle Prepare(TraceSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bundlePath = GetBundlePath(snapshot);
        if (IsComplete(bundlePath, snapshot))
            return new TraceEvidenceBundle(bundlePath, Created: false);

        var parent = Path.GetDirectoryName(bundlePath)!;
        Directory.CreateDirectory(parent);
        if (Directory.Exists(bundlePath))
            DeleteDirectoryWithinRoot(bundlePath);

        var temporaryPath = Path.Combine(parent, $".{Path.GetFileName(bundlePath)}.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(temporaryPath);
        try
        {
            WriteBundle(temporaryPath, snapshot, cancellationToken);
            try
            {
                Directory.Move(temporaryPath, bundlePath);
            }
            catch (IOException) when (IsComplete(bundlePath, snapshot))
            {
                DeleteDirectoryWithinRoot(temporaryPath);
                return new TraceEvidenceBundle(bundlePath, Created: false);
            }
            return new TraceEvidenceBundle(bundlePath, Created: true);
        }
        catch
        {
            if (Directory.Exists(temporaryPath))
                DeleteDirectoryWithinRoot(temporaryPath);
            throw;
        }
    }

    public void Delete(TraceSnapshot snapshot)
    {
        var path = GetBundlePath(snapshot);
        if (Directory.Exists(path))
            DeleteDirectoryWithinRoot(path);
    }

    public void KeepOnly(TraceSnapshot snapshot)
    {
        var current = GetBundlePath(snapshot);
        var revisionRoot = Path.GetDirectoryName(current)!;
        if (!Directory.Exists(revisionRoot))
            return;

        foreach (var directory in Directory.EnumerateDirectories(revisionRoot))
        {
            if (!string.Equals(directory, current, StringComparison.OrdinalIgnoreCase))
                DeleteDirectoryWithinRoot(directory);
        }
    }

    private string GetBundlePath(TraceSnapshot snapshot) => Path.Combine(
        _evidenceRoot,
        Hash(Path.TrimEndingDirectorySeparator(Path.GetFullPath(snapshot.WorkspacePath)).ToUpperInvariant()),
        Hash(snapshot.SessionKey),
        Hash(snapshot.Revision));

    private static bool IsComplete(string path, TraceSnapshot snapshot)
    {
        var manifestPath = Path.Combine(path, "manifest.json");
        if (!File.Exists(manifestPath) || !File.Exists(Path.Combine(path, "events", "index.jsonl")))
            return false;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath, Encoding.UTF8));
            var root = document.RootElement;
            return root.GetProperty("schemaVersion").GetInt32() == 1
                   && root.GetProperty("sessionKey").GetString() == snapshot.SessionKey
                   && root.GetProperty("revision").GetString() == snapshot.Revision
                   && root.GetProperty("eventCount").GetInt32() == snapshot.Events.Count;
        }
        catch (Exception exception) when (exception is IOException or JsonException or KeyNotFoundException)
        {
            return false;
        }
    }

    private static void WriteBundle(string root, TraceSnapshot snapshot, CancellationToken cancellationToken)
    {
        var eventsRoot = Path.Combine(root, "events");
        Directory.CreateDirectory(eventsRoot);
        var indexPath = Path.Combine(eventsRoot, "index.jsonl");
        using var index = new StreamWriter(indexPath, append: false, Utf8NoBom);

        for (var eventIndex = 0; eventIndex < snapshot.Events.Count; eventIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var traceEvent = snapshot.Events[eventIndex];
            var ordinal = eventIndex + 1;
            var relativeDirectory = ordinal.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
            var detailDirectory = Path.Combine(eventsRoot, relativeDirectory);
            Directory.CreateDirectory(detailDirectory);

            var fieldFiles = WriteLargeFields(detailDirectory, traceEvent, cancellationToken);
            var eventNode = JsonSerializer.SerializeToNode(traceEvent, JsonOptions)!.AsObject();
            foreach (var fieldName in LargeFieldNames)
                eventNode.Remove(fieldName);
            eventNode["fieldFiles"] = JsonSerializer.SerializeToNode(fieldFiles, JsonOptions);
            File.WriteAllText(
                Path.Combine(detailDirectory, "event.json"),
                eventNode.ToJsonString(JsonOptions),
                Utf8NoBom);

            var indexEntry = new
            {
                Ordinal = ordinal,
                traceEvent.Id,
                Type = traceEvent.Type.ToString(),
                traceEvent.Timestamp,
                traceEvent.DurationMs,
                traceEvent.ToolName,
                traceEvent.CallId,
                traceEvent.ResponseId,
                traceEvent.MessageId,
                traceEvent.ModelId,
                traceEvent.RequestIndex,
                traceEvent.LlmCallIndex,
                DetailPath = $"events/{relativeDirectory}/event.json"
            };
            index.WriteLine(JsonSerializer.Serialize(indexEntry, JsonLineOptions));
        }

        var manifest = new
        {
            SchemaVersion = 1,
            snapshot.SessionKey,
            snapshot.Revision,
            GeneratedAt = DateTimeOffset.UtcNow,
            snapshot.LastActivityAt,
            EventCount = snapshot.Events.Count,
            EventIndex = "events/index.jsonl"
        };
        File.WriteAllText(
            Path.Combine(root, "manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions),
            Utf8NoBom);
    }

    private static Dictionary<string, TraceEvidenceFieldFiles> WriteLargeFields(
        string directory,
        TraceEvent traceEvent,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, TraceEvidenceFieldFiles>(StringComparer.Ordinal);
        WriteField(fields, directory, "content", "content", ".txt", traceEvent.Content, cancellationToken);
        WriteField(fields, directory, "toolArguments", "tool-arguments", ".txt", traceEvent.ToolArguments, cancellationToken);
        WriteField(fields, directory, "toolResult", "tool-result", ".txt", traceEvent.ToolResult, cancellationToken);
        WriteField(fields, directory, "metadataJson", "metadata", ".json", traceEvent.MetadataJson, cancellationToken);
        WriteField(fields, directory, "finalSystemPrompt", "final-system-prompt", ".txt", traceEvent.FinalSystemPrompt, cancellationToken);
        return fields;
    }

    private static void WriteField(
        IDictionary<string, TraceEvidenceFieldFiles> fields,
        string directory,
        string fieldName,
        string fileStem,
        string extension,
        string? value,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(value))
            return;

        var files = new List<string>();
        var offset = 0;
        var part = 1;
        while (offset < value.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = Math.Min(ChunkCharacters, value.Length - offset);
            if (offset + length < value.Length && char.IsHighSurrogate(value[offset + length - 1]))
                length--;
            var fileName = $"{fileStem}-{part:D4}{extension}";
            File.WriteAllText(Path.Combine(directory, fileName), value.AsSpan(offset, length), Utf8NoBom);
            files.Add(fileName);
            offset += length;
            part++;
        }
        fields[fieldName] = new TraceEvidenceFieldFiles(value.Length, files);
    }

    private void DeleteDirectoryWithinRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_evidenceRoot));
        if (!fullPath.StartsWith(boundary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Evidence cleanup target is outside the Trace Viewer evidence root.");
        Directory.Delete(fullPath, recursive: true);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];

    private sealed record TraceEvidenceFieldFiles(int CharacterCount, IReadOnlyList<string> Files);
}

internal sealed record TraceEvidenceBundle(string Path, bool Created);
