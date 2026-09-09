using System.Reflection;
using System.Text;
using DotCraft.Plugins;

namespace DotCraft.Skills;

public sealed partial class SkillsLoader
{
    private static readonly Lazy<HashSet<string>> CoreBuiltInSkillNames = new(() =>
    {
        var assembly = typeof(SkillsLoader).Assembly;
        return GetBuiltInResources(assembly)
            .Select(group => ReadBuiltInSkillName(assembly, group) ?? group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    });

    private readonly HashSet<string> _availableBuiltInSkills = new(CoreBuiltInSkillNames.Value, StringComparer.OrdinalIgnoreCase);

    private static IGrouping<string, (string SkillName, string FileName, string ResourceName)>[] GetBuiltInResources(Assembly assembly)
    {
        const string resourcePrefix = "DotCraft.Skills.BuiltIn.";
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(resourcePrefix, StringComparison.Ordinal))
            .Select(name =>
            {
                var remainder = name[resourcePrefix.Length..];
                var dotIndex = remainder.IndexOf('.');
                // Resources at the BuiltIn root (e.g. .gitkeep) have no skill prefix
                if (dotIndex <= 0)
                    return (SkillName: string.Empty, FileName: remainder, ResourceName: name);
                return (
                    SkillName: remainder[..dotIndex],
                    FileName: remainder[(dotIndex + 1)..],
                    ResourceName: name
                );
            })
            .Where(r => !string.IsNullOrEmpty(r.SkillName))
            .GroupBy(r => r.SkillName)
            .ToArray();
    }

    /// <summary>
    /// Deploy built-in skills (embedded in the assembly) to the workspace skills directory.
    /// Skips skills that were created by the user (no .builtin marker) and skills
    /// that are already up to date.
    /// </summary>
    /// <param name="resourceAssembly">
    /// The assembly that contains the embedded skill resources.
    /// Pass <c>typeof(Program).Assembly</c> (or equivalent) from the host application.
    /// Falls back to the assembly containing <see cref="SkillsLoader"/> when null.
    /// </param>
    public void DeployBuiltInSkills(Assembly? resourceAssembly = null)
    {
        const string markerFile = ".builtin";

        var assembly = resourceAssembly ?? typeof(SkillsLoader).Assembly;
        var currentVersion = PluginHostVersion.Current.ProductText;

        var resourcesBySkill = GetBuiltInResources(assembly);
        foreach (var group in resourcesBySkill)
            _availableBuiltInSkills.Add(ReadBuiltInSkillName(assembly, group) ?? group.Key);

        Directory.CreateDirectory(WorkspaceSkillsPath);

        foreach (var skillGroup in resourcesBySkill)
        {
            var skillName = ReadBuiltInSkillName(assembly, skillGroup) ?? skillGroup.Key;
            var skillDir = Path.Combine(WorkspaceSkillsPath, skillName);
            var markerPath = Path.Combine(skillDir, markerFile);

            // If the skill directory exists but has no .builtin marker, the user owns it
            if (Directory.Exists(skillDir) && !File.Exists(markerPath))
                continue;

            // If the skill is already at the current version, skip it
            if (File.Exists(markerPath) && File.ReadAllText(markerPath).Trim() == currentVersion)
                continue;

            Directory.CreateDirectory(skillDir);

            foreach (var resource in skillGroup)
            {
                using var stream = assembly.GetManifestResourceStream(resource.ResourceName);
                if (stream == null)
                    continue;

                var targetPath = Path.Combine(skillDir, NormalizeBuiltInResourceFileName(resource.FileName));
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                using var file = File.Create(targetPath);
                stream.CopyTo(file);
            }

            File.WriteAllText(markerPath, currentVersion);
        }
    }

    private static string? ReadBuiltInSkillName(
        Assembly assembly,
        IEnumerable<(string SkillName, string FileName, string ResourceName)> resources)
    {
        var skillResource = resources.FirstOrDefault(
            resource => string.Equals(resource.FileName, "SKILL.md", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(skillResource.ResourceName))
            return null;

        using var stream = assembly.GetManifestResourceStream(skillResource.ResourceName);
        if (stream == null)
            return null;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var content = reader.ReadToEnd();
        return ReadFrontmatterValue(content, "name");
    }

    private static string NormalizeBuiltInResourceFileName(string fileName)
    {
        if (fileName.StartsWith("agents.", StringComparison.Ordinal))
            return Path.Combine("agents", fileName["agents.".Length..]);
        if (fileName.StartsWith("assets.", StringComparison.Ordinal))
            return Path.Combine("assets", fileName["assets.".Length..]);
        if (fileName.StartsWith("scripts.", StringComparison.Ordinal))
            return Path.Combine("scripts", fileName["scripts.".Length..]);
        if (fileName.StartsWith("references.", StringComparison.Ordinal))
            return Path.Combine("references", fileName["references.".Length..]);
        return fileName;
    }

}
