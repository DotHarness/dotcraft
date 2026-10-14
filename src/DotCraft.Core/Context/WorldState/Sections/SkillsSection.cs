using System.Security;
using System.Text;
using System.Text.Json.Nodes;
using DotCraft.Skills;

namespace DotCraft.Context.WorldState;

/// <summary>A change restates the whole catalog, which replaces the one shown before it.</summary>
internal sealed class SkillsSection : IWorldStateSection
{
    public const string SectionId = "skills";

    private const string Heading = "## Skills";

    public string Id => SectionId;

    public JsonNode? Snapshot(WorldStateContext context)
    {
        if (context.Skills is not { } read)
            return null;

        var entries = new JsonObject();
        foreach (var skill in read())
            entries[skill.Name] = WorldStateHash.Of($"{skill.Description}\n{skill.Location}\n{skill.AlwaysLoaded}");

        // Keep an object even when nothing is listed, so an empty catalog stays distinct from an absent one.
        return new JsonObject { ["skills"] = entries };
    }

    public string? RenderDiff(WorldStateContext context, PreviousSectionState previous)
    {
        if (context.Skills is not { } read)
            return null;

        var skills = read();
        var previouslyListed = previous.Kind switch
        {
            PreviousSectionKind.Known => previous.Value?["skills"] is JsonObject { Count: > 0 },
            PreviousSectionKind.Unknown => true,
            _ => false
        };
        if (skills.Count == 0)
            return previouslyListed ? $"{Heading}\nNo skills are available now." : null;

        var text = new StringBuilder(Heading).Append('\n');
        if (previouslyListed)
            text.Append("This list replaces any skills list shown earlier.\n\n");

        text.Append("<skills>\n");
        foreach (var skill in skills)
        {
            text.Append($"  <skill always=\"{(skill.AlwaysLoaded ? "true" : "false")}\">\n");
            text.Append($"    <name>{SecurityElement.Escape(skill.Name)}</name>\n");
            text.Append($"    <description>{SecurityElement.Escape(skill.Description)}</description>\n");
            text.Append($"    <location>{SecurityElement.Escape(skill.Location)}</location>\n");
            text.Append("  </skill>\n");
        }

        return text.Append("</skills>").ToString();
    }
}
