using DotCraft.Agents;
using DotCraft.Skills;

namespace DotCraft.Sessions;

/// <summary>The loader and effective-location target a thread's skill catalog is read with.</summary>
internal sealed record SkillCatalogSource(SkillsLoader Loader, bool VariantModeEnabled, SkillVariantTarget VariantTarget)
{
    public static SkillCatalogSource For(AgentRuntimeContext context) =>
        new(
            context.SkillsLoader,
            string.Equals(context.Config.Skills.SelfLearning.VariantMode, "enabled", StringComparison.OrdinalIgnoreCase),
            SkillVariantStore.CreateTarget(
                context.EffectiveMainModel,
                context.WorkspacePath,
                context.Config.Permissions.DefaultApprovalPolicy.ToString(),
                toolNames: null));
}

internal sealed record SkillCatalogRead(
    string TurnId,
    SkillsLoader Loader,
    long Revision,
    IReadOnlyList<SkillCatalogEntry> Entries);

public sealed partial class SessionService
{
    /// <summary>Reads the catalog once per turn, and again only after the loader reports a change.</summary>
    private IReadOnlyList<SkillCatalogEntry> ReadSkillCatalog(SessionThread thread, string turnId)
    {
        _runtimeRegistry.TryGetRuntime(thread.Id, out var runtime);
        var source = runtime?.SkillCatalogSource ?? SkillCatalogSource.For(agentFactory.RuntimeContext);
        var revision = source.Loader.Revision;
        if (runtime?.SkillCatalog is { } previous
            && previous.TurnId == turnId
            && previous.Revision == revision
            && ReferenceEquals(previous.Loader, source.Loader))
        {
            return previous.Entries;
        }

        var entries = source.Loader.ListCatalog(
            source.VariantModeEnabled,
            source.VariantTarget,
            thread.Configuration?.SkillsPolicy);
        if (runtime is not null)
            runtime.SkillCatalog = new SkillCatalogRead(turnId, source.Loader, revision, entries);
        return entries;
    }

    private Func<IReadOnlyList<SkillCatalogEntry>>? ResolveSkillCatalog(SessionThread thread, string turnId)
    {
        // A Thread running on caller-supplied instructions has no skill sections, so it gets no catalog either.
        if (thread.Configuration is { UseToolProfileOnly: true } config
            && !string.IsNullOrWhiteSpace(config.AgentInstructions))
        {
            return null;
        }

        return () => ReadSkillCatalog(thread, turnId);
    }
}
