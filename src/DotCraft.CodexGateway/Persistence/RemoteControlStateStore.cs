using System.Text.Json;

namespace DotCraft.CodexGateway.Persistence;

internal sealed class RemoteControlStateStore : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _path;
    private readonly FileStream _owner;
    private readonly HostState _state;

    public RemoteControlStateStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "host.json");
        _owner = new FileStream(Path.Combine(directory, "owner.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            _state = File.Exists(_path)
                ? JsonSerializer.Deserialize<HostState>(File.ReadAllText(_path), Json)
                    ?? throw new InvalidDataException("codex_remote_control.invalid_state: Invalid host state.")
                : new HostState(Guid.NewGuid().ToString(), []);
            Save();
        }
        catch
        {
            _owner.Dispose();
            throw;
        }
    }

    public string InstallationId => _state.InstallationId;

    public EnrollmentIdentity? Find(Uri origin, string accountId) =>
        _state.Enrollments.GetValueOrDefault(Key(origin, accountId));

    public void Set(Uri origin, string accountId, EnrollmentIdentity? identity)
    {
        var key = Key(origin, accountId);
        if (identity is null)
            _state.Enrollments.Remove(key);
        else
            _state.Enrollments[key] = identity;
        Save();
    }

    private void Save()
    {
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_state, Json));
        File.Move(temporary, _path, overwrite: true);
    }

    private static string Key(Uri origin, string accountId) => origin.AbsoluteUri + "\n" + accountId;

    public void Dispose() => _owner.Dispose();

    private sealed record HostState(
        string InstallationId, Dictionary<string, EnrollmentIdentity> Enrollments);
}

internal sealed record EnrollmentIdentity(string ServerId, string EnvironmentId);
