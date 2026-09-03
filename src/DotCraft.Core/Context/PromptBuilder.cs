using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Contributions;
using DotCraft.Dreams;
using DotCraft.Memory;
using DotCraft.Skills;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.Context;

/// <summary>Assembles the complete system prompt from the <see cref="ISystemPromptSection"/> contributions resolved per build; see <see cref="SystemPromptSectionCatalog"/> for the built-in set.</summary>
public sealed class PromptBuilder(
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    string craftPath,
    string workspacePath,
    CustomCommandLoader? customCommandLoader = null,
    bool sandboxEnabled = false,
    IReadOnlyList<string>? deferredMcpServerNames = null,
    string? subAgentProfilesSection = null,
    Func<IReadOnlyList<string>>? toolNamesProvider = null,
    bool skillVariantModeEnabled = false,
    SkillVariantTarget? skillVariantTarget = null,
    string? roleInstructions = null,
    string? developerInstructions = null,
    IContextPageManager? contextPageManager = null,
    DreamStore? dreamStore = null,
    SubAgentWaitAgentTimeoutOptions? subAgentWaitAgentTimeoutOptions = null,
    string? originChannel = null,
    IReadOnlyList<string>? workspaceRoots = null,
    ILogger<PromptBuilder>? logger = null,
    IContributionView? contributions = null)
{
    private readonly ILogger<PromptBuilder> _logger = logger ?? NullLogger<PromptBuilder>.Instance;

    private readonly IContributionView _contributions = ResolveView(contributions);

    private readonly PromptSectionSources _sources = new()
    {
        MemoryStore = memoryStore,
        SkillsLoader = skillsLoader,
        CraftPath = Path.GetFullPath(craftPath),
        WorkspacePath = Path.GetFullPath(workspacePath),
        RawCraftPath = craftPath,
        RawWorkspacePath = workspacePath,
        WorkspaceRoots = workspaceRoots ?? [Path.GetFullPath(workspacePath)],
        CustomCommandLoader = customCommandLoader,
        SandboxEnabled = sandboxEnabled,
        DeferredMcpServerNames = deferredMcpServerNames,
        SubAgentProfilesSection = subAgentProfilesSection,
        SkillVariantModeEnabled = skillVariantModeEnabled,
        SkillVariantTarget = skillVariantTarget,
        RoleInstructions = roleInstructions,
        DeveloperInstructions = developerInstructions,
        ContextPageManager = contextPageManager,
        DreamStore = dreamStore,
        SubAgentWaitAgentTimeoutOptions = subAgentWaitAgentTimeoutOptions,
        // Must be the same view the section list is resolved from, or sections evaluate against foreign sources.
        Contributions = ResolveView(contributions),
        Logger = logger ?? NullLogger<PromptBuilder>.Instance
    };

    /// <summary>Resolves the view to build from, falling back to the built-in sections alone.</summary>
    private static IContributionView ResolveView(IContributionView? contributions) =>
        contributions ?? SystemPromptSectionCatalog.DefaultView;

    /// <summary>Builds the complete system prompt for one thread, or for an unbound build when <paramref name="threadId"/> is <see langword="null"/>.</summary>
    public string BuildSystemPrompt(string? threadId = null)
    {
        var context = new SystemPromptSectionContext(
            threadId,
            _sources.WorkspacePath,
            _sources.CraftPath,
            toolNamesProvider?.Invoke(),
            originChannel)
        {
            Sources = _sources
        };

        var prompt = SystemPromptComposition.Compose(
            _contributions.Resolve<ISystemPromptSection>(threadId),
            context,
            _logger);

        return SystemPromptComposition.ApplyAssembler(
            _contributions.Resolve<ISystemPromptAssembler>(threadId),
            prompt,
            context,
            _logger);
    }
}
