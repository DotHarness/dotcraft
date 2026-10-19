using System.Text.RegularExpressions;
using DotCraft.Sessions;

namespace DotCraft.Memory;

/// <summary>Durable knowledge belongs to whoever accumulates it: a Thread that names a scope reads and
/// writes its own store instead of the workspace's, and a scope redirects memory and its history alone.</summary>
public static partial class MemoryScopes
{
    public static MemoryStore ForThread(ThreadConfiguration? configuration, MemoryStore workspaceStore, string dataPath) =>
        OwnDataRoot(configuration, dataPath) is { } root ? new MemoryStore(root) : workspaceStore;

    /// <summary>The directory of the store a Thread with this configuration uses, without creating it.</summary>
    public static string StoreDirectory(ThreadConfiguration? configuration, string dataPath) =>
        MemoryStore.DirectoryOf(OwnDataRoot(configuration, dataPath) ?? dataPath);

    /// <summary>Empties the workspace store and every scope store beside it, keeping their directories.</summary>
    public static void ClearAll(MemoryStore workspaceStore)
    {
        workspaceStore.ClearAll();
        var scopesRoot = Path.Combine(Path.GetDirectoryName(workspaceStore.MemoryDirectoryPath)!, "scopes");
        if (!Directory.Exists(scopesRoot))
            return;

        foreach (var scopeRoot in Directory.EnumerateDirectories(scopesRoot))
        {
            if (Segment().IsMatch(Path.GetFileName(scopeRoot))
                && (File.GetAttributes(scopeRoot) & FileAttributes.ReparsePoint) == 0
                && Directory.Exists(MemoryStore.DirectoryOf(scopeRoot)))
            {
                new MemoryStore(scopeRoot).ClearAll();
            }
        }
    }

    private static string? OwnDataRoot(ThreadConfiguration? configuration, string dataPath)
    {
        if (!string.IsNullOrEmpty(configuration?.MemoryScope))
            return ScopeRoot(configuration.MemoryScope, dataPath);
        return string.IsNullOrEmpty(configuration?.WorkspaceOverride)
            ? null
            : Path.Combine(configuration.WorkspaceOverride, Path.GetFileName(dataPath));
    }

    private static string ScopeRoot(string scope, string dataPath)
    {
        if (!Segment().IsMatch(scope))
            throw new ArgumentException($"Memory scope '{scope}' is not a single safe path segment.", nameof(scope));
        return Path.Combine(dataPath, "scopes", scope);
    }

    public const int MaxAttachedStores = 8;

    public static IReadOnlyList<AttachedMemoryStore> AttachedStores(ThreadConfiguration? configuration, string dataPath)
    {
        if (configuration?.MemoryStores is not { Count: > 0 } attachments)
            return [];

        ValidateAttachments(attachments);
        return attachments
            .Select(attachment =>
            {
                var directory = Path.Combine(dataPath, "memory-stores", attachment.Id);
                Directory.CreateDirectory(directory);
                return new AttachedMemoryStore(attachment, directory);
            })
            .ToArray();
    }

    public static void ValidateAttachments(IReadOnlyList<MemoryStoreAttachment>? attachments)
    {
        if (attachments is null)
            return;
        if (attachments.Count > MaxAttachedStores)
            throw new ArgumentException($"A Thread can attach at most {MaxAttachedStores} memory stores.", nameof(attachments));

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attachment in attachments)
        {
            if (attachment is null || string.IsNullOrEmpty(attachment.Id) || !Segment().IsMatch(attachment.Id))
                throw new ArgumentException($"Memory store id '{attachment?.Id}' is not a single safe path segment.", nameof(attachments));
            if (!ids.Add(attachment.Id))
                throw new ArgumentException($"Memory store '{attachment.Id}' is attached more than once.", nameof(attachments));
            if (string.IsNullOrWhiteSpace(attachment.Name) || string.IsNullOrWhiteSpace(attachment.Description))
                throw new ArgumentException($"Memory store '{attachment.Id}' needs a name and a description.", nameof(attachments));
            if (attachment.Access is not (MemoryStoreAccess.Read or MemoryStoreAccess.ReadWrite))
                throw new ArgumentException($"Memory store '{attachment.Id}' has unknown access '{attachment.Access}'.", nameof(attachments));
        }
    }

    [GeneratedRegex(@"^(?!\.\.?$)[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex Segment();
}
