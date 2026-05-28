using DotCraft.Configuration;
using DotCraft.Protocol.AppServer;
using DotCraft.Skills;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerSkillsManagementTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "dotcraft-appserver-skills-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SkillsList_ReturnsOptionalInterfaceMetadataAndIconDataUrl()
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

        var loader = new SkillsLoader(craftPath);
        using var harness = new AppServerTestHarness(workspaceCraftPath: craftPath, skillsLoader: loader);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsList, new { includeUnavailable = true }));
        using var response = harness.Transport.TryReadSent()!;
        var skill = response.RootElement.GetProperty("result").GetProperty("skills")[0];

        Assert.Equal("demo-skill", skill.GetProperty("name").GetString());
        Assert.Equal("Demo Skill", skill.GetProperty("displayName").GetString());
        Assert.Equal("Short demo", skill.GetProperty("shortDescription").GetString());
        Assert.StartsWith("data:image/svg+xml;base64,", skill.GetProperty("iconSmallDataUrl").GetString());
        Assert.Equal("Use $demo-skill.", skill.GetProperty("defaultPrompt").GetString());
    }

    [Fact]
    public async Task SkillsList_IgnoresMissingOrEscapingIconPaths()
    {
        var craftPath = Path.Combine(_tempRoot, ".craft");
        var safeSkillDir = Path.Combine(craftPath, "skills", "safe-skill");
        var plainSkillDir = Path.Combine(craftPath, "skills", "plain-skill");
        Directory.CreateDirectory(Path.Combine(safeSkillDir, "agents"));
        Directory.CreateDirectory(plainSkillDir);
        File.WriteAllText(Path.Combine(craftPath, "skills", "secret.svg"), "<svg />");
        File.WriteAllText(Path.Combine(safeSkillDir, "SKILL.md"), "---\nname: safe-skill\ndescription: Safe\n---\n# Safe");
        File.WriteAllText(Path.Combine(safeSkillDir, "agents", "openai.yaml"), """
            interface:
              display_name: "Safe Skill"
              icon_small: "../secret.svg"
            """);
        File.WriteAllText(Path.Combine(plainSkillDir, "SKILL.md"), "---\nname: plain-skill\ndescription: Plain\n---\n# Plain");

        var loader = new SkillsLoader(craftPath);
        using var harness = new AppServerTestHarness(workspaceCraftPath: craftPath, skillsLoader: loader);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsList, new { includeUnavailable = true }));
        using var response = harness.Transport.TryReadSent()!;
        var skills = response.RootElement.GetProperty("result").GetProperty("skills");
        var safeSkill = skills.EnumerateArray().Single(skill => skill.GetProperty("name").GetString() == "safe-skill");
        var plainSkill = skills.EnumerateArray().Single(skill => skill.GetProperty("name").GetString() == "plain-skill");

        Assert.False(safeSkill.TryGetProperty("iconSmallDataUrl", out _));
        Assert.False(plainSkill.TryGetProperty("displayName", out _));
    }

    [Fact]
    public async Task SkillsView_ReturnsEffectiveVariantBody_WhileSkillsReadReturnsSourceRawContent()
    {
        var craftPath = Path.Combine(_tempRoot, ".craft");
        var loader = new SkillsLoader(craftPath);
        WriteSkill(loader, "demo-skill", "Source body.");
        using var harness = new AppServerTestHarness(workspaceCraftPath: craftPath, skillsLoader: loader);
        var target = SkillVariantStore.CreateTarget(
            harness.Monitor.Current.Model,
            harness.Identity.WorkspacePath,
            sandboxEnabled: false,
            harness.Monitor.Current.Permissions.DefaultApprovalPolicy.ToString(),
            toolNames: null);
        var applier = new VariantSkillMutationApplier(new WorkspaceFileSkillMutationApplier(loader), loader, target);
        await applier.PatchAsync(new SkillPatchRequest("demo-skill", "Source body.", "Variant body.", null, false));
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsView, new { name = "demo-skill" }));
        using var viewResponse = harness.Transport.TryReadSent()!;
        var viewContent = viewResponse.RootElement.GetProperty("result").GetProperty("content").GetString();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsRead, new { name = "demo-skill" }));
        using var readResponse = harness.Transport.TryReadSent()!;
        var readContent = readResponse.RootElement.GetProperty("result").GetProperty("content").GetString();

        Assert.Contains("Variant body.", viewContent);
        Assert.DoesNotContain("name: demo-skill", viewContent);
        Assert.Contains("Source body.", readContent);
        Assert.Contains("name: demo-skill", readContent);
    }

    [Fact]
    public async Task SkillsList_ReportsHasVariantForCurrentVariantOnly()
    {
        var craftPath = Path.Combine(_tempRoot, ".craft");
        var loader = new SkillsLoader(craftPath);
        WriteSkill(loader, "demo-skill", "Source body.");
        using var harness = new AppServerTestHarness(workspaceCraftPath: craftPath, skillsLoader: loader);
        var target = SkillVariantStore.CreateTarget(
            harness.Monitor.Current.Model,
            harness.Identity.WorkspacePath,
            sandboxEnabled: false,
            harness.Monitor.Current.Permissions.DefaultApprovalPolicy.ToString(),
            toolNames: null);
        var applier = new VariantSkillMutationApplier(new WorkspaceFileSkillMutationApplier(loader), loader, target);
        await applier.PatchAsync(new SkillPatchRequest("demo-skill", "Source body.", "Variant body.", null, false));
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsList, new { includeUnavailable = true }));
        using var listResponse = harness.Transport.TryReadSent()!;
        var skill = listResponse.RootElement.GetProperty("result").GetProperty("skills")[0];
        Assert.True(skill.GetProperty("hasVariant").GetBoolean());

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsRestoreOriginal, new { name = "demo-skill" }));
        using var restoreResponse = harness.Transport.TryReadSent()!;
        Assert.True(restoreResponse.RootElement.GetProperty("result").GetProperty("restored").GetBoolean());

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsList, new { includeUnavailable = true }));
        using var restoredListResponse = harness.Transport.TryReadSent()!;
        var restoredSkill = restoredListResponse.RootElement.GetProperty("result").GetProperty("skills")[0];
        Assert.False(restoredSkill.TryGetProperty("hasVariant", out var hasVariant)
                     && hasVariant.GetBoolean());
    }

    [Fact]
    public async Task SkillsRestoreOriginal_MakesSkillsViewFallBackToSource()
    {
        var craftPath = Path.Combine(_tempRoot, ".craft");
        var loader = new SkillsLoader(craftPath);
        WriteSkill(loader, "demo-skill", "Source body.");
        using var harness = new AppServerTestHarness(workspaceCraftPath: craftPath, skillsLoader: loader);
        var target = SkillVariantStore.CreateTarget(
            harness.Monitor.Current.Model,
            harness.Identity.WorkspacePath,
            sandboxEnabled: false,
            harness.Monitor.Current.Permissions.DefaultApprovalPolicy.ToString(),
            toolNames: null);
        var applier = new VariantSkillMutationApplier(new WorkspaceFileSkillMutationApplier(loader), loader, target);
        await applier.PatchAsync(new SkillPatchRequest("demo-skill", "Source body.", "Variant body.", null, false));
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsRestoreOriginal, new { name = "demo-skill" }));
        using var restoreResponse = harness.Transport.TryReadSent()!;
        Assert.True(restoreResponse.RootElement.GetProperty("result").GetProperty("restored").GetBoolean());

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsView, new { name = "demo-skill" }));
        using var viewResponse = harness.Transport.TryReadSent()!;
        var viewContent = viewResponse.RootElement.GetProperty("result").GetProperty("content").GetString();

        Assert.Contains("Source body.", viewContent);
        Assert.DoesNotContain("Variant body.", viewContent);
    }

    [Fact]
    public async Task SkillsUninstall_RemovesWorkspaceSkill_DisabledEntry_AndEmitsConfigChanged()
    {
        var craftPath = Path.Combine(_tempRoot, ".craft");
        var loader = new SkillsLoader(craftPath);
        WriteSkill(loader, "demo-skill", "Source body.");
        loader.SetDisabledSkills(["demo-skill"]);
        using var harness = new AppServerTestHarness(workspaceCraftPath: craftPath, skillsLoader: loader);
        var changes = new List<AppConfigChangedEventArgs>();
        harness.Monitor.Changed += OnChanged;
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsUninstall, new { name = "demo-skill" }));
        using var response = harness.Transport.TryReadSent()!;
        var result = response.RootElement.GetProperty("result");

        Assert.True(result.GetProperty("uninstalled").GetBoolean());
        Assert.Equal("workspace", result.GetProperty("source").GetString());
        Assert.False(Directory.Exists(Path.Combine(loader.WorkspaceSkillsPath, "demo-skill")));
        Assert.DoesNotContain(loader.ListSkills(filterUnavailable: false), skill => skill.Name == "demo-skill");
        Assert.DoesNotContain(loader.ListSkills(filterUnavailable: false), skill => !skill.Enabled);
        Assert.Single(changes);
        Assert.Equal(AppServerMethods.SkillsUninstall, changes[0].Source);
        Assert.Contains(ConfigChangeRegions.Skills, changes[0].Regions);

        var configText = File.ReadAllText(Path.Combine(craftPath, "config.json"));
        Assert.DoesNotContain("demo-skill", configText);
        harness.Monitor.Changed -= OnChanged;

        void OnChanged(object? sender, AppConfigChangedEventArgs args) => changes.Add(args);
    }

    [Fact]
    public async Task SkillsUninstall_RemovesUserSkillFromUserRoot()
    {
        var craftPath = Path.Combine(_tempRoot, ".craft");
        var userSkillsPath = Path.Combine(_tempRoot, "user-skills");
        var loader = new SkillsLoader(craftPath, userSkillsPath);
        WriteSkillAtRoot(userSkillsPath, "user-skill", "User body.");
        using var harness = new AppServerTestHarness(workspaceCraftPath: craftPath, skillsLoader: loader);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsUninstall, new { name = "user-skill" }));
        using var response = harness.Transport.TryReadSent()!;
        var result = response.RootElement.GetProperty("result");

        Assert.True(result.GetProperty("uninstalled").GetBoolean());
        Assert.Equal("user", result.GetProperty("source").GetString());
        Assert.False(Directory.Exists(Path.Combine(userSkillsPath, "user-skill")));
    }

    [Fact]
    public async Task SkillsUninstall_RejectsBuiltinAndPluginSkills()
    {
        var craftPath = Path.Combine(_tempRoot, ".craft");
        var loader = new SkillsLoader(craftPath);
        WriteSkill(loader, "builtin-skill", "Built-in body.");
        File.WriteAllText(Path.Combine(loader.WorkspaceSkillsPath, "builtin-skill", ".builtin"), string.Empty);

        var pluginSkillsPath = Path.Combine(_tempRoot, "plugin", "skills");
        WriteSkillAtRoot(pluginSkillsPath, "plugin-skill", "Plugin body.");
        loader.SetPluginSkillSources([
            new SkillsLoader.PluginSkillSource("demo-plugin", "Demo Plugin", pluginSkillsPath)
        ]);

        using var harness = new AppServerTestHarness(workspaceCraftPath: craftPath, skillsLoader: loader);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsUninstall, new { name = "builtin-skill" }));
        using var builtinResponse = harness.Transport.TryReadSent()!;
        AppServerTestHarness.AssertIsErrorResponse(builtinResponse, AppServerErrors.InvalidParamsCode);
        Assert.True(Directory.Exists(Path.Combine(loader.WorkspaceSkillsPath, "builtin-skill")));

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsUninstall, new { name = "plugin-skill" }));
        using var pluginResponse = harness.Transport.TryReadSent()!;
        AppServerTestHarness.AssertIsErrorResponse(pluginResponse, AppServerErrors.InvalidParamsCode);
        Assert.True(Directory.Exists(Path.Combine(pluginSkillsPath, "plugin-skill")));
    }

    [Fact]
    public async Task SkillsUninstall_RemovesAssociatedVariants()
    {
        var craftPath = Path.Combine(_tempRoot, ".craft");
        var loader = new SkillsLoader(craftPath);
        WriteSkill(loader, "demo-skill", "Source body.");
        using var harness = new AppServerTestHarness(workspaceCraftPath: craftPath, skillsLoader: loader);
        var target = SkillVariantStore.CreateTarget(
            harness.Monitor.Current.Model,
            harness.Identity.WorkspacePath,
            sandboxEnabled: false,
            harness.Monitor.Current.Permissions.DefaultApprovalPolicy.ToString(),
            toolNames: null);
        var applier = new VariantSkillMutationApplier(new WorkspaceFileSkillMutationApplier(loader), loader, target);
        await applier.PatchAsync(new SkillPatchRequest("demo-skill", "Source body.", "Variant body.", null, false));
        var sourceVariantRoot = Path.Combine(loader.VariantStore.VariantsRoot, "workspace.demo-skill");
        Assert.True(Directory.Exists(sourceVariantRoot));
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsUninstall, new { name = "demo-skill" }));
        using var response = harness.Transport.TryReadSent()!;
        var result = response.RootElement.GetProperty("result");

        Assert.Equal(1, result.GetProperty("removedVariantCount").GetInt32());
        Assert.False(Directory.Exists(sourceVariantRoot));
        Assert.False(Directory.Exists(Path.Combine(loader.WorkspaceSkillsPath, "demo-skill")));
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
