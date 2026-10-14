using DotCraft.Sessions;

namespace DotCraft.Skills;

/// <summary>One skill as the agent's catalog lists it.</summary>
public sealed record SkillCatalogEntry(string Name, string Description, string Location, bool AlwaysLoaded);

public sealed partial class SkillsLoader
{
    private long _revision;

    /// <summary>Advances whenever this loader learns that skills changed, so a catalog read at an older value is stale.</summary>
    public long Revision => Interlocked.Read(ref _revision);

    /// <summary>Lists the enabled skills the policy permits, in name order, with their effective locations.</summary>
    public IReadOnlyList<SkillCatalogEntry> ListCatalog(
        bool variantModeEnabled,
        SkillVariantTarget? target,
        ThreadSkillsPolicy? policy = null)
    {
        var entries = new List<SkillCatalogEntry>();
        foreach (var skill in ListSkills())
        {
            if (!skill.Enabled || policy?.Permits(skill.Name) == false)
                continue;

            var metadata = GetSkillMetadataFromFile(skill.Path);
            entries.Add(new SkillCatalogEntry(
                skill.Name,
                metadata?.GetValueOrDefault("description", skill.Name) ?? skill.Name,
                ResolveEffectiveSkillFile(skill, variantModeEnabled, target) ?? skill.Path,
                IsAlwaysLoaded(metadata)));
        }

        return entries;
    }

    private void MarkChanged() => Interlocked.Increment(ref _revision);

    private static bool IsAlwaysLoaded(Dictionary<string, string>? metadata) =>
        string.Equals(metadata?.GetValueOrDefault("always"), "true", StringComparison.OrdinalIgnoreCase);
}
