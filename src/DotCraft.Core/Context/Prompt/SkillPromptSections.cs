using DotCraft.Contributions;
using DotCraft.Skills;
using System.Text;

namespace DotCraft.Context;

/// <summary>The built-in skill sections: self-learning guidance, the inlined always-loaded skills, and the progressive-loading summary of the rest.</summary>
internal static class SkillPromptSections
{
    /// <summary>Builds the <c>self-learning</c> section, or omits it without <c>SkillManage</c>.</summary>
    internal static string? SelfLearning(SystemPromptSectionContext context) =>
        context.IsToolAvailable("SkillManage")
            ?
"""
## Skill Self-Learning

You can create and maintain workspace skills with `SkillManage`. Skills are procedural memory: reusable, narrow instructions for task types that are likely to recur.

Create or update a skill after a complex task succeeds, especially after about 5+ tool calls, iterative troubleshooting, a tricky error fix, a user-corrected workflow, or an explicit request to remember a procedure. Do not create skills for simple one-off answers.

When you load a skill and find it stale, incomplete, wrong, using incorrect commands, or missing a pitfall discovered during the task, patch it before finishing with `SkillManage(action: "patch")`. Prefer `patch` for small corrections. For major rewrites, load the current skill with `SkillView` first and then use `edit`.

Prefer updating or generalizing an existing skill over creating a new one when the existing skill already covers the task class. Create new skills at the reusable task-class level, not for one exact session.

Newly created or updated skills may not affect the current prompt immediately; they are available after the next turn or session refresh.
"""
            : null;

    /// <summary>Builds the <c>active-skills</c> section from the always-loaded skills.</summary>
    internal static string? ActiveSkills(SystemPromptSectionContext context)
    {
        var sources = context.RequireSources();
        var toolNames = context.AvailableToolNames;
        var content = sources.GetContextPage(
            context.ThreadId,
            ContextPageKeys.SkillsAlways(BuildSkillsVariant(sources, toolNames)),
            () =>
            {
                var alwaysSkills = sources.SkillsLoader.GetAlwaysSkills(toolNames);
                return alwaysSkills.Count == 0
                    ? string.Empty
                    : sources.SkillsLoader.LoadSkillsForContext(
                        alwaysSkills,
                        sources.SkillVariantModeEnabled,
                        sources.SkillVariantTarget);
            });

        return string.IsNullOrWhiteSpace(content) ? null : $"# Active Skills\n\n{content}";
    }

    /// <summary>Builds the <c>skills-summary</c> section listing the skills available on demand.</summary>
    internal static string? SkillsSummary(SystemPromptSectionContext context)
    {
        var sources = context.RequireSources();
        var toolNames = context.AvailableToolNames;
        var summary = sources.GetContextPage(
            context.ThreadId,
            ContextPageKeys.SkillsSummary(BuildSkillsVariant(sources, toolNames)),
            () => sources.SkillsLoader.BuildSkillsSummary(
                toolNames,
                sources.SkillVariantModeEnabled,
                sources.SkillVariantTarget));

        if (string.IsNullOrWhiteSpace(summary))
            return null;

        var skillLoadInstruction = context.IsToolAvailable("SkillView")
            ? "Before replying, scan the available skills below. If the user names a skill or the current task clearly matches a skill's description, load that skill with the SkillView tool and follow its instructions. Use ReadFile only when SkillView is unavailable or when you need to inspect a specific physical supporting file referenced by the loaded skill."
            : "Before replying, scan the available skills below. If the user names a skill or the current task clearly matches a skill's description, read that skill's SKILL.md and follow its instructions.";

        return
$"""
# Skills

{skillLoadInstruction}

Skills encode project workflows, pitfalls, user preferences, and quality standards that may outperform a general-purpose approach. Use the minimal set of matching skills. Do not carry skill choices across turns unless the skill is re-mentioned or the new task clearly matches it.

Active skills shown above are already loaded; follow their instructions directly instead of calling SkillView for them again.

{summary}
""";
    }

    /// <summary>Builds the cache variant key pinning both skill pages to the workspace, skill root, variant selection, and exposed tool set.</summary>
    private static string BuildSkillsVariant(
        PromptSectionSources sources,
        IReadOnlyList<string>? availableToolNames)
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
        sb.Append("|tools:");
        if (availableToolNames is { Count: > 0 })
            sb.Append(string.Join(",", availableToolNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)));
        return sb.ToString();
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
        sb.Append(target.Sandbox);
        sb.Append('|');
        sb.Append(target.ToolProfileHash);
        sb.Append('|');
        sb.Append(target.ApprovalPolicy);
        sb.Append('|');
        sb.Append(target.WorkspaceHash);
    }
}
