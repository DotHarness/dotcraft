using DotCraft.Contributions;

namespace DotCraft.Context;

/// <summary>Built-in sections that bridge other contribution points into the prompt, plus deferred MCP discovery and role instructions.</summary>
internal static class ProviderPromptSections
{
    /// <summary>Builds the <c>chat-context</c> section from the registered chat context providers.</summary>
    internal static string? ChatContext(SystemPromptSectionContext context)
    {
        var sources = context.RequireSources();
        var providers = sources.Contributions.Resolve<IChatContextProvider>(context.ThreadId);
        if (providers.Count == 0)
            return null;

        var parts = new List<string>(providers.Count);
        foreach (var provider in providers)
        {
            var section = provider.GetSystemPromptSection();
            if (!string.IsNullOrWhiteSpace(section))
                parts.Add(section);
        }

        return SystemPromptComposition.Join(parts);
    }

    /// <summary>Builds the <c>thread-context</c> section from providers declaring <see cref="ThreadPromptPlacement.BaseInstructions"/>.</summary>
    internal static string? ThreadContext(SystemPromptSectionContext context)
    {
        var threadId = context.ThreadId;
        if (string.IsNullOrWhiteSpace(threadId))
            return null;

        var sources = context.RequireSources();
        var providers = sources.ResolveThreadPromptProviders(threadId);
        if (providers.Count == 0)
            return null;

        var promptContext = new ThreadSystemPromptContext(
            threadId.Trim(),
            sources.WorkspacePath,
            context.OriginChannel);

        var parts = new List<string>(providers.Count);
        foreach (var provider in providers)
        {
            // ThreadContextItem providers are connection-bound; they ship as appended history items so a
            // binding change cannot invalidate the cached instruction prefix.
            if (provider.Placement != ThreadPromptPlacement.BaseInstructions)
                continue;

            var section = sources.GetContextPage(
                threadId,
                provider.ContextPageKey,
                () => provider.GetSystemPromptSection(promptContext) ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(section))
                parts.Add(section);
        }

        return SystemPromptComposition.Join(parts);
    }

    /// <summary>Builds the <c>deferred-tools</c> section that points the model at <c>SearchTools</c> for on-demand MCP tools.</summary>
    internal static string? DeferredTools(SystemPromptSectionContext context)
    {
        if (context.RequireSources().DeferredMcpServerNames is not { Count: > 0 } serverNames)
            return null;

        var servers = string.Join(", ", serverNames);
        return
$$"""
## Available Tool Sources

You have a core set of tools available directly. Additional tools from external
services (MCP servers) are available on demand.

To use an external tool:
1. Call `SearchTools` with keywords describing what you need
2. The matching tools will become available for use
3. Call the discovered tool directly

Do NOT guess tool names. Always use SearchTools to discover available tools first.
Currently connected external services: {{servers}}
""";
    }

    /// <summary>Builds the <c>role-instructions</c> section.</summary>
    internal static string? RoleInstructions(SystemPromptSectionContext context)
    {
        var roleInstructions = context.RequireSources().RoleInstructions;
        return string.IsNullOrWhiteSpace(roleInstructions)
            ? null
            : $"## Role Instructions\n\n{roleInstructions.Trim()}";
    }

    /// <summary>Builds the <c>developer-instructions</c> section.</summary>
    internal static string? DeveloperInstructions(SystemPromptSectionContext context)
    {
        var developerInstructions = context.RequireSources().DeveloperInstructions;
        return string.IsNullOrWhiteSpace(developerInstructions)
            ? null
            : $"## Developer Instructions\n\n{developerInstructions.Trim()}";
    }
}
