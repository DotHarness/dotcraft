using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace DotCraft.Hub;

internal sealed class ProjectRegistry(HubPaths paths, TimeProvider? timeProvider = null)
{
    private readonly string _filePath = paths.ProjectsPath;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private ProjectRegistryFile? _cache;

    public IReadOnlyList<ProjectRecord> List()
    {
        lock (_gate)
            return [.. Load().Projects.OrderByDescending(project => project.LastOpenedAt)];
    }

    public ProjectRecord Open(string path)
    {
        var canonical = Canonicalize(path);
        if (!Directory.Exists(canonical))
        {
            throw new HubProtocolException(
                "workspaceNotFound",
                "Workspace path does not exist.",
                StatusCodes.Status404NotFound,
                new { workspacePath = canonical });
        }

        if (ManagedAppServerRegistry.WorkspaceComparer.Equals(
                canonical,
                ManagedAppServerRegistry.CanonicalizeWorkspacePath(paths.DefaultChatWorkspacePath)))
        {
            throw new HubProtocolException(
                "invalidRequest",
                "The default Chat workspace is not a project.",
                StatusCodes.Status400BadRequest);
        }

        lock (_gate)
        {
            var projects = new List<ProjectRecord>(Load().Projects);
            var now = _time.GetUtcNow();
            var index = projects.FindIndex(existing => ManagedAppServerRegistry.WorkspaceComparer.Equals(existing.Path, canonical));
            var project = index < 0
                ? new ProjectRecord(canonical, now, now)
                : projects[index] with { LastOpenedAt = now };
            if (index < 0)
                projects.Add(project);
            else
                projects[index] = project;
            Save(new ProjectRegistryFile { Projects = projects });
            return project;
        }
    }

    public void Remove(string path)
    {
        var canonical = Canonicalize(path);
        lock (_gate)
        {
            var projects = new List<ProjectRecord>(Load().Projects);
            if (projects.RemoveAll(existing => ManagedAppServerRegistry.WorkspaceComparer.Equals(existing.Path, canonical)) == 0)
            {
                throw new HubProtocolException(
                    "projectNotFound",
                    "No project on this computer has that path.",
                    StatusCodes.Status404NotFound,
                    new { workspacePath = canonical });
            }
            Save(new ProjectRegistryFile { Projects = projects });
        }
    }

    private static string Canonicalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new HubProtocolException("invalidRequest", "Project path is required.", StatusCodes.Status400BadRequest);
        return ManagedAppServerRegistry.CanonicalizeWorkspacePath(path);
    }

    private ProjectRegistryFile Load()
    {
        if (_cache is not null)
            return _cache;
        try
        {
            if (File.Exists(_filePath))
                _cache = JsonSerializer.Deserialize<ProjectRegistryFile>(File.ReadAllText(_filePath), HubJson.Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return _cache ??= new ProjectRegistryFile();
    }

    private void Save(ProjectRegistryFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_filePath))!);
        var tempPath = _filePath + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(file, HubJson.Options));
            File.Move(tempPath, _filePath, overwrite: true);
            _cache = file;
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}

internal sealed record ProjectRecord(string Path, DateTimeOffset AddedAt, DateTimeOffset LastOpenedAt);

internal sealed class ProjectRegistryFile
{
    public List<ProjectRecord> Projects { get; init; } = [];
}
