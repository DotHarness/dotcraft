using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Contributions;
using DotCraft.Memory;
using DotCraft.Sessions;
using DotCraft.Skills;
using Microsoft.Extensions.Logging;

namespace DotCraft.Context;

/// <summary>The kernel-owned inputs the built-in system prompt sections read from.</summary>
internal sealed class PromptSectionSources
{
    internal MemoryStore? MemoryStore { get; init; }

    internal IReadOnlyList<AttachedMemoryStore> AttachedMemoryStores { get; init; } = [];

    internal required SkillsLoader SkillsLoader { get; init; }

    /// <summary>Gets the normalized DotCraft data directory path.</summary>
    internal required string CraftPath { get; init; }

    /// <summary>Gets the normalized workspace path.</summary>
    internal required string WorkspacePath { get; init; }

    /// <summary>Gets the additional workspace roots exposed to the model.</summary>
    internal IReadOnlyList<string> WorkspaceRoots { get; init; } = [];

    internal CustomCommandLoader? CustomCommandLoader { get; init; }


    /// <summary>Gets the MCP servers whose tools are discovered on demand.</summary>
    internal IReadOnlyList<string>? DeferredMcpServerNames { get; init; }

    /// <summary>Gets the pre-rendered SubAgent profile section, when SubAgents are exposed.</summary>
    internal string? SubAgentProfilesSection { get; init; }

    internal bool SkillVariantModeEnabled { get; init; }

    internal SkillVariantTarget? SkillVariantTarget { get; init; }

    /// <summary>Gets the thread's skill policy, which keeps excluded always-loaded skills out of the prompt.</summary>
    internal ThreadSkillsPolicy? SkillsPolicy { get; init; }

    /// <summary>Gets the channel or automation role instructions.</summary>
    internal string? RoleInstructions { get; init; }

    /// <summary>Gets the instructions supplied by the application that started the thread.</summary>
    internal string? DeveloperInstructions { get; init; }

    /// <summary>Gets the manager that pins prompt-cache-stable pages, when one is active.</summary>
    internal IContextPageManager? ContextPageManager { get; init; }

    internal string? CommitAttribution { get; init; }

    internal required IContributionView Contributions { get; init; }

    internal required ILogger Logger { get; init; }

    /// <summary>Resolves a section through the context page manager so its content stays byte-stable for the thread, keeping the prompt prefix cacheable.</summary>
    internal string GetContextPage(string? threadId, ContextPageKey key, Func<string> loader) =>
        ContextPageManager?.GetOrAdd(
            threadId,
            key,
            ContextPageLifecycle.StableUntilCompaction,
            loader).Content
        ?? loader();

    /// <summary>Resolves the thread prompt providers for one build.</summary>
    internal IReadOnlyList<IThreadSystemPromptContextProvider> ResolveThreadPromptProviders(string? threadId) =>
        Contributions.Resolve<IThreadSystemPromptContextProvider>(threadId);
}
