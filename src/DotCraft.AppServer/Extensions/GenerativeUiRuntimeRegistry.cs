using System.Collections.Concurrent;
using DotCraft.AppServer;
using DotCraft.Context;

namespace DotCraft.GenerativeUi;

/// <summary>Tells the model about Generative UI in threads bound by a capable, open connection.</summary>
public sealed class GenerativeUiRuntimeRegistry : IThreadSystemPromptContextProvider
{
    private const string Section = """
        # Generative UI

        DotCraft Desktop renders fenced `dotcraft-ui` blocks in your replies as interactive answers built from its own components. Before writing one, load the `generative-ui` skill with `SkillView` and follow it.
        """;

    private readonly ConcurrentDictionary<string, AppServerConnection> _bindings = new(StringComparer.Ordinal);

    public ContextPageKey ContextPageKey => ContextPageKeys.GenerativeUi();

    public ThreadPromptPlacement Placement => ThreadPromptPlacement.ThreadContextItem;

    public void BindThread(string threadId, AppServerConnection connection)
    {
        if (!connection.SupportsGenerativeUi || connection.IsClosed)
            return;

        _bindings[threadId] = connection;
    }

    public IReadOnlyList<string> UnbindConnection(AppServerConnection connection)
    {
        var removed = new List<string>();
        foreach (var pair in _bindings.ToArray())
        {
            if (ReferenceEquals(pair.Value, connection) && _bindings.TryRemove(pair))
                removed.Add(pair.Key);
        }
        return removed;
    }

    public string? GetSystemPromptSection(ThreadSystemPromptContext context) =>
        _bindings.TryGetValue(context.ThreadId, out var connection) && !connection.IsClosed ? Section : null;
}
