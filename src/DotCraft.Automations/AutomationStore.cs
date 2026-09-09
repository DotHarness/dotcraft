using System.Text.Json;

namespace DotCraft.Automations;

/// <summary>Atomic JSON storage rooted in the automations namespace.</summary>
public sealed class AutomationStore(string root)
{
    private readonly SemaphoreSlim _readingGate = new(1, 1);
    private sealed record ReadReceipt(DateTimeOffset? ReadAt, DateTimeOffset? CompletedAt);
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string DirectoryFor(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("automation.invalidId");
        return Path.Combine(root, id);
    }
    public async Task<IReadOnlyList<AutomationDefinition>> ListAsync(CancellationToken ct)
    {
        if (!Directory.Exists(root)) return [];
        var result = new List<AutomationDefinition>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var file = Path.Combine(directory, "automation.json");
            if (!File.Exists(file)) continue;
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            result.Add((await JsonSerializer.DeserializeAsync<AutomationDefinition>(stream, Json, ct))!);
        }
        return result;
    }
    public async Task<IReadOnlyList<AutomationRun>> RunsAsync(string id, CancellationToken ct)
    {
        var directory = Path.Combine(DirectoryFor(id), "runs");
        if (!Directory.Exists(directory)) return [];
        var result = new List<AutomationRun>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            var run = await ReadRunAsync(file, ct);
            if (run != null) result.Add(run);
        }
        var receipts = await ReadReceiptsAsync(id, ct);
        return result.Select(run => run with { ReadAt = receipts.TryGetValue(run.Id, out var receipt)
            && receipt.CompletedAt == run.CompletedAt ? receipt.ReadAt : null }).OrderByDescending(r => r.CreatedAt).ToArray();
    }
    private async Task<Dictionary<string, ReadReceipt>> ReadReceiptsAsync(string id, CancellationToken ct)
    {
        var path = Path.Combine(DirectoryFor(id), "reading.json");
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return await JsonSerializer.DeserializeAsync<Dictionary<string, ReadReceipt>>(stream, Json, ct) ?? [];
        }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
    }
    /// <summary>Stores reading receipts without changing the scheduler-owned run files.</summary>
    public async Task<IReadOnlyList<AutomationRun>> SetRunsReadAsync(string id, IReadOnlyList<string> runIds, bool read, CancellationToken ct)
    {
        if (runIds == null || runIds.Count is < 1 or > 200 || runIds.Any(string.IsNullOrWhiteSpace)
            || runIds.Distinct().Count() != runIds.Count) throw new ArgumentException("automation.invalidRunIds");
        await _readingGate.WaitAsync(ct);
        try
        {
            var runs = (await RunsAsync(id, ct)).ToDictionary(run => run.Id);
            if (runIds.Any(runId => !runs.ContainsKey(runId))) throw new ArgumentException("automation.runNotFound");
            var receipts = await ReadReceiptsAsync(id, ct);
            var now = DateTimeOffset.UtcNow;
            foreach (var runId in runIds) receipts[runId] = new(read ? now : null, runs[runId].CompletedAt);
            await WriteAsync(Path.Combine(DirectoryFor(id), "reading.json"), receipts, ct);
            return (await RunsAsync(id, ct)).Where(run => runIds.Contains(run.Id)).ToArray();
        }
        finally { _readingGate.Release(); }
    }
    private static async Task<AutomationRun?> ReadRunAsync(string file, CancellationToken ct)
    {
        const int attempts = 10;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return await JsonSerializer.DeserializeAsync<AutomationRun>(stream, Json, ct);
            }
            catch (IOException) when (attempt < attempts - 1)
            {
                await Task.Delay(5, ct);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }
        return null;
    }
    public Task SaveAsync(AutomationDefinition definition, CancellationToken ct) =>
        WriteAsync(Path.Combine(DirectoryFor(definition.Id), "automation.json"), definition, ct);
    public Task SaveRunAsync(AutomationRun run, CancellationToken ct) =>
        WriteAsync(Path.Combine(DirectoryFor(run.AutomationId), "runs", run.Id + ".json"), run, ct);
    public void Delete(string id) => File.Delete(Path.Combine(DirectoryFor(id), "automation.json"));
    private static async Task WriteAsync<T>(string path, T value, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, Json), ct);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
