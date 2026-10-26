using System.ComponentModel;
using DotCraft.Tools;
using DotCraft.Tracing;

namespace DotCraft.Skills;

/// <summary>
/// Agent-facing tool for loading a skill's instructions.
/// </summary>
public sealed class SkillViewTool(
    SkillsLoader skillsLoader,
    TraceCollector? traceCollector = null)
{
    private const string SkillViewDescription = "Load the instructions for a listed skill.";

    /// <summary>
    /// Loads a skill's <c>SKILL.md</c> body.
    /// </summary>
    [GeneratedTool]
    [ToolPresentation("core.skill-view")]
    [Description(SkillViewDescription)]
    public string SkillView(
        [Description("Skill name, for example 'browser' or 'skill-authoring'.")]
        string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Skill name is required.";

        var skill = skillsLoader.LoadSkillBody(name.Trim());
        if (skill == null)
            return $"Skill '{name}' not found.";

        // An agent loading a skill counts as a skill use for the Profile metrics (spec §27A.5),
        // alongside user-typed `$name` references. Keyed by the trace session (the active thread).
        var sessionKey = TracingChatClient.CurrentSessionKey ?? TracingChatClient.GetActiveSessionKey();
        if (!string.IsNullOrEmpty(sessionKey))
            traceCollector?.RecordSkillReferenced(
                sessionKey!,
                skill.Name,
                ToolUsageSource.FromSkill(skill.Source, skill.PluginId));

        return skill.Content;
    }
}
