using System.Text;
using System.Text.Json;
using DotCraft.Configuration;

namespace DotCraft.Dreams;

/// <summary>
/// Persists each run's state under <c>runs/&lt;id&gt;/state.json</c> and the latest run under <c>state.json</c>.
/// </summary>
public sealed class DreamsStateStore(string dreamsDirectory)
{
    private readonly string _statePath = Path.Combine(dreamsDirectory, "state.json");
    private readonly string _runsDir = Path.Combine(dreamsDirectory, "runs");
    private readonly object _syncRoot = new();

    public DreamsRunState? Load()
    {
        lock (_syncRoot)
            return Read(_statePath);
    }

    public DreamsRunState? Load(string runId)
    {
        lock (_syncRoot)
            return Read(GetRunStatePath(runId));
    }

    public IReadOnlyList<DreamsRunState> List(bool includeArchived = false)
    {
        lock (_syncRoot)
        {
            if (!Directory.Exists(_runsDir))
                return [];

            return Directory.EnumerateDirectories(_runsDir)
                .Select(directory => Read(Path.Combine(directory, "state.json")))
                .OfType<DreamsRunState>()
                .Where(state => includeArchived || state.ReviewStatus != DreamsReviewStatuses.Archived)
                .OrderByDescending(static state => state.StartedAt)
                .ToArray();
        }
    }

    /// <summary>Saves a run and makes it the latest run.</summary>
    public void Save(DreamsRunState state)
    {
        lock (_syncRoot)
        {
            WriteAtomic(_statePath, state);
            WriteAtomic(GetRunStatePath(state.Id), state);
        }
    }

    /// <summary>Saves a run, updating the latest run only when it is that run.</summary>
    public void SaveRun(DreamsRunState state)
    {
        lock (_syncRoot)
        {
            WriteAtomic(GetRunStatePath(state.Id), state);
            if (Read(_statePath)?.Id == state.Id)
                WriteAtomic(_statePath, state);
        }
    }

    public bool DeleteRun(string runId)
    {
        lock (_syncRoot)
        {
            var runDirectory = Path.GetDirectoryName(GetRunStatePath(runId))!;
            if (!Directory.Exists(runDirectory))
                return false;

            DreamsArtifacts.DeleteEntry(runDirectory);
            if (Read(_statePath)?.Id == runId)
                ReplaceLatest();
            return true;
        }
    }

    public void DeleteAllRuns()
    {
        lock (_syncRoot)
        {
            if (Directory.Exists(_runsDir))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(_runsDir).ToArray())
                    DreamsArtifacts.DeleteEntry(entry);
            }

            if (File.Exists(_statePath))
                File.Delete(_statePath);
        }
    }

    private void ReplaceLatest()
    {
        var replacement = List(includeArchived: true).FirstOrDefault();
        if (replacement != null)
            WriteAtomic(_statePath, replacement);
        else if (File.Exists(_statePath))
            File.Delete(_statePath);
    }

    private string GetRunStatePath(string runId) =>
        Path.Combine(_runsDir, DreamsArtifacts.NormalizeRunId(runId), "state.json");

    private static DreamsRunState? Read(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<DreamsRunState>(File.ReadAllText(path, Encoding.UTF8), AppConfig.SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void WriteAtomic(string path, DreamsRunState state)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var tempFile = Path.Combine(directory, $".state.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempFile, JsonSerializer.Serialize(state, AppConfig.SerializerOptions) + Environment.NewLine, Encoding.UTF8);
        try
        {
            File.Move(tempFile, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
