using System.Text.Json.Nodes;
using DotCraft.Memory;
using DotCraft.Tools;

namespace DotCraft.Sessions;

internal readonly record struct MemoryPresentationMark(string Kind, MemoryStoreAttachment? Store);

public sealed partial class SessionService
{
    internal const string MemoryPresentationOption = "memory";

    internal const string MemoryStorePresentationOption = "memoryStore";

    /// <summary>The memory mark of a file tool call whose target lies inside one of the Thread's memory stores, or null.</summary>
    private MemoryPresentationMark? ResolveMemoryPresentation(
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
               && ResolveMemoryStores(configuration).Classify(target) is { } store
            ? new MemoryPresentationMark(mark, store.Attached?.Attachment)
            : null;
    }
}
