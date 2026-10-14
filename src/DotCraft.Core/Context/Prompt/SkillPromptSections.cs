using DotCraft.Contributions;
using DotCraft.Sessions;
using DotCraft.Skills;
using System.Text;

namespace DotCraft.Context;

/// <summary>The built-in skill sections: self-learning guidance, the inlined always-loaded skills, and how to choose and load the rest. The catalog itself is the <c>skills</c> world-state section.</summary>
internal static class SkillPromptSections
{
    /// <summary>Builds the <c>self-learning</c> section, or omits it without <c>SkillManage</c>.</summary>
    internal static string? SelfLearning(SystemPromptSectionContext context) =>
        context.IsToolAvailable("SkillManage")
            ?
"""
## Skill Self-Learning

Use `SkillManage` to maintain workspace skills: reusable instructions for recurring tasks.

Capture verified, reusable procedures learned from tasks, troubleshooting, or user corrections. Apply explicit requests to remember a procedure. Do not create skills for simple one-off answers.

Correct outdated or incomplete instructions in a loaded skill before finishing. Use `SkillManage(action: "patch")` for small corrections. Before a major rewrite with `edit`, read the current skill with `SkillView`.

Update an existing skill when it covers the task. Scope new skills to a reusable task type.

Skill changes reach the skills list on your next step.
"""
            : null;

    /// <summary>Builds the <c>active-skills</c> section from the always-loaded skills.</summary>
    internal static string? ActiveSkills(SystemPromptSectionContext context)
    {
        var sources = context.RequireSources();
        var content = sources.GetContextPage(
            context.ThreadId,
            ContextPageKeys.SkillsAlways(BuildSkillsVariant(sources)),
            () =>
            {
                var alwaysSkills = sources.SkillsLoader.GetAlwaysSkills()
                    .Where(name => sources.SkillsPolicy?.Permits(name) != false)
                    .ToList();
                return alwaysSkills.Count == 0
                    ? string.Empty
                    : sources.SkillsLoader.LoadSkillsForContext(
                        alwaysSkills,
                        sources.SkillVariantModeEnabled,
                        sources.SkillVariantTarget);
            });

        return string.IsNullOrWhiteSpace(content) ? null : $"# Active Skills\n\n{content}";
    }

    /// <summary>Builds the <c>skills-summary</c> section: how to choose and load a skill from the catalog.</summary>
    internal static string? SkillsSummary(SystemPromptSectionContext context)
    {
        var skillLoadInstruction = context.IsToolAvailable("SkillView")
            ? "If the user names a skill or the task clearly matches its description, load it with SkillView and follow its instructions. Use ReadFile for supporting files referenced by the loaded skill."
            : "If the user names a skill or the task clearly matches its description, read its SKILL.md and follow its instructions.";

        return
$"""
# Skills

{skillLoadInstruction}

Use the minimal set of matching skills. Reuse a skill across turns only when it is named again or the current task still matches it.

Active skills shown above are already loaded. Follow them without loading them again.

The skills you can use are listed in the Skills context section. When the list is restated, the newest one applies.
""";
    }

    /// <summary>Builds the cache variant key pinning the always-loaded page to the workspace, skill root, variant selection, and skill policy.</summary>
    private static string BuildSkillsVariant(PromptSectionSources sources)
    {
        var sb = new StringBuilder();
        sb.Append("workspace:");
        sb.Append(sources.WorkspacePath);
        sb.Append("|skills:");
        sb.Append(sources.SkillsLoader.WorkspaceSkillsPath);
        sb.Append("|variantMode:");
        sb.Append(sources.SkillVariantModeEnabled.ToString().ToLowerInvariant());
        sb.Append("|target:");
        AppendSkillVariantTarget(sb, sources.SkillVariantTarget);
        sb.Append("|policy:");
        AppendSkillsPolicy(sb, sources.SkillsPolicy);
        return sb.ToString();
    }

    private static void AppendSkillsPolicy(StringBuilder sb, ThreadSkillsPolicy? policy)
    {
        sb.Append("allow=");
        sb.Append(policy?.Allow == null ? "*" : string.Join(',', policy.Allow));
        sb.Append(";deny=");
        sb.Append(policy?.Deny == null ? string.Empty : string.Join(',', policy.Deny));
    }

    private static void AppendSkillVariantTarget(StringBuilder sb, SkillVariantTarget? target)
    {
        if (target == null)
        {
            sb.Append("none");
            return;
        }

        sb.Append(target.Harness);
        sb.Append('|');
        sb.Append(target.HarnessVersion);
        sb.Append('|');
        sb.Append(target.Model);
        sb.Append('|');
        sb.Append(target.Os);
        sb.Append('|');
        sb.Append(target.Shell);
        sb.Append('|');
        sb.Append(target.ToolProfileHash);
        sb.Append('|');
        sb.Append(target.ApprovalPolicy);
        sb.Append('|');
        sb.Append(target.WorkspaceHash);
    }
}
