using System.Text;
using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Sessions;
using DreamsRunState = DotCraft.Dreams.DreamsRunState;

namespace DotCraft.Dreams;

/// <summary>
/// Persists the latest Dreams run state under .craft/dreams/state.json.
/// </summary>
public sealed class DreamsStateStore
{
    private readonly string _statePath;
    private readonly string _runsDir;
    private readonly object _syncRoot = new();

    public DreamsStateStore(DreamStore dreamStore)
    {
        _statePath = Path.Combine(dreamStore.DreamsDirectoryPath, "state.json");
        _runsDir = dreamStore.RunsDirectoryPath;
    }

    public string StatePath => _statePath;

    public DreamsRunState? Load()
    {
        lock (_syncRoot)
        {
            if (!File.Exists(_statePath))
                return null;

            try
            {
                return JsonSerializer.Deserialize<DreamsRunState>(
                    File.ReadAllText(_statePath, Encoding.UTF8),
                    AppConfig.SerializerOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    public DreamsRunState? Load(string runId)
    {
        lock (_syncRoot)
        {
            var path = GetRunStatePath(runId);
            if (!File.Exists(path))
                return null;

            try
            {
                return JsonSerializer.Deserialize<DreamsRunState>(
                    File.ReadAllText(path, Encoding.UTF8),
                    AppConfig.SerializerOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    public IReadOnlyList<DreamsRunState> List(bool includeArchived = false)
    {
        lock (_syncRoot)
        {
            if (!Directory.Exists(_runsDir))
                return [];

            var runs = new List<DreamsRunState>();
            foreach (var statePath in Directory.EnumerateFiles(_runsDir, "state.json", SearchOption.AllDirectories))
            {
                try
                {
                    var state = JsonSerializer.Deserialize<DreamsRunState>(
                        File.ReadAllText(statePath, Encoding.UTF8),
                        AppConfig.SerializerOptions);
                    if (state == null)
                        continue;
                    if (!includeArchived && state.ReviewStatus == DreamsReviewStatuses.Archived)
                        continue;
                    runs.Add(state);
                }
                catch (JsonException)
                {
                    // Ignore corrupt per-run state and keep the management surface usable.
                }
            }

            return runs
                .OrderByDescending(static state => state.StartedAt)
                .ToArray();
        }
    }

    public void Save(DreamsRunState state)
    {
        lock (_syncRoot)
        {
            SaveLatestStateCore(state);
            SaveRunStateCore(state);
        }
    }

    public void SaveRun(DreamsRunState state)
    {
        lock (_syncRoot)
        {
            SaveRunStateCore(state);
            var latest = Load();
            if (latest?.Id == state.Id)
                SaveLatestStateCore(state);
        }
    }

    public bool DeleteRun(string runId)
    {
        lock (_syncRoot)
        {
            var runDirectory = Path.GetDirectoryName(GetRunStatePath(runId))!;
            if (!Directory.Exists(runDirectory))
                return false;

            var latest = Load();
            var attributes = File.GetAttributes(runDirectory);
            Directory.Delete(runDirectory, recursive: (attributes & FileAttributes.ReparsePoint) == 0);

            if (latest?.Id == runId)
            {
                var replacement = List(includeArchived: true).FirstOrDefault();
                if (replacement == null)
                {
                    if (File.Exists(_statePath))
                        File.Delete(_statePath);
                }
                else
                {
                    SaveLatestStateCore(replacement);
                }
            }

            return true;
        }
    }

    public int DeleteAllRuns()
    {
        lock (_syncRoot)
        {
            var count = List(includeArchived: true).Count;
            if (Directory.Exists(_runsDir))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(_runsDir).ToArray())
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.Directory) != 0)
                        Directory.Delete(entry, recursive: (attributes & FileAttributes.ReparsePoint) == 0);
                    else
                        File.Delete(entry);
                }
            }

            if (File.Exists(_statePath))
                File.Delete(_statePath);
            Directory.CreateDirectory(_runsDir);
            return count;
        }
    }

    private void SaveLatestStateCore(DreamsRunState state)
    {
        var dir = Path.GetDirectoryName(_statePath)!;
        Directory.CreateDirectory(dir);
        var tempFile = Path.Combine(dir, $".state.{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(state, AppConfig.SerializerOptions) + Environment.NewLine;
        File.WriteAllText(tempFile, json, Encoding.UTF8);
        try
        {
            if (File.Exists(_statePath))
                File.Replace(tempFile, _statePath, null);
            else
                File.Move(tempFile, _statePath);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    private void SaveRunStateCore(DreamsRunState state)
    {
        var path = GetRunStatePath(state.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempFile = Path.Combine(Path.GetDirectoryName(path)!, $".state.{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(state, AppConfig.SerializerOptions) + Environment.NewLine;
        File.WriteAllText(tempFile, json, Encoding.UTF8);
        try
        {
            if (File.Exists(path))
            {
                File.Replace(tempFile, path, null);
            }
            else
            {
                File.Move(tempFile, path);
            }
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    private string GetRunStatePath(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            throw new ArgumentException("Dream run id is required.", nameof(runId));
        foreach (var ch in runId)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')
                continue;
            throw new ArgumentException($"Dream run id contains invalid characters: {runId}", nameof(runId));
        }

        return Path.Combine(_runsDir, runId, "state.json");
    }
}
