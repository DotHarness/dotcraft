using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.CodexGateway.Workspace;

public sealed record CodexWorkspaceRegistration(string Path, string Name, DateTimeOffset AddedAt, DateTimeOffset LastOpenedAt);
public sealed record CodexWorkspace(string Id, string Path, string Name, int Position, DateTimeOffset AddedAt, DateTimeOffset LastOpenedAt);

public sealed class CodexWorkspaceCatalog
{
    private readonly Func<IReadOnlyList<CodexWorkspaceRegistration>> _source;
    private readonly string _stateDirectory;
    private readonly string _selectionPath;
    private readonly object _gate = new();
    private IReadOnlyList<CodexWorkspace> _workspaces = [];
    private JsonObject _selection;

    public CodexWorkspaceCatalog(Func<IReadOnlyList<CodexWorkspaceRegistration>> source, string stateDirectory)
    {
        _source = source;
        _stateDirectory = Path.GetFullPath(stateDirectory);
        Directory.CreateDirectory(_stateDirectory);
        _selectionPath = Path.Combine(_stateDirectory, "project-selection.json");
        _selection = File.Exists(_selectionPath) ? JsonNode.Parse(File.ReadAllText(_selectionPath))!.AsObject() : new();
        Refresh();
    }

    public IReadOnlyList<CodexWorkspace> Workspaces => Snapshot();
    internal event Action? Changed;

    public void Refresh() => Snapshot();

    internal IReadOnlyList<CodexWorkspace> Snapshot()
    {
        IReadOnlyList<CodexWorkspace> snapshot;
        bool changed;
        lock (_gate)
        {
            snapshot = _source().Select((entry, index) =>
            {
                var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(entry.Path));
                var routes = WorkspaceRoutes.Open(_stateDirectory, path);
                var id = routes.External("project", path);
                return new CodexWorkspace(id, path, entry.Name, index, entry.AddedAt, entry.LastOpenedAt);
            }).ToArray();
            changed = !_workspaces.SequenceEqual(snapshot);
            _workspaces = snapshot;
            PruneSelection(snapshot);
        }
        if (changed) Changed?.Invoke();
        return snapshot;
    }

    internal bool Contains(string id)
    {
        lock (_gate) return _workspaces.Any(workspace => workspace.Id == id);
    }

    internal WorkspaceRoutes Routes(CodexWorkspace workspace) => WorkspaceRoutes.Open(_stateDirectory, workspace.Path);

    internal static CodexWorkspace ById(IReadOnlyList<CodexWorkspace> workspaces, string id) =>
        workspaces.FirstOrDefault(w => w.Id == id)
        ?? throw new ArgumentException("Project is not authorized by this gateway.");

    internal CodexWorkspace ByThread(IReadOnlyList<CodexWorkspace> workspaces, string id) =>
        workspaces.FirstOrDefault(w => Routes(w).Contains(id, "thread"))
        ?? throw new ArgumentException("Conversation is not exposed by this gateway.");

    internal static CodexWorkspace ByPath(IReadOnlyList<CodexWorkspace> workspaces, string path) =>
        FindPath(workspaces, path) ?? throw new ArgumentException("Directory is outside the authorized workspaces.");

    private static CodexWorkspace? FindPath(IReadOnlyList<CodexWorkspace> workspaces, string path)
    {
        var full = Path.GetFullPath(path);
        return workspaces.OrderByDescending(w => w.Path.Length).FirstOrDefault(w =>
        {
            var relative = Path.GetRelativePath(w.Path, full);
            return relative == "." || (!Path.IsPathRooted(relative)
                && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        });
    }

    internal static CodexWorkspace Select(IReadOnlyList<CodexWorkspace> workspaces, JsonElement parameters)
    {
        var project = parameters.Text("projectId") is { } id ? ById(workspaces, id) : null;
        var cwd = parameters.Text("cwd") is { } path ? ByPath(workspaces, path) : null;
        if (project is not null && cwd is not null && project.Id != cwd.Id)
            throw new ArgumentException("Project and cwd belong to different workspaces.");
        return project ?? cwd ?? RequireDefault(workspaces);
    }

    internal static CodexWorkspace RequireDefault(IReadOnlyList<CodexWorkspace> workspaces) =>
        workspaces.Count > 0 ? workspaces[0] : throw new ProjectRequiredException();

    internal static object Project(CodexWorkspace workspace) => new
    {
        id = workspace.Id, name = workspace.Name, roots = new[] { new { path = workspace.Path } },
        metadata = new Dictionary<string, string>(), position = workspace.Position,
        createdAt = workspace.AddedAt.ToUnixTimeSeconds(),
        updatedAt = workspace.LastOpenedAt.ToUnixTimeSeconds(),
        recencyAt = workspace.LastOpenedAt.ToUnixTimeSeconds()
    };

    internal JsonObject ReadState(IReadOnlyList<CodexWorkspace> workspaces)
    {
        lock (_gate)
        {
            var state = _selection.DeepClone().AsObject();
            state["electron-saved-workspace-roots"] = JsonSerializer.SerializeToNode(workspaces.Select(w => w.Path));
            state["electron-workspace-root-labels"] = JsonSerializer.SerializeToNode(workspaces.ToDictionary(w => w.Path, w => w.Name));
            state["active-workspace-roots"] ??= JsonSerializer.SerializeToNode(workspaces.Take(1).Select(w => w.Path));
            state["local-projects"] = JsonSerializer.SerializeToNode(workspaces.ToDictionary(w => w.Id, w => new
            {
                id = w.Id, name = w.Name, rootPaths = new[] { w.Path },
                createdAt = w.AddedAt.ToUnixTimeMilliseconds(), updatedAt = w.LastOpenedAt.ToUnixTimeMilliseconds()
            }));
            state["project-order"] = JsonSerializer.SerializeToNode(workspaces.Select(w => w.Id));
            if (workspaces.Count > 0)
                state["selected-project"] ??= JsonSerializer.SerializeToNode(new { type = "local", projectId = workspaces[0].Id });
            return state;
        }
    }

    internal void WriteState(JsonObject state, IReadOnlyList<CodexWorkspace> workspaces)
    {
        if (state["active-workspace-roots"] is JsonArray roots)
            foreach (var root in roots) ByPath(workspaces, root!.GetValue<string>());
        if (state["selected-project"] is JsonObject project)
        {
            if (project["type"]?.GetValue<string>() != "local")
                throw new ArgumentException("Only authorized local projects are available.");
            ById(workspaces, project["projectId"]!.GetValue<string>());
        }
        var selected = new JsonObject();
        foreach (var key in new[] { "active-workspace-roots", "selected-project" })
            if (state.TryGetPropertyValue(key, out var value)) selected[key] = value?.DeepClone();
        lock (_gate)
        {
            var temporary = _selectionPath + ".tmp";
            File.WriteAllText(temporary, selected.ToJsonString());
            File.Move(temporary, _selectionPath, true);
            _selection = selected;
        }
    }

    private void PruneSelection(IReadOnlyList<CodexWorkspace> workspaces)
    {
        if (_selection["active-workspace-roots"] is JsonArray roots)
        {
            var remaining = roots.Where(root => FindPath(workspaces, root!.GetValue<string>()) is not null)
                .Select(root => root!.DeepClone()).ToArray();
            if (remaining.Length == 0 && roots.Count > 0) _selection.Remove("active-workspace-roots");
            else if (remaining.Length != roots.Count) _selection["active-workspace-roots"] = new JsonArray(remaining);
        }
        if (_selection["selected-project"] is JsonObject selected &&
            (selected["type"]?.GetValue<string>() != "local" || !workspaces.Any(w => w.Id == selected["projectId"]?.GetValue<string>())))
            _selection.Remove("selected-project");
    }
}

internal sealed class ProjectRequiredException() : ArgumentException("A registered local project is required.");
