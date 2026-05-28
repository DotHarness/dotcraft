using DotCraft.Skills;

namespace DotCraft.Tests.Skills;

public sealed class SkillInstallServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "dotcraft-skillinstall-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task VerifyAsync_WithValidCandidate_ReturnsSkillName()
    {
        var candidate = WriteCandidate("demo-skill");
        var service = CreateService();

        var result = await service.VerifyAsync(new SkillInstallVerifyRequest(candidate));

        Assert.True(result.IsValid);
        Assert.Equal("demo-skill", result.SkillName);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task VerifyAsync_RejectsMissingRootSkillFile()
    {
        var candidate = Path.Combine(_tempRoot, "candidate");
        Directory.CreateDirectory(Path.Combine(candidate, "nested"));
        File.WriteAllText(Path.Combine(candidate, "nested", "SKILL.md"), ValidSkill("demo-skill"));
        var service = CreateService();

        var result = await service.VerifyAsync(new SkillInstallVerifyRequest(candidate));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("SKILL.md at its root", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyAsync_AllowsOrdinaryBundleFiles()
    {
        var candidate = WriteCandidate("demo-skill");
        Directory.CreateDirectory(Path.Combine(candidate, "docs"));
        Directory.CreateDirectory(Path.Combine(candidate, "references"));
        File.WriteAllText(Path.Combine(candidate, "advanced.md"), "Advanced notes");
        File.WriteAllText(Path.Combine(candidate, "_meta.json"), "{}");
        File.WriteAllText(Path.Combine(candidate, "docs", "guide.md"), "Guide");
        File.WriteAllText(Path.Combine(candidate, "references", "commands.md"), "Commands");
        var service = CreateService();

        var result = await service.VerifyAsync(new SkillInstallVerifyRequest(candidate));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task VerifyAsync_AllowsUppercaseFrontmatterName()
    {
        var candidate = WriteCandidate("Git");
        var service = CreateService();

        var result = await service.VerifyAsync(new SkillInstallVerifyRequest(candidate));

        Assert.True(result.IsValid);
        Assert.Equal("Git", result.SkillName);
    }

    [Fact]
    public async Task VerifyAsync_WithExpectedName_UsesLocalNameWithoutRequiringFrontmatterMatch()
    {
        var candidate = WriteCandidate("Git");
        var service = CreateService();

        var result = await service.VerifyAsync(new SkillInstallVerifyRequest(candidate, ExpectedName: "git"));

        Assert.True(result.IsValid);
        Assert.Equal("git", result.SkillName);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task VerifyAsync_RejectsHiddenControlFiles()
    {
        var candidate = WriteCandidate("demo-skill");
        File.WriteAllText(Path.Combine(candidate, ".env"), "SECRET=1");
        var service = CreateService();

        var result = await service.VerifyAsync(new SkillInstallVerifyRequest(candidate));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains(".env", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InstallAsync_WithValidCandidate_PublishesWorkspaceSkillAndMarker()
    {
        var candidate = WriteCandidate("demo-skill");
        Directory.CreateDirectory(Path.Combine(candidate, "docs"));
        File.WriteAllText(Path.Combine(candidate, "commands.md"), "Commands");
        File.WriteAllText(Path.Combine(candidate, "docs", "guide.md"), "Guide");
        var loader = CreateLoader();
        var service = new SkillInstallService(loader);

        var result = await service.InstallAsync(new SkillInstallRequest(candidate, Source: "local-test"));

        Assert.True(result.Success);
        Assert.Equal("demo-skill", result.SkillName);
        Assert.False(result.Overwritten);
        Assert.StartsWith("sha256:", result.SourceFingerprint);
        Assert.True(File.Exists(Path.Combine(loader.WorkspaceSkillsPath, "demo-skill", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(loader.WorkspaceSkillsPath, "demo-skill", "commands.md")));
        Assert.True(File.Exists(Path.Combine(loader.WorkspaceSkillsPath, "demo-skill", "docs", "guide.md")));
        Assert.True(File.Exists(Path.Combine(loader.WorkspaceSkillsPath, "demo-skill", ".dotcraft-skill.json")));
    }

    [Fact]
    public async Task InstallAsync_WithDuplicateName_RejectsWithoutOverwrite()
    {
        var candidate = WriteCandidate("demo-skill");
        var loader = CreateLoader();
        Directory.CreateDirectory(Path.Combine(loader.WorkspaceSkillsPath, "demo-skill"));
        File.WriteAllText(Path.Combine(loader.WorkspaceSkillsPath, "demo-skill", "SKILL.md"), ValidSkill("demo-skill", "Existing."));
        var service = new SkillInstallService(loader);

        var result = await service.InstallAsync(new SkillInstallRequest(candidate));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("already exists", StringComparison.Ordinal));
        Assert.Contains("Existing.", File.ReadAllText(Path.Combine(loader.WorkspaceSkillsPath, "demo-skill", "SKILL.md")));
    }

    [Fact]
    public async Task InstallAsync_WithOverwrite_ReplacesWorkspaceSkill()
    {
        var candidate = WriteCandidate("demo-skill", "Replacement.");
        var loader = CreateLoader();
        Directory.CreateDirectory(Path.Combine(loader.WorkspaceSkillsPath, "demo-skill"));
        File.WriteAllText(Path.Combine(loader.WorkspaceSkillsPath, "demo-skill", "SKILL.md"), ValidSkill("demo-skill", "Existing."));
        var service = new SkillInstallService(loader);

        var result = await service.InstallAsync(new SkillInstallRequest(candidate, Overwrite: true));

        Assert.True(result.Success);
        Assert.True(result.Overwritten);
        var installed = File.ReadAllText(Path.Combine(loader.WorkspaceSkillsPath, "demo-skill", "SKILL.md"));
        Assert.Contains("Replacement.", installed);
        Assert.DoesNotContain("Existing.", installed);
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

    private SkillInstallService CreateService() => new(CreateLoader());

    private SkillsLoader CreateLoader()
    {
        Directory.CreateDirectory(_tempRoot);
        return new SkillsLoader(Path.Combine(_tempRoot, ".craft"));
    }

    private string WriteCandidate(string name, string body = "Follow these steps.")
    {
        var candidate = Path.Combine(_tempRoot, "candidate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(candidate);
        Directory.CreateDirectory(Path.Combine(candidate, "scripts"));
        Directory.CreateDirectory(Path.Combine(candidate, "assets"));
        Directory.CreateDirectory(Path.Combine(candidate, "agents"));
        File.WriteAllText(Path.Combine(candidate, "SKILL.md"), ValidSkill(name, body));
        File.WriteAllText(Path.Combine(candidate, "scripts", "check.ps1"), "Write-Output ok");
        File.WriteAllText(Path.Combine(candidate, "assets", "notes.txt"), "notes");
        File.WriteAllText(Path.Combine(candidate, "agents", "openai.yaml"), "interface:\n  display_name: Demo\n");
        return candidate;
    }

    private static string ValidSkill(string name, string body = "Follow these steps.") =>
        $"""
        ---
        name: {name}
        description: Test skill
        ---

        # {name}

        {body}
        """;
}
