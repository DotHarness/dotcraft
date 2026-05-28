using System.Text.Json;

namespace DotCraft.Hub;

/// <summary>
/// Best-effort persistence for Hub-known AppServer metadata.
/// </summary>
internal sealed class HubAppServerRegistryStore
{
    private readonly string _path;

    public HubAppServerRegistryStore(string path)
    {
        _path = path;
    }

    public IReadOnlyDictionary<string, HubAppServerRegistryRecord> Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new Dictionary<string, HubAppServerRegistryRecord>(WorkspaceComparer);

            var json = File.ReadAllText(_path);
            var document = JsonSerializer.Deserialize<HubAppServerRegistryDocument>(json, HubJson.Options);
            return (document?.AppServers ?? [])
                .Where(r => !string.IsNullOrWhiteSpace(r.CanonicalWorkspacePath))
                .GroupBy(r => r.CanonicalWorkspacePath, WorkspaceComparer)
                .ToDictionary(g => g.Key, g => g.First(), WorkspaceComparer);
        }
        catch
        {
            return new Dictionary<string, HubAppServerRegistryRecord>(WorkspaceComparer);
        }
    }

    public void Save(IEnumerable<HubAppServerRegistryRecord> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var document = new HubAppServerRegistryDocument(
            Version: 1,
            UpdatedAt: DateTimeOffset.UtcNow,
            AppServers: records
                .Where(r => !string.IsNullOrWhiteSpace(r.CanonicalWorkspacePath))
                .OrderBy(r => r.CanonicalWorkspacePath, WorkspaceComparer)
                .ToArray());

        var tempPath = _path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(document, HubJson.Options));
        try
        {
            File.Move(tempPath, _path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    private static StringComparer WorkspaceComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

internal sealed record HubAppServerRegistryDocument(
    int Version,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<HubAppServerRegistryRecord> AppServers);

internal sealed record HubAppServerRegistryRecord(
    string WorkspacePath,
    string CanonicalWorkspacePath,
    string DisplayName,
    string State,
    int? Pid,
    IReadOnlyDictionary<string, string> Endpoints,
    IReadOnlyDictionary<string, HubServiceStatus> ServiceStatus,
    string? ServerVersion,
    bool StartedByHub,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? LastExitedAt,
    int? ExitCode,
    string? LastError,
    string? RecentStderr)
{
    public HubAppServerResponse ToResponse() => new(
        WorkspacePath,
        CanonicalWorkspacePath,
        State,
        Pid,
        Endpoints,
        ServiceStatus,
        ServerVersion,
        StartedByHub,
        ExitCode,
        LastError,
        RecentStderr);
}
