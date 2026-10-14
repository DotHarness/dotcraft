using System.Text.Json.Nodes;
using DotCraft.Security;
using DotCraft.Tools;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    internal const string MemoryPresentationOption = "memory";

    /// <summary>The <c>memory</c> option of a file tool call whose target lies inside the Thread's memory store, or null.</summary>
    private string? ResolveMemoryPresentation(
        ToolDefinition definition,
        JsonObject? arguments,
        string? workspacePath,
        ThreadConfiguration? configuration)
    {
        var mark = definition.Id.Kind == ToolSourceKind.CoreNative
            ? definition.Presentation?.Id.Value switch
            {
                "core.read-file" => "recall",
                "core.file-write" => "update",
                _ => null
            }
            : null;
        return mark is not null
               && configuration is not null and not { MemoryEnabled: false }
               && FileToolAccess.TryResolveTarget(definition.Id.SourceToolId.Value, arguments, workspacePath, out _, out var target)
               && WorkspaceBoundary.IsWithin(target, ResolveMemoryStore(configuration).MemoryDirectoryPath)
            ? mark
            : null;
    }
}
