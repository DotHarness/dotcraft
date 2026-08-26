using DotCraft.Contributions;

namespace DotCraft.Context;

/// <summary>Registers DotCraft's own system prompt segments as ordinary built-in contributions.</summary>
internal static class SystemPromptSectionCatalog
{
    private static readonly Lazy<IContributionView> LazyDefaultView = new(CreateDefaultView, isThreadSafe: true);

    /// <summary>Gets the immutable process-wide view containing only the built-in sections.</summary>
    public static IContributionView DefaultView => LazyDefaultView.Value;

    /// <summary>Registers every built-in system prompt section into a registry.</summary>
    /// <param name="registrar">Optional origin-scoped owner for the handles; when omitted the sections are attributed to <see cref="ContributionOrigin.Builtin"/> and live for the registry's lifetime.</param>
    /// <returns>The handles in prompt order.</returns>
    internal static IReadOnlyList<IContributionHandle> RegisterBuiltIns(
        IContributionRegistry registry,
        IContributionRegistrar? registrar = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        using var batch = registry.BeginBatch();
        var handles = new List<IContributionHandle>(Definitions.Count);
        foreach (var (name, order, content) in Definitions)
        {
            var options = new ContributionOptions(Order: order) { TargetName = name };
            var section = new BuiltInPromptSection(name, content);
            handles.Add(registrar is null
                ? registry.Add<ISystemPromptSection>(section, options)
                : registrar.Add<ISystemPromptSection>(section, options));
        }

        return handles;
    }

    /// <summary>The built-in sections in prompt order, with their Tier-B target names.</summary>
    private static IReadOnlyList<(string Name, int Order, Func<SystemPromptSectionContext, string?> Content)>
        Definitions { get; } =
    [
        (SystemPromptSectionNames.Identity, 100, IdentityPromptSection.Build),
        (SystemPromptSectionNames.SubAgentProfiles, 200, SubAgentPromptSections.Profiles),
        (SystemPromptSectionNames.SubAgentLifecycle, 300, SubAgentPromptSections.Lifecycle),
        (SystemPromptSectionNames.ProjectInstructions, 350, static _ => GuidancePromptSections.ProjectInstructions),
        (SystemPromptSectionNames.WorkingStyle, 400, static _ => GuidancePromptSections.WorkingStyle),
        (SystemPromptSectionNames.ResponseStyle, 500, static _ => GuidancePromptSections.ResponseStyle),
        (SystemPromptSectionNames.EditingWorkflow, 600, static _ => GuidancePromptSections.EditingWorkflow),
        (SystemPromptSectionNames.FileReferences, 700, static _ => GuidancePromptSections.FileReferences),
        (SystemPromptSectionNames.ModeProtocol, 800, static _ => GuidancePromptSections.ModeProtocol),
        (SystemPromptSectionNames.RequestUserInput, 900, static context =>
            context.IsToolAvailable("RequestUserInput") ? GuidancePromptSections.RequestUserInput : null),
        (SystemPromptSectionNames.BootstrapFiles, 1000, WorkspaceContextPromptSections.Bootstrap),
        (SystemPromptSectionNames.Memory, 1100, WorkspaceContextPromptSections.Memory),
        (SystemPromptSectionNames.SelfLearning, 1200, SkillPromptSections.SelfLearning),
        (SystemPromptSectionNames.ActiveSkills, 1300, SkillPromptSections.ActiveSkills),
        (SystemPromptSectionNames.SkillsSummary, 1400, SkillPromptSections.SkillsSummary),
        (SystemPromptSectionNames.CustomCommands, 1500, WorkspaceContextPromptSections.CustomCommands),
        (SystemPromptSectionNames.ChatContext, 1600, ProviderPromptSections.ChatContext),
        (SystemPromptSectionNames.ThreadContext, 1700, ProviderPromptSections.ThreadContext),
        (SystemPromptSectionNames.DeferredTools, 1800, ProviderPromptSections.DeferredTools),
        (SystemPromptSectionNames.RoleInstructions, 1900, ProviderPromptSections.RoleInstructions)
    ];

    private static IContributionView CreateDefaultView()
    {
        var registry = new ContributionRegistry();
        RegisterBuiltIns(registry);
        return registry;
    }
}
