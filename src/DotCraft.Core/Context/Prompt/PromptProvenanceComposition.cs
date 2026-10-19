using DotCraft.Agents;
using DotCraft.Contributions;
using Microsoft.Extensions.Logging;

namespace DotCraft.Context;

internal static class PromptProvenanceComposition
{
    public static (string Text, IReadOnlyList<ProviderPromptFragment> Fragments) Compose(
        IReadOnlyList<ContributionEntry<ISystemPromptSection>> entries, SystemPromptSectionContext context, ILogger logger)
    {
        var fragments = new List<ProviderPromptFragment>();
        foreach (var entry in entries)
        {
            try
            {
                var text = entry.Contribution.GetContent(context);
                if (string.IsNullOrWhiteSpace(text)) continue;
                string name;
                try { name = entry.Contribution.Name; }
                catch { name = entry.Id.ToString(); }
                var prefix = fragments.Count > 0 ? SystemPromptComposition.SectionSeparator : string.Empty;
                var kind = entry.Origin.Kind == ContributionOriginKind.Builtin ? BuiltinKind(name) : ProviderPromptSourceKind.Extension;
                fragments.Add(new ProviderPromptFragment(entry.Origin + "/" + name, kind, prefix + text, "instructions"));
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "A prompt section failed and was omitted.");
            }
        }
        return (string.Concat(fragments.Select(fragment => fragment.Text)), fragments);
    }

    private static ProviderPromptSourceKind BuiltinKind(string name) => name switch
    {
        SystemPromptSectionNames.RoleInstructions or SystemPromptSectionNames.DeveloperInstructions
            or SystemPromptSectionNames.BootstrapFiles => ProviderPromptSourceKind.CallerInstructions,
        SystemPromptSectionNames.ActiveSkills or SystemPromptSectionNames.SkillsSummary
            or SystemPromptSectionNames.SelfLearning => ProviderPromptSourceKind.Skill,
        SystemPromptSectionNames.Memory => ProviderPromptSourceKind.Memory,
        SystemPromptSectionNames.ChatContext or SystemPromptSectionNames.ThreadContext => ProviderPromptSourceKind.Extension,
        _ => ProviderPromptSourceKind.Harness
    };
}
