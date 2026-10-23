using DotCraft.Configuration;
using DotCraft.Skills;
using DotCraft.AppServer;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerSkillsManagementTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "dotcraft-appserver-skills-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SkillsList_ReturnsInterfaceMetadataAndOnlySafeIconDataUrls()
    {
        var craftPath = Path.Combine(_tempRoot, ".craft");
        var skillDir = Path.Combine(craftPath, "skills", "demo-skill");
        Directory.CreateDirectory(Path.Combine(skillDir, "agents"));
        Directory.CreateDirectory(Path.Combine(skillDir, "assets"));
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), "---\nname: demo-skill\ndescription: Long skill description\n---\n# Demo");
        File.WriteAllText(Path.Combine(skillDir, "agents", "openai.yaml"), """
            interface:
              display_name: "Demo Skill"
              short_description: "Short demo"
              icon_small: "./assets/demo.svg"
              default_prompt: "Use $demo-skill."
            """);
        File.WriteAllText(Path.Combine(skillDir, "assets", "demo.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 16 16\" />");
        var escapingSkillDir = Path.Combine(craftPath, "skills", "safe-skill");
        var plainSkillDir = Path.Combine(craftPath, "skills", "plain-skill");
        Directory.CreateDirectory(Path.Combine(escapingSkillDir, "agents"));
        Directory.CreateDirectory(plainSkillDir);
        File.WriteAllText(Path.Combine(craftPath, "skills", "secret.svg"), "<svg />");
        File.WriteAllText(Path.Combine(escapingSkillDir, "SKILL.md"), "---\nname: safe-skill\ndescription: Safe\n---\n# Safe");
        File.WriteAllText(Path.Combine(escapingSkillDir, "agents", "openai.yaml"), """
            interface:
              display_name: "Safe Skill"
              icon_small: "../secret.svg"
            """);
        File.WriteAllText(Path.Combine(plainSkillDir, "SKILL.md"), "---\nname: plain-skill\ndescription: Plain\n---\n# Plain");

        var loader = new SkillsLoader(craftPath);
        using var harness = new AppServerTestHarness(workspaceCraftPath: craftPath, skillsLoader: loader);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsList, new { }));
        using var response = harness.Transport.TryReadSent()!;
        var skills = response.RootElement.GetProperty("result").GetProperty("skills").EnumerateArray()
            .ToDictionary(skill => skill.GetProperty("name").GetString()!);

        var skill = skills["demo-skill"];
        Assert.Equal("Demo Skill", skill.GetProperty("displayName").GetString());
        Assert.Equal("Short demo", skill.GetProperty("shortDescription").GetString());
        Assert.StartsWith("data:image/svg+xml;base64,", skill.GetProperty("iconSmallDataUrl").GetString());
        Assert.Equal("Use $demo-skill.", skill.GetProperty("defaultPrompt").GetString());
        Assert.False(skill.TryGetProperty("available", out _));
        Assert.False(skill.TryGetProperty("unavailableReason", out _));
        Assert.False(skills["safe-skill"].TryGetProperty("iconSmallDataUrl", out _));
        Assert.False(skills["plain-skill"].TryGetProperty("displayName", out _));
    }

    [Fact]
    public async Task SkillVariant_IsViewedAndListedUntilRestoredWhileReadReturnsTheSource()
    {
        var craftPath = Path.Combine(_tempRoot, ".craft");
        var loader = new SkillsLoader(craftPath);
        WriteSkill(loader, "demo-skill", "Source body.");
        using var harness = new AppServerTestHarness(workspaceCraftPath: craftPath, skillsLoader: loader);
        await PatchVariantAsync(harness, loader);
        await harness.InitializeAsync();

        var viewContent = await ReadContentAsync(harness, DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsView);
        Assert.Contains("Variant body.", viewContent);
        Assert.DoesNotContain("name: demo-skill", viewContent);
        var readContent = await ReadContentAsync(harness, DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsRead);
        Assert.Contains("Source body.", readContent);
        Assert.Contains("name: demo-skill", readContent);
        Assert.True(await HasVariantAsync(harness));

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsRestoreOriginal, new { name = "demo-skill" }));
        using (var restoreResponse = harness.Transport.TryReadSent()!)
            Assert.True(restoreResponse.RootElement.GetProperty("result").GetProperty("restored").GetBoolean());

        Assert.False(await HasVariantAsync(harness));
        var restoredView = await ReadContentAsync(harness, DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsView);
        Assert.Contains("Source body.", restoredView);
        Assert.DoesNotContain("Variant body.", restoredView);
    }

    [Fact]
    public async Task SkillsUninstall_RemovesWorkspaceSkillVariantsAndDisabledEntryAndEmitsConfigChanged()
    {
        var craftPath = Path.Combine(_tempRoot, ".craft");
        var loader = new SkillsLoader(craftPath);
        WriteSkill(loader, "demo-skill", "Source body.");
        loader.SetDisabledSkills(["demo-skill"]);
        using var harness = new AppServerTestHarness(workspaceCraftPath: craftPath, skillsLoader: loader);
        await PatchVariantAsync(harness, loader);
        var sourceVariantRoot = Path.Combine(loader.VariantStore.VariantsRoot, "workspace.demo-skill");
        Assert.True(Directory.Exists(sourceVariantRoot));
        var changes = new List<AppConfigChangedEventArgs>();
        harness.Monitor.Changed += OnChanged;
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsUninstall, new { name = "demo-skill" }));
        using var response = harness.Transport.TryReadSent()!;
        var result = response.RootElement.GetProperty("result");

        Assert.True(result.GetProperty("uninstalled").GetBoolean());
        Assert.Equal("workspace", result.GetProperty("source").GetString());
        Assert.Equal(1, result.GetProperty("removedVariantCount").GetInt32());
        Assert.False(Directory.Exists(sourceVariantRoot));
        Assert.False(Directory.Exists(Path.Combine(loader.WorkspaceSkillsPath, "demo-skill")));
        Assert.DoesNotContain(loader.ListSkills(), skill => skill.Name == "demo-skill");
        Assert.DoesNotContain(loader.ListSkills(), skill => !skill.Enabled);
        var change = Assert.Single(changes);
        Assert.Equal(DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsUninstall, change.Source);
        Assert.Contains(ConfigChangeRegions.Skills, change.Regions);
        Assert.DoesNotContain("demo-skill", File.ReadAllText(Path.Combine(craftPath, "config.json")));
        harness.Monitor.Changed -= OnChanged;

        void OnChanged(object? sender, AppConfigChangedEventArgs args) => changes.Add(args);
    }

    [Fact]
    public async Task SkillsUninstall_RemovesUserSkillsButRejectsSharedBuiltinAndPluginSkills()
    {
        var craftPath = Path.Combine(_tempRoot, ".craft");
        var userSkillsPath = Path.Combine(_tempRoot, "user-skills");
        var sharedSkillsPath = Path.Combine(_tempRoot, "shared-skills");
        var loader = new SkillsLoader(craftPath, userSkillsPath, sharedSkillsPath);
        loader.DeployBuiltInSkills();
        WriteSkillAtRoot(userSkillsPath, "user-skill", "User body.");
        WriteSkillAtRoot(sharedSkillsPath, "shared-skill", "Shared body.");
        var builtinSkill = loader.ListSkills()
            .First(skill => string.Equals(skill.Source, "builtin", StringComparison.OrdinalIgnoreCase));
        var pluginSkillsPath = Path.Combine(_tempRoot, "plugin", "skills");
        WriteSkillAtRoot(pluginSkillsPath, "plugin-skill", "Plugin body.");
        loader.SetPluginSkillSources([
            new SkillsLoader.PluginSkillSource("demo-plugin", "Demo Plugin", pluginSkillsPath, Path.GetDirectoryName(pluginSkillsPath)!)
        ]);
        using var harness = new AppServerTestHarness(workspaceCraftPath: craftPath, skillsLoader: loader);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsUninstall, new { name = "user-skill" }));
        using (var response = harness.Transport.TryReadSent()!)
        {
            var result = response.RootElement.GetProperty("result");
            Assert.True(result.GetProperty("uninstalled").GetBoolean());
            Assert.Equal("user", result.GetProperty("source").GetString());
        }
        Assert.False(Directory.Exists(Path.Combine(userSkillsPath, "user-skill")));

        foreach (var name in new[] { "shared-skill", builtinSkill.Name, "plugin-skill" })
        {
            await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsUninstall, new { name }));
            using var rejected = harness.Transport.TryReadSent()!;
            AppServerTestHarness.AssertIsErrorResponse(rejected, AppServerErrors.InvalidParamsCode);
        }
        Assert.True(Directory.Exists(Path.Combine(sharedSkillsPath, "shared-skill")));
        Assert.True(Directory.Exists(Path.GetDirectoryName(builtinSkill.Path)));
        Assert.True(Directory.Exists(Path.Combine(pluginSkillsPath, "plugin-skill")));
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
        }
    }

    private static Task PatchVariantAsync(AppServerTestHarness harness, SkillsLoader loader)
    {
        var target = SkillVariantStore.CreateTarget(
            harness.Monitor.Current.ProviderPreferences[harness.Monitor.Current.ProviderId].Model,
            harness.Identity.WorkspacePath,
            harness.Monitor.Current.Permissions.DefaultApprovalPolicy.ToString(),
            toolNames: null);
        var applier = new VariantSkillMutationApplier(new WorkspaceFileSkillMutationApplier(loader), loader, target);
        return applier.PatchAsync(new SkillPatchRequest("demo-skill", "Source body.", "Variant body.", null, false));
    }

    private static async Task<string?> ReadContentAsync(AppServerTestHarness harness, string method)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(method, new { name = "demo-skill" }));
        using var response = harness.Transport.TryReadSent()!;
        return response.RootElement.GetProperty("result").GetProperty("content").GetString();
    }

    private static async Task<bool> HasVariantAsync(AppServerTestHarness harness)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsList, new { }));
        using var response = harness.Transport.TryReadSent()!;
        var skill = response.RootElement.GetProperty("result").GetProperty("skills")[0];
        return skill.TryGetProperty("hasVariant", out var hasVariant) && hasVariant.GetBoolean();
    }

    private static void WriteSkill(SkillsLoader loader, string name, string body)
    {
        WriteSkillAtRoot(loader.WorkspaceSkillsPath, name, body);
    }

    private static void WriteSkillAtRoot(string skillsRoot, string name, string body)
    {
        var skillDir = Path.Combine(skillsRoot, name);
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(
            Path.Combine(skillDir, "SKILL.md"),
            $"""
            ---
            name: {name}
            description: Test skill
            ---

            # {name}

            {body}
            """);
    }
}
