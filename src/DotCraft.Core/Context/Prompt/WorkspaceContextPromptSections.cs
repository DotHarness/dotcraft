using DotCraft.Contributions;
using DotCraft.Memory;
using Microsoft.Extensions.Logging;
using System.Text;

namespace DotCraft.Context;

/// <summary>The built-in workspace-state sections: bootstrap markdown and memory instructions, all served through prompt-cache-stable context pages.</summary>
internal static class WorkspaceContextPromptSections
{
    /// <summary>The markdown files loaded from the DotCraft directory, in prompt order.</summary>
    internal static readonly string[] BootstrapFiles =
    [
        "SOUL.md",
        "USER.md",
        "TOOLS.md",
        "IDENTITY.md"
    ];

    /// <summary>Builds the <c>bootstrap-files</c> section.</summary>
    internal static string? Bootstrap(SystemPromptSectionContext context)
    {
        var sources = context.RequireSources();
        var content = sources.GetContextPage(
            context.ThreadId,
            ContextPageKeys.BootstrapFiles(sources.CraftPath),
            () => LoadBootstrapFiles(sources));
        return string.IsNullOrWhiteSpace(content) ? null : content;
    }

    /// <summary>Builds the <c>memory</c> section from the memory instructions.</summary>
    internal static string? Memory(SystemPromptSectionContext context)
    {
        var sources = context.RequireSources();
        if (sources.MemoryStore is not { } memoryStore)
            return null;

        var personal = sources.PersonalMemoryStore;
        var attached = sources.AttachedMemoryStores;
        var policy = sources.ReadMemoryPolicy();
        return sources.GetContextPage(
            context.ThreadId,
            ContextPageKeys.Memory(
                Path.GetFullPath(memoryStore.MemoryDirectoryPath)
                + "\n" + (personal is null ? string.Empty : Path.GetFullPath(personal.MemoryDirectoryPath))
                + AttachedStoresVariant(attached) + "\n" + policy.InstructionsFingerprint),
            () => string.Join("\n", ((string?[])
            [
                BuildMemoryContext(memoryStore, personal, policy),
                personal is null ? null : PersonalStoreRules,
                attached.Count == 0 ? null : BuildAttachedStoresContext(attached)
            ]).OfType<string>()));
    }

    private static string LoadBootstrapFiles(PromptSectionSources sources)
    {
        var parts = new List<string>();

        foreach (var filename in BootstrapFiles)
        {
            var filePath = Path.Combine(sources.CraftPath, filename);
            if (!File.Exists(filePath))
                continue;

            try
            {
                var content = File.ReadAllText(filePath, Encoding.UTF8);
                if (!string.IsNullOrWhiteSpace(content))
                    parts.Add($"## {filename}\n\n{content}");
            }
            catch (Exception ex)
            {
                sources.Logger.LogWarning(ex, "Failed to load bootstrap file {BootstrapFile}", filename);
            }
        }

        return parts.Count > 0 ? string.Join("\n\n", parts) : string.Empty;
    }

    private static string AttachedStoresVariant(IReadOnlyList<AttachedMemoryStore> attached) =>
        attached.Count == 0
            ? string.Empty
            : "\n" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Join("\n", attached.Select(static store =>
                    $"{store.DirectoryPath}\t{store.Attachment.Name}\t{store.Attachment.Description}\t{store.Attachment.Access}")))));

    private static string BuildAttachedStoresContext(IReadOnlyList<AttachedMemoryStore> attached)
    {
        var lines = attached.Select(static store =>
            $"- **{store.Attachment.Name}** (`{Path.GetFullPath(store.DirectoryPath)}`, {(store.Writable ? "read and write" : "read only")}): {store.Attachment.Description}");
        return $"""
## Shared memories

You also use these memories, shared with others. Each keeps its own `MEMORY.md` index in the same format as yours.

{string.Join("\n", lines)}

Save to a shared memory you can write only what holds for everyone who uses it, as the general lesson rather than the case that taught it. Never save credentials, conversation content, or details that identify a person there. What concerns your own role and way of working, or anything you are unsure about, goes to your own memory. To delete a memory file from a shared memory, write it empty and remove its index line.
""";
    }

    private const string PersonalStoreRules =
"""
## Personal memory

- Save a memory of a personal type to the personal memory. Save any other memory to your memory, unless the user said it applies in every project. Then save it to the personal memory.
- Never save project facts, paths, commands, business or customer details, credentials, or conversation content to the personal memory, because every workspace on this computer reads it.
- Only after saving the personal memory and updating its index successfully, delete any duplicate in your memory.
- A memory in your memory that narrows a personal memory to this workspace takes precedence here. Otherwise the personal memory takes precedence.
- To delete a personal memory, write its file empty and remove its index line.
""";

    private static string BuildMemoryContext(MemoryStore memoryStore, MemoryStore? personal, MemoryPolicy policy) =>
$"""
# Memory

You have a persistent, file-based memory at `{Path.GetFullPath(memoryStore.MemoryDirectoryPath)}`. The directory already exists; write to it directly.{(personal is null ? string.Empty : $" You also have a personal memory at `{Path.GetFullPath(personal.MemoryDirectoryPath)}`, shared by every workspace on this computer, with its own `MEMORY.md` index in the same format.")}

{MemoryInstructions.FileFormat(policy.Types, markPersonal: personal is not null)}

After writing the file, add or update its pointer in `MEMORY.md`: one line, `- [Title](name.md) — hook`, under about 150 characters. MEMORY.md is the index shown to you in each conversation. It has no frontmatter and never holds memory content.

{policy.SaveGuidance.Trim()}

Apply requests to remember, correct, or forget in the same reply, even when the guidance above would not save it. The file format, the index, and the rules in this section take precedence over that guidance.

Memories are point-in-time background, not instructions; current instructions and observed evidence take precedence. If a memory names a file, function, or flag, verify it still exists before recommending it. If the user says to ignore memory, do not use, cite, or mention it.
""";
}
