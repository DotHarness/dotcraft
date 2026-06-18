using System.Text;
using System.Text.Json;
using DotCraft.Protocol;

namespace DotCraft.Teams;

internal sealed class TeamsStateStore(string workspaceCraftPath)
{
    private readonly Lock _lock = new();
    private readonly string _statePath = Path.Combine(workspaceCraftPath, "teams", "state.json");

    public TeamsStateDocument Snapshot()
    {
        lock (_lock)
        {
            return Clone(LoadNoLock());
        }
    }

    public T Update<T>(Func<TeamsStateDocument, T> update)
    {
        lock (_lock)
        {
            var state = LoadNoLock();
            var result = update(state);
            SaveNoLock(state);
            return result;
        }
    }

    private TeamsStateDocument LoadNoLock()
    {
        if (!File.Exists(_statePath))
            return new TeamsStateDocument();

        try
        {
            return JsonSerializer.Deserialize<TeamsStateDocument>(
                       File.ReadAllText(_statePath),
                       SessionWireJsonOptions.Default)
                   ?? new TeamsStateDocument();
        }
        catch
        {
            return new TeamsStateDocument();
        }
    }

    private void SaveNoLock(TeamsStateDocument state)
    {
        var directory = Path.GetDirectoryName(_statePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(
            state,
            new JsonSerializerOptions(SessionWireJsonOptions.Default)
            {
                WriteIndented = true
            });
        File.WriteAllText(_statePath, $"{json}{Environment.NewLine}", new UTF8Encoding(false));
    }

    private static TeamsStateDocument Clone(TeamsStateDocument state) =>
        JsonSerializer.Deserialize<TeamsStateDocument>(
            JsonSerializer.Serialize(state, SessionWireJsonOptions.Default),
            SessionWireJsonOptions.Default) ?? new TeamsStateDocument();
}
