using System.Text;
using System.Text.Json;
using DotCraft.Sessions.Wire;

namespace DotCraft.Teams;

internal sealed class TeamsStateStore(string workspaceCraftPath)
{
    internal const int CurrentSchemaVersion = 1;

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
            return CreateNewState();

        try
        {
            var json = File.ReadAllText(_statePath);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("schemaVersion", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var schemaVersion)
                || schemaVersion != CurrentSchemaVersion)
            {
                return CreateNewState();
            }

            var state = JsonSerializer.Deserialize<TeamsStateDocument>(json, SessionWireJsonOptions.Default);
            return state is { SchemaVersion: CurrentSchemaVersion } ? state : CreateNewState();
        }
        catch
        {
            return CreateNewState();
        }
    }

    private void SaveNoLock(TeamsStateDocument state)
    {
        state.SchemaVersion = CurrentSchemaVersion;
        var directory = Path.GetDirectoryName(_statePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(
            state,
            new JsonSerializerOptions(SessionWireJsonOptions.Default)
            {
                WriteIndented = true
            });
        var temporaryPath = $"{_statePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, $"{json}{Environment.NewLine}", new UTF8Encoding(false));
            File.Move(temporaryPath, _statePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static TeamsStateDocument Clone(TeamsStateDocument state) =>
        JsonSerializer.Deserialize<TeamsStateDocument>(
            JsonSerializer.Serialize(state, SessionWireJsonOptions.Default),
            SessionWireJsonOptions.Default) ?? CreateNewState();

    private static TeamsStateDocument CreateNewState() => new()
    {
        SchemaVersion = CurrentSchemaVersion
    };
}
