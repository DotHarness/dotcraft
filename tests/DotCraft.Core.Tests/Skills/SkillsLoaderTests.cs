using DotCraft.Plugins;
using DotCraft.Skills;
using Xunit;

namespace DotCraft.Tests.Skills;

public sealed class SkillsLoaderTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "dotcraft-skillsloader-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void GetSkillInterface_LocalOnlyReadsOwnAssetsAndRejectsOtherDirectories()
    {
        Directory.CreateDirectory(_tempRoot);
        var loader = new SkillsLoader(_tempRoot);
        var skillDir = Path.Combine(loader.WorkspaceSkillsPath, "demo-skill");
        Directory.CreateDirectory(Path.Combine(skillDir, "agents"));
        Directory.CreateDirectory(Path.Combine(skillDir, "assets"));
        Directory.CreateDirectory(Path.Combine(skillDir, "other"));
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), "---\nname: demo-skill\ndescription: Demo\n---\n# Demo");
        File.WriteAllText(Path.Combine(skillDir, "agents", "openai.yaml"), """
            interface:
              display_name: "Demo Skill"
              short_description: "Short demo"
              icon_small: "./assets/demo.svg"
              icon_large: "./other/secret.svg"
              default_prompt: "Use $demo-skill."
            """);
        File.WriteAllText(Path.Combine(skillDir, "assets", "demo.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\" />");
        File.WriteAllText(Path.Combine(skillDir, "other", "secret.svg"), "<svg />");

        var info = loader.GetSkillInterface("demo-skill");

        Assert.NotNull(info);
        Assert.Equal("Demo Skill", info.DisplayName);
        Assert.Equal("Short demo", info.ShortDescription);
        Assert.StartsWith("data:image/svg+xml;base64,", info.IconSmallDataUrl);
        Assert.Null(info.IconLargeDataUrl);
        Assert.Equal("Use $demo-skill.", info.DefaultPrompt);
    }

    [Fact]
    public void GetSkillInterface_LocalOnlyRejectsParentAndAbsolutePaths()
    {
        var loader = new SkillsLoader(_tempRoot);
        var skillDir = WriteSkill(loader.WorkspaceSkillsPath, "demo-skill");
        var sharedAssets = Path.Combine(_tempRoot, "assets");
        Directory.CreateDirectory(sharedAssets);
        var sharedIcon = Path.Combine(sharedAssets, "shared.svg");
        File.WriteAllText(sharedIcon, "<svg />");

        foreach (var iconPath in new[] { "../../assets/shared.svg", sharedIcon.Replace('\\', '/') })
        {
            WriteInterface(skillDir, iconPath);

            var info = loader.GetSkillInterface("demo-skill");

            Assert.NotNull(info);
            Assert.Equal("Demo Skill", info.DisplayName);
            Assert.Null(info.IconSmallDataUrl);
        }
    }

    [Fact]
    public void GetSkillInterface_PluginSharedReadsLocalAndPluginAssets()
    {
        var loader = new SkillsLoader(_tempRoot);
        var pluginRoot = Path.Combine(_tempRoot, "plugin");
        var skillsPath = Path.Combine(pluginRoot, "skills");
        var skillDir = WriteSkill(skillsPath, "demo-skill");
        Directory.CreateDirectory(Path.Combine(skillDir, "assets"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "assets"));
        File.WriteAllText(Path.Combine(skillDir, "assets", "local.svg"), "<svg />");
        File.WriteAllText(Path.Combine(pluginRoot, "assets", "shared.svg"), "<svg />");
        File.WriteAllText(Path.Combine(skillDir, "agents", "openai.yaml"), """
            interface:
              display_name: "Demo Skill"
              icon_small: "./assets/local.svg"
              icon_large: "../../assets/shared.svg"
            """);
        loader.SetPluginSkillSources([
            new SkillsLoader.PluginSkillSource("demo-plugin", "Demo Plugin", skillsPath, pluginRoot)
        ]);

        var info = loader.GetSkillInterface("demo-skill");

        Assert.NotNull(info);
        Assert.StartsWith("data:image/svg+xml;base64,", info.IconSmallDataUrl);
        Assert.StartsWith("data:image/svg+xml;base64,", info.IconLargeDataUrl);
    }

    [Fact]
    public void GetPluginSkillInterfaceFromFile_RejectsPathsOutsideSharedAssets()
    {
        var pluginRoot = Path.Combine(_tempRoot, "plugin");
        var skillDir = WriteSkill(Path.Combine(pluginRoot, "skills"), "demo-skill");
        var otherDir = Path.Combine(pluginRoot, "other");
        Directory.CreateDirectory(otherDir);
        File.WriteAllText(Path.Combine(otherDir, "other.svg"), "<svg />");
        var outsideIcon = Path.Combine(_tempRoot, "outside.svg");
        File.WriteAllText(outsideIcon, "<svg />");

        foreach (var iconPath in new[]
                 {
                     "../../other/other.svg",
                     "../../../outside.svg",
                     outsideIcon.Replace('\\', '/')
                 })
        {
            WriteInterface(skillDir, iconPath);

            var info = SkillsLoader.GetPluginSkillInterfaceFromFile(
                Path.Combine(skillDir, "SKILL.md"),
                pluginRoot);

            Assert.NotNull(info);
            Assert.Equal("Demo Skill", info.DisplayName);
            Assert.Null(info.IconSmallDataUrl);
        }
    }

    [Fact]
    public void DeployBuiltInSkills_RemovesLegacyUnderscoreBuiltIns()
    {
        Directory.CreateDirectory(_tempRoot);
        var loader = new SkillsLoader(_tempRoot);
        var legacyDir = Path.Combine(loader.WorkspaceSkillsPath, "skill_authoring");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, "SKILL.md"), "legacy");
        File.WriteAllText(Path.Combine(legacyDir, ".builtin"), "0.0.0.0");

        loader.DeployBuiltInSkills();

        Assert.False(Directory.Exists(legacyDir));
        Assert.True(Directory.Exists(Path.Combine(loader.WorkspaceSkillsPath, "skill-authoring")));
    }

    [Fact]
    public void DeployBuiltInSkills_WritesCanonicalProductVersion()
    {
        Directory.CreateDirectory(_tempRoot);
        var loader = new SkillsLoader(_tempRoot);

        loader.DeployBuiltInSkills();

        Assert.Equal(
            PluginHostVersion.Current.ProductText,
            File.ReadAllText(Path.Combine(
                loader.WorkspaceSkillsPath,
                "plugin-creator",
                ".builtin")));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp test directories.
        }
    }

    private static string WriteSkill(string skillsPath, string name)
    {
        var skillDir = Path.Combine(skillsPath, name);
        Directory.CreateDirectory(Path.Combine(skillDir, "agents"));
        File.WriteAllText(
            Path.Combine(skillDir, "SKILL.md"),
            $"---\nname: {name}\ndescription: Demo\n---\n# Demo");
        return skillDir;
    }

    private static void WriteInterface(string skillDir, string iconPath)
    {
        File.WriteAllText(Path.Combine(skillDir, "agents", "openai.yaml"), $$"""
            interface:
              display_name: "Demo Skill"
              icon_small: "{{iconPath}}"
            """);
    }
}
