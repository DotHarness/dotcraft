using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.DynamicWorkflows;

public sealed class DynamicWorkflowStore
{
    private readonly string _runsRoot;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public DynamicWorkflowStore(string craftPath) =>
        _runsRoot = Path.Combine(Path.GetFullPath(craftPath), "workflows", "runs");

    public string GetRunDirectory(string runId) => Path.Combine(_runsRoot, ValidateRunId(runId));

    public async Task CreateAsync(DynamicWorkflowRun run, string script, CancellationToken cancellationToken)
    {
        var directory = GetRunDirectory(run.RunId);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "script.js"), script, new UTF8Encoding(false), cancellationToken);
        await WriteStateAsync(run, cancellationToken);
        await AppendJournalAsync(run.RunId, "run.created", new JsonObject
        {
            ["name"] = run.Name,
            ["scriptHash"] = run.ScriptHash
        }, cancellationToken);
    }

    public async Task WriteStateAsync(DynamicWorkflowRun run, CancellationToken cancellationToken)
    {
        var directory = GetRunDirectory(run.RunId);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "state.json");
        var temporary = Path.Combine(directory, $".state.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(run, _jsonOptions), new UTF8Encoding(false), cancellationToken);
        File.Move(temporary, target, overwrite: true);
    }

    public async Task<DynamicWorkflowRun?> ReadStateAsync(string runId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(GetRunDirectory(runId), "state.json");
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<DynamicWorkflowRun>(stream, _jsonOptions, cancellationToken);
    }

    public async Task AppendJournalAsync(
        string runId,
        string type,
        JsonNode? payload,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(GetRunDirectory(runId), "journal.jsonl");
        var entry = new JsonObject
        {
            ["at"] = DateTimeOffset.UtcNow,
            ["type"] = type,
            ["payload"] = payload?.DeepClone()
        };
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        await writer.WriteLineAsync(entry.ToJsonString().AsMemory(), cancellationToken);
    }

    public async Task<IReadOnlyList<JsonObject>> ReadJournalAsync(string runId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(GetRunDirectory(runId), "journal.jsonl");
        if (!File.Exists(path)) return [];
        var entries = new List<JsonObject>();
        foreach (var line in await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (JsonNode.Parse(line) is JsonObject entry) entries.Add(entry);
        }
        return entries;
    }

    public IEnumerable<string> EnumerateRunIds()
    {
        if (!Directory.Exists(_runsRoot)) yield break;
        foreach (var directory in Directory.EnumerateDirectories(_runsRoot, "run_*", SearchOption.TopDirectoryOnly))
            yield return Path.GetFileName(directory);
    }

    public Task DeleteAsync(string runId)
    {
        var directory = GetRunDirectory(runId);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        return Task.CompletedTask;
    }

    private static string ValidateRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId) || !runId.StartsWith("run_", StringComparison.Ordinal)
            || runId.Any(character => !(char.IsLetterOrDigit(character) || character == '_')))
            throw new ArgumentException("Invalid workflow run id.", nameof(runId));
        return runId;
    }
}
