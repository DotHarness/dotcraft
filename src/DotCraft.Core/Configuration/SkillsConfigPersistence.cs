using System.Text.Json;
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
        var path = Path.Combine(craftPath, "config.json");
        JsonObject root;
        if (File.Exists(path))
        {
            var text = File.ReadAllText(path);
            root = (JsonObject)(JsonNode.Parse(text) ?? new JsonObject());
        }
        else
        {
            root = new JsonObject();
        }

        var skillsObj = root["Skills"] as JsonObject ?? new JsonObject();
        var arr = new JsonArray();
        foreach (var s in disabledSkills)
            arr.Add(s);
        skillsObj["DisabledSkills"] = arr;
        root["Skills"] = skillsObj;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
