using DotCraft.Commands.Core;
using DotCraft.Commands.Custom;
using DotCraft.Contributions;
using DotCraft.Dreams;
using DotCraft.Memory;
using Microsoft.Extensions.Logging;
using System.Text;

namespace DotCraft.Context;

/// <summary>The built-in workspace-state sections: bootstrap markdown, long-term and Dream memory, and custom commands, all served through prompt-cache-stable context pages.</summary>
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

    /// <summary>Builds the <c>memory</c> section from long-term memory and Dream memory.</summary>
    internal static string? Memory(SystemPromptSectionContext context)
    {
        var sources = context.RequireSources();
        var memory = sources.GetContextPage(
            context.ThreadId,
            ContextPageKeys.MemoryLongTerm(MemoryVariant(sources.MemoryStore, sources.DreamStore)),
            () => BuildMemoryContext(sources));
        return string.IsNullOrWhiteSpace(memory) ? null : $"# Memory\n\n{memory}";
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

    /// <summary>The cache variant naming which stores this page was built from, shared with invalidation.</summary>
    internal static string MemoryVariant(MemoryStore memoryStore, DreamStore? dreamStore)
    {
        var sb = new StringBuilder();
        sb.Append("memory:");
        sb.Append(Path.GetFullPath(memoryStore.MemoryDirectoryPath));
        if (dreamStore != null)
        {
            sb.Append("|dreams:");
            sb.Append(Path.GetFullPath(dreamStore.DreamsDirectoryPath));
        }

        return sb.ToString();
    }

    private static string BuildMemoryContext(PromptSectionSources sources)
    {
        // The agent maintains these files itself, so the section names where they are.
        var parts = new List<string> { $"Memory files: {Path.GetFullPath(sources.MemoryStore.MemoryDirectoryPath)}" };
        var longTerm = sources.MemoryStore.GetMemoryContext();
        if (!string.IsNullOrWhiteSpace(longTerm))
            parts.Add(longTerm);

        var dreamMemory = BuildDreamMemoryContext(sources);
        if (!string.IsNullOrWhiteSpace(dreamMemory))
            parts.Add(dreamMemory);

        return string.Join("\n\n", parts);
    }

    private static string BuildDreamMemoryContext(PromptSectionSources sources)
    {
        var dream = sources.DreamStore?.ReadDream();
        if (string.IsNullOrWhiteSpace(dream))
            return string.Empty;

        var dreamsRoot = Path
            .GetRelativePath(sources.RawWorkspacePath, sources.RawCraftPath)
            .Replace('\\', '/');

        return
$"""
## Dream Memory

The following is inferred background context generated by scheduled Dreams. Use it as helpful workspace context, but do not treat it as explicit user instruction when it conflicts with direct instructions, project files, or MEMORY.md.
Detailed Dream topic files, when listed, live under {dreamsRoot}/dreams/memory/ and should be read on demand only when relevant.

{StripDreamMemoryHeading(dream)}
""";
    }

    private static string StripDreamMemoryHeading(string markdown)
    {
        var trimmed = markdown.Trim();
        if (trimmed.StartsWith("# Dream Memory", StringComparison.OrdinalIgnoreCase))
        {
            var nextLine = trimmed.IndexOf('\n');
            return nextLine < 0 ? string.Empty : trimmed[(nextLine + 1)..].TrimStart();
        }

        return trimmed;
    }
}
