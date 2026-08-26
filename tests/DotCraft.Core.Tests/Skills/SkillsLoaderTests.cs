using DotCraft.Plugins;
using DotCraft.Skills;
using Xunit;

namespace DotCraft.Tests.Skills;

public sealed class SkillsLoaderTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "dotcraft-skillsloader-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void GetSkillInterface_ReadsOpenAiManifestAndRejectsEscapingIcons()
    {
        Directory.CreateDirectory(_tempRoot);
        var loader = new SkillsLoader(_tempRoot);
        var skillDir = Path.Combine(loader.WorkspaceSkillsPath, "demo-skill");
        Directory.CreateDirectory(Path.Combine(skillDir, "agents"));
        Directory.CreateDirectory(Path.Combine(skillDir, "assets"));
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), "---\nname: demo-skill\ndescription: Demo\n---\n# Demo");
        File.WriteAllText(Path.Combine(skillDir, "agents", "openai.yaml"), """
            interface:
              display_name: "Demo Skill"
              short_description: "Short demo"
              icon_small: "./assets/demo.svg"
              icon_large: "../secret.svg"
              default_prompt: "Use $demo-skill."
            """);
        File.WriteAllText(Path.Combine(skillDir, "assets", "demo.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\" />");
        File.WriteAllText(Path.Combine(loader.WorkspaceSkillsPath, "secret.svg"), "<svg />");

        var info = loader.GetSkillInterface("demo-skill");

        Assert.NotNull(info);
        Assert.Equal("Demo Skill", info.DisplayName);
        Assert.Equal("Short demo", info.ShortDescription);
        Assert.StartsWith("data:image/svg+xml;base64,", info.IconSmallDataUrl);
        Assert.Null(info.IconLargeDataUrl);
        Assert.Equal("Use $demo-skill.", info.DefaultPrompt);
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
}
