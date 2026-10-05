using System.Text.Json.Nodes;

namespace DotCraft.Configuration;

/// <summary>
/// Persists <see cref="AppConfig.SkillsConfig"/> fields to the workspace <c>.craft/config.json</c>.
/// </summary>
public static class SkillsConfigPersistence
{
    /// <summary>
    /// Writes <c>Skills.DisabledSkills</c> to the workspace config file, merging with existing JSON.
    /// </summary>
    /// <param name="craftPath">Absolute path to the <c>.craft</c> directory.</param>
    /// <param name="disabledSkills">Skill names to persist as disabled.</param>
    public static void WriteWorkspaceDisabledSkills(string craftPath, IReadOnlyList<string> disabledSkills)
    {
        AtomicConfigDocument.Update(Path.Combine(craftPath, "config.json"), root =>
        {
            var skills = AtomicConfigDocument.Object(root, "Skills");
            skills[AtomicConfigDocument.Key(skills, "DisabledSkills") ?? "DisabledSkills"] =
                new JsonArray(disabledSkills.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray());
        });
    }
}
