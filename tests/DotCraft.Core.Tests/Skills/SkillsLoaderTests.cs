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

    [Fact]
    public void DeployBuiltInSkills_IncludesAutomations()
    {
        Directory.CreateDirectory(_tempRoot);
        var loader = new SkillsLoader(_tempRoot);

        loader.DeployBuiltInSkills();

        var skill = loader.ResolveSkillInfo("automations");
        Assert.NotNull(skill);
        Assert.Equal("builtin", skill.Source);
        Assert.Contains("Automation", loader.LoadSkill("automations"));
    }

    [Fact]
    public void UnavailableBuiltIn_IsNotListedOrLoaded_AndFilesRemain()
    {
        const string name = "unavailable-test-skill";
        var loader = new SkillsLoader(_tempRoot);
        var directory = Path.Combine(loader.WorkspaceSkillsPath, name);
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "SKILL.md");
        File.WriteAllText(source, $"---\nname: {name}\ndescription: Unavailable built-in\n---\nUnavailable instructions");
        File.WriteAllText(Path.Combine(directory, ".builtin"), "unavailable-version");

        Assert.Null(loader.LoadSkill(name));
        loader.DeployBuiltInSkills();

        Assert.DoesNotContain(loader.ListSkills(), skill => skill.Name == name);
        Assert.Null(loader.ResolveSkillInfo(name));
        Assert.Null(loader.LoadSkill(name));
        Assert.Null(loader.LoadEffectiveSkill(name, variantModeEnabled: false, target: null));
        Assert.Contains("Unavailable instructions", File.ReadAllText(source));
        Assert.Equal("unavailable-version", File.ReadAllText(Path.Combine(directory, ".builtin")));
        Assert.NotNull(loader.LoadSkill("plugin-creator"));
    }

    [Fact]
    public void UnavailableBuiltIn_DoesNotHideSameNameUserSkill()
    {
        var userSkills = Path.Combine(_tempRoot, "user-skills");
        var loader = new SkillsLoader(_tempRoot, userSkills);
        var unavailable = Path.Combine(loader.WorkspaceSkillsPath, "unavailable-test-skill");
        var user = Path.Combine(userSkills, "unavailable-test-skill");
        Directory.CreateDirectory(unavailable);
        Directory.CreateDirectory(user);
        File.WriteAllText(Path.Combine(unavailable, "SKILL.md"), "Unavailable instructions");
        File.WriteAllText(Path.Combine(unavailable, ".builtin"), "unavailable-version");
        File.WriteAllText(Path.Combine(user, "SKILL.md"), "---\nname: unavailable-test-skill\ndescription: User skill\n---\nUser instructions");

        Assert.Equal("user", loader.ResolveSkillInfo("unavailable-test-skill")!.Source);
        Assert.Contains("User instructions", loader.LoadSkill("unavailable-test-skill"));

        File.Delete(Path.Combine(unavailable, ".builtin"));
        File.WriteAllText(Path.Combine(unavailable, "SKILL.md"), "---\nname: unavailable-test-skill\ndescription: Workspace skill\n---\nWorkspace instructions");
        loader.DeployBuiltInSkills();
        Assert.Equal("workspace", loader.ResolveSkillInfo("unavailable-test-skill")!.Source);
        Assert.Contains("Workspace instructions", loader.LoadSkill("unavailable-test-skill"));
    }

    [Fact]
    public void SharedRoot_SkillsAreDiscoveredAsUserSource()
    {
        var sharedSkills = Path.Combine(_tempRoot, "shared-skills");
        var loader = new SkillsLoader(_tempRoot, null, sharedSkills);
        WriteSkill(sharedSkills, "shared-only-skill", "Shared instructions");

        var info = loader.ResolveSkillInfo("shared-only-skill");

        Assert.NotNull(info);
        Assert.Equal("user", info.Source);
        Assert.StartsWith(sharedSkills, info.Path);
        Assert.Contains("Shared instructions", loader.LoadSkill("shared-only-skill"));
    }

    [Fact]
    public void SharedRoot_UserRootWinsOnDuplicateName()
    {
        var userSkills = Path.Combine(_tempRoot, "user-skills");
        var sharedSkills = Path.Combine(_tempRoot, "shared-skills");
        var loader = new SkillsLoader(_tempRoot, userSkills, sharedSkills);
        WriteSkill(userSkills, "duplicate-skill", "User instructions");
        WriteSkill(sharedSkills, "duplicate-skill", "Shared instructions");

        var info = loader.ResolveSkillInfo("duplicate-skill");

        Assert.Equal("user", info!.Source);
        Assert.StartsWith(userSkills, info.Path);
        Assert.Contains("User instructions", loader.LoadSkill("duplicate-skill"));
        Assert.Single(loader.ListSkills(), skill => skill.Name == "duplicate-skill");
    }

    [Fact]
    public void DeclaredRequirements_DoNotFilterDiscoveryOrSummary()
    {
        var sharedSkills = Path.Combine(_tempRoot, "shared-skills");
        var loader = new SkillsLoader(_tempRoot, null, sharedSkills);
        var skillDir = Path.Combine(sharedSkills, "declarative-requirements");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(
            Path.Combine(skillDir, "SKILL.md"),
            "---\nname: declarative-requirements\ndescription: Declarative requirements\nbins: command-that-does-not-exist-dotcraft\nenv: DOTCRAFT_TEST_ENV_THAT_DOES_NOT_EXIST\ntools: ToolThatDoesNotExist\n---\nInstructions");

        var skill = Assert.Single(loader.ListSkills(), candidate => candidate.Name == "declarative-requirements");
        var metadata = loader.GetSkillMetadata(skill.Name);
        var summary = loader.BuildSkillsSummary();

        Assert.Equal("command-that-does-not-exist-dotcraft", metadata!["bins"]);
        Assert.Contains("<name>declarative-requirements</name>", summary);
        Assert.DoesNotContain("available=", summary);
        Assert.DoesNotContain("<requires>", summary);
    }

    [Fact]
    public void AlwaysSkill_WithMissingDeclaredRequirements_IsLoaded()
    {
        var loader = new SkillsLoader(_tempRoot);
        var skillDir = Path.Combine(loader.WorkspaceSkillsPath, "always-declarative");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(
            Path.Combine(skillDir, "SKILL.md"),
            "---\nname: always-declarative\ndescription: Always loaded\nalways: true\nbins: command-that-does-not-exist-dotcraft\ntools: ToolThatDoesNotExist\n---\nAlways instructions");

        Assert.Contains("always-declarative", loader.GetAlwaysSkills());
        Assert.Contains("Always instructions", loader.LoadSkillsForContext(loader.GetAlwaysSkills()));
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

    private static void WriteSkill(string skillsPath, string name, string body)
    {
        var skillDir = Path.Combine(skillsPath, name);
        Directory.CreateDirectory(skillDir);
        var content = string.Join(
            Environment.NewLine,
            "---",
            $"name: {name}",
            "description: Demo",
            "---",
            body);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), content);
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
