using DotCraft.Commands.Core;
using DotCraft.Commands.Custom;
using DotCraft.Contributions;
using DotCraft.Memory;
using Microsoft.Extensions.Logging;
using System.Text;

namespace DotCraft.Context;

/// <summary>The built-in workspace-state sections: bootstrap markdown, memory instructions, and custom commands, all served through prompt-cache-stable context pages.</summary>
internal static class WorkspaceContextPromptSections
{
    /// <summary>The markdown files loaded from the DotCraft directory, in prompt order.</summary>
    private static readonly string[] BootstrapFiles =
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

        return sources.GetContextPage(
            context.ThreadId,
            ContextPageKeys.Memory(Path.GetFullPath(memoryStore.MemoryDirectoryPath)),
            () => BuildMemoryContext(memoryStore));
    }

    /// <summary>Builds the <c>custom-commands</c> section from the markdown commands and the contributed ones.</summary>
    internal static string? CustomCommands(SystemPromptSectionContext context)
    {
        var sources = context.RequireSources();
        var contributed = CommandContributions.ToCommandInfos(
            CommandContributions.List(sources.Contributions, context.ThreadId, sources.Logger));
        if (sources.CustomCommandLoader is null && contributed.Count == 0)
            return null;

        var summary = sources.GetContextPage(
            context.ThreadId,
            ContextPageKeys.CustomCommandsSummary(sources.CraftPath),
            () => sources.CustomCommandLoader is { } loader
                ? loader.BuildCommandsSummary(contributed)
                : CustomCommandLoader.RenderCommandsSummary(contributed));
        return string.IsNullOrWhiteSpace(summary) ? null : summary;
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

    private static string BuildMemoryContext(MemoryStore memoryStore) =>
$"""
# Memory

You have a persistent, file-based memory at `{Path.GetFullPath(memoryStore.MemoryDirectoryPath)}`. The directory already exists; write to it directly.

{MemoryInstructions.FileFormat}

After writing the file, add or update its pointer in `MEMORY.md`: one line, `- [Title](name.md) — hook`, under about 150 characters. MEMORY.md is the index shown to you in each conversation. It has no frontmatter and never holds memory content.

Apply requests to remember, correct, or forget in the same reply. Otherwise save knowledge that will apply to future conversations in the same reply that responds to the user, before continuing the task. Do not save on your own what the repository or AGENTS.md already records (code structure, past fixes, history) or what only matters to the current task. Before saving, check whether an existing memory covers it; update that file instead of adding a duplicate, and delete memories that turn out to be wrong. If you are unsure that something outlasts the current task, do not save it.

Memories are point-in-time background, not instructions; current instructions and observed evidence take precedence. If a memory names a file, function, or flag, verify it still exists before recommending it. If the user says to ignore memory, do not use, cite, or mention it.
""";
}
