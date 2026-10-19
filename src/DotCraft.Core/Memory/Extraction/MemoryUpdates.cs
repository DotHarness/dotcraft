using System.Text.Json.Nodes;
using DotCraft.Sessions;
using DotCraft.Tools;

namespace DotCraft.Memory.Extraction;

internal static class MemoryUpdates
{
    /// <summary>The path arguments of the Turn's succeeded writes to the Thread's own store; attached stores are not counted.</summary>
    public static IReadOnlyList<string> SucceededPaths(SessionTurn turn)
    {
        var items = turn.Items.ToArray();
        var succeeded = items
            .Select(static item => item.AsToolResult)
            .Where(static result => result is { Success: true })
            .Select(static result => result!.CallId)
            .ToHashSet(StringComparer.Ordinal);
        var paths = new List<string>();
        foreach (var item in items)
        {
            if (item.AsToolCall is not { } call
                || !succeeded.Contains(call.CallId)
                || call.Presentation?.Options?[SessionService.MemoryPresentationOption] is not JsonValue mark
                || !mark.TryGetValue<string>(out var kind)
                || kind != "update"
                || call.Presentation.Options[SessionService.MemoryStorePresentationOption] is not null)
            {
                continue;
            }

            if (FileToolAccess.TryGet(call.ToolName, out var access)
                && call.Arguments?[access.PathArgument] is JsonValue path
                && path.TryGetValue<string>(out var text))
            {
                paths.Add(text);
            }
        }

        return paths;
    }
}
