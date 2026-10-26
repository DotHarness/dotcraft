using DotCraft.Skills;

namespace DotCraft.Sessions;

internal sealed record SkillCatalogRead(
    SkillsLoader Loader,
    long Revision,
    IReadOnlyList<SkillCatalogEntry> Entries);

public sealed partial class SessionService : ISkillReloadService
{
    public void ReloadSkills()
    {
        agentFactory.RuntimeContext.SkillsLoader.RefreshDescriptors();
        foreach (var runtime in _runtimeRegistry.Values)
            runtime.SkillCatalogLoader?.RefreshDescriptors();
    }

    /// <summary>Reads the catalog once, and again only after the loader reports a change (Skills §4.1).</summary>
    private IReadOnlyList<SkillCatalogEntry> ReadSkillCatalog(SessionThread thread)
    {
        _runtimeRegistry.TryGetRuntime(thread.Id, out var runtime);
        var loader = runtime?.SkillCatalogLoader ?? agentFactory.RuntimeContext.SkillsLoader;
        var revision = loader.Revision;
        if (runtime?.SkillCatalog is { } previous
            && previous.Revision == revision
            && ReferenceEquals(previous.Loader, loader))
        {
            return previous.Entries;
        }

        var entries = loader.ListCatalog(thread.Configuration?.SkillsPolicy);
        if (runtime is not null)
            runtime.SkillCatalog = new SkillCatalogRead(loader, revision, entries);
        return entries;
    }

    private Func<IReadOnlyList<SkillCatalogEntry>>? ResolveSkillCatalog(SessionThread thread)
    {
        // A Thread running on caller-supplied instructions has no skill sections, so it gets no catalog either.
        if (thread.Configuration is { UseToolProfileOnly: true } config
            && !string.IsNullOrWhiteSpace(config.AgentInstructions))
        {
            return null;
        }

        return () => ReadSkillCatalog(thread);
    }
}
