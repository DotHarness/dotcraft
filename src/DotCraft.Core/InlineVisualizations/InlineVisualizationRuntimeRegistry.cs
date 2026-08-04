using System.Collections.Concurrent;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Protocol.AppServer;
using DotCraft.AppServer;
using DotCraft.Sessions;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.InlineVisualizations;

/// <summary>Tracks thread-scoped Desktop visualization capabilities and authoring roots.</summary>
public sealed class InlineVisualizationRuntimeRegistry(
    InlineVisualizationAssetStore assets,
    AppConfig config) : IThreadSystemPromptContextProvider
{
    private readonly ConcurrentDictionary<string, Binding> _bindings = new(StringComparer.Ordinal);

    public ContextPageKey ContextPageKey => ContextPageKeys.InlineVisualization();

    /// <summary>Binds one capable connection to a thread.</summary>
    public bool BindThread(
        SessionThread thread,
        IAppServerTransport transport,
        AppServerConnection connection)
    {
        if (!connection.SupportsInlineVisualizations || config.Tools.Sandbox.Enabled)
            return false;

        string directory;
        try
        {
            directory = assets.GetAuthoringDirectory(thread);
        }
        catch (Exception ex) when (ex is InlineVisualizationException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
        _bindings[thread.Id] = new Binding(transport, connection, directory);
        return true;
    }

    /// <summary>Removes bindings owned by a disconnected transport.</summary>
    public IReadOnlyList<string> UnbindTransport(IAppServerTransport transport)
    {
        var removed = new List<string>();
        foreach (var pair in _bindings.ToArray())
        {
            if (ReferenceEquals(pair.Value.Transport, transport)
                && _bindings.TryRemove(pair.Key, out _))
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
        # Inline Visualizations

        DotCraft Desktop can render inline HTML visualizations in this thread.
        Write each visualization as a new HTML fragment with the ordinary `WriteFile` tool under this writable directory:

        `{{directory}}`

        Read the file back with `ReadFile`, then place `::dotcraft-inline-vis{file="<lowercase-hyphen-name>.html"}` on its own line where the visualization belongs. Do not emit the directive unless the file was written successfully.
        """;
    }

    private sealed record Binding(
        IAppServerTransport Transport,
        AppServerConnection Connection,
        string Directory);
}
