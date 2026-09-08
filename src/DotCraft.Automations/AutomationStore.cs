using System.Text.Json;

namespace DotCraft.Automations;

/// <summary>Atomic JSON storage rooted in the automations namespace.</summary>
public sealed class AutomationStore(string root)
{
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
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            result.Add((await JsonSerializer.DeserializeAsync<AutomationRun>(stream, Json, ct))!);
        }
        return result.OrderByDescending(r => r.CreatedAt).ToArray();
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
