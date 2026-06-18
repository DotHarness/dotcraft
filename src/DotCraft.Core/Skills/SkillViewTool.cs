using System.ComponentModel;
using DotCraft.Tools;
using DotCraft.Tracing;

namespace DotCraft.Skills;

/// <summary>
/// Agent-facing tool for loading the effective source-or-variant skill body.
/// </summary>
public sealed class SkillViewTool(
    SkillsLoader skillsLoader,
    bool variantModeEnabled,
    SkillVariantTarget target,
    TraceCollector? traceCollector = null)
{
    private const string SkillViewDescription =
        """
        Load the effective instructions for a skill by name. Use this instead of ReadFile
        when a listed skill is relevant to the task. The result is only the SKILL.md body
        the agent should follow.
        """;

    /// <summary>
    /// Loads a skill's effective <c>SKILL.md</c> body.
    /// </summary>
    [GeneratedTool]
    [Description(SkillViewDescription)]
    public string SkillView(
        [Description("Skill name, for example 'browser' or 'skill-authoring'.")]
        string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Skill name is required.";

        var effective = skillsLoader.LoadEffectiveSkill(name.Trim(), variantModeEnabled, target);
        if (effective == null)
            return $"Skill '{name}' not found.";

        // An agent loading a skill counts as a skill use for the Profile metrics (spec §27A.5),
        // alongside user-typed `$name` references. Keyed by the trace session (the active thread).
        var sessionKey = TracingChatClient.CurrentSessionKey ?? TracingChatClient.GetActiveSessionKey();
        if (!string.IsNullOrEmpty(sessionKey))
            traceCollector?.RecordSkillReferenced(sessionKey!, effective.Name);

        return effective.Content;
    }
}
