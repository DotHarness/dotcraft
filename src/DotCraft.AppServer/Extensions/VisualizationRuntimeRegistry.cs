using System.Collections.Concurrent;
using DotCraft.Context;
using DotCraft.AppServer;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.Visualizations;

/// <summary>Tracks thread-scoped Desktop visualization capabilities and authoring roots.</summary>
public sealed class VisualizationRuntimeRegistry(
    VisualizationAssetStore assets) : IThreadSystemPromptContextProvider
{
    private readonly ConcurrentDictionary<string, Binding> _bindings = new(StringComparer.Ordinal);

    public ContextPageKey ContextPageKey => ContextPageKeys.Visualization();

    /// <inheritdoc />
    public ThreadPromptPlacement Placement => ThreadPromptPlacement.ThreadContextItem;

    /// <summary>Binds one capable connection to a thread.</summary>
    public bool BindThread(
        SessionThread thread,
        IAppServerTransport transport,
        AppServerConnection connection,
        bool replaceExisting = true)
    {
        if (!connection.SupportsVisualizations || connection.IsClosed)
            return false;

        if (_bindings.TryGetValue(thread.Id, out var existing) && !existing.Connection.IsClosed
            && (!replaceExisting || ReferenceEquals(existing.Connection, connection)))
            return false;

        string directory;
        try
        {
            directory = assets.GetAuthoringDirectory(thread);
        }
        catch (Exception ex) when (ex is VisualizationException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
        var candidate = new Binding(transport, connection, directory);
        var binding = _bindings.AddOrUpdate(thread.Id, candidate, (_, current) =>
            ReferenceEquals(current.Connection, connection) || (!replaceExisting && !current.Connection.IsClosed)
                ? current : candidate);
        return ReferenceEquals(binding, candidate);
    }

    /// <summary>Removes bindings owned by a disconnected transport.</summary>
    public IReadOnlyList<string> UnbindTransport(IAppServerTransport transport)
    {
        var removed = new List<string>();
        foreach (var pair in _bindings.ToArray())
        {
            if (ReferenceEquals(pair.Value.Transport, transport)
                && _bindings.TryRemove(pair))
            {
                removed.Add(pair.Key);
            }
        }
        return removed;
    }

    /// <summary>Gets the active authoring root for a thread.</summary>
    public bool TryGetAuthoringDirectory(string threadId, out string directory)
    {
        if (_bindings.TryGetValue(threadId, out var binding) && !binding.Connection.IsClosed)
        {
            directory = binding.Directory;
            return true;
        }

        directory = string.Empty;
        return false;
    }

    /// <summary>Returns whether the supplied connection owns the active thread binding.</summary>
    public bool IsBoundTo(string threadId, AppServerConnection connection) =>
        _bindings.TryGetValue(threadId, out var binding)
        && !binding.Connection.IsClosed
        && ReferenceEquals(binding.Connection, connection);

    public string? GetSystemPromptSection(ThreadSystemPromptContext context)
    {
        if (!TryGetAuthoringDirectory(context.ThreadId, out var directory))
            return null;

        return $$"""
        # Visualizations

        DotCraft Desktop can render inline HTML visualizations in this thread.
        Write each visualization as an HTML fragment with the ordinary `WriteFile` tool directly inside this writable directory:

        `{{directory}}`

        Read the file back with `ReadFile`, then place `visualize{"path":"<file-name>.html"}` on its own line where the visualization belongs. The reference uses only the file name; the host resolves it in the directory above. To update a visualization, rewrite its file and include its reference again. Include a reference only for a file that was written successfully.
        """;
    }

    private sealed record Binding(
        IAppServerTransport Transport,
        AppServerConnection Connection,
        string Directory);
}
