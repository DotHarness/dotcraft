using DotCraft.Skills;
using Xunit;

namespace DotCraft.Tests.Skills;

public sealed class SkillInstallServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "dotcraft-skillinstall-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task VerifyAsync_AcceptsOrdinaryBundleFilesAndReturnsSkillName()
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
        Assert.Equal("demo-skill", result.SkillName);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task VerifyAsync_RejectsMissingRootSkillFileAndHiddenControlFiles()
    {
        var nested = Path.Combine(_tempRoot, "candidate");
        Directory.CreateDirectory(Path.Combine(nested, "nested"));
        File.WriteAllText(Path.Combine(nested, "nested", "SKILL.md"), ValidSkill("demo-skill"));
        var hidden = WriteCandidate("demo-skill");
        File.WriteAllText(Path.Combine(hidden, ".env"), "SECRET=1");
        var service = CreateService();

        var missingRoot = await service.VerifyAsync(new SkillInstallVerifyRequest(nested));
        var hiddenFile = await service.VerifyAsync(new SkillInstallVerifyRequest(hidden));

        Assert.False(missingRoot.IsValid);
        Assert.Contains(missingRoot.Errors, error => error.Contains("SKILL.md at its root", StringComparison.Ordinal));
        Assert.False(hiddenFile.IsValid);
        Assert.Contains(hiddenFile.Errors, error => error.Contains(".env", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyAsync_AllowsUppercaseFrontmatterNameAndPrefersTheExpectedLocalName()
    {
        var candidate = WriteCandidate("Git");
        var service = CreateService();

        var frontmatter = await service.VerifyAsync(new SkillInstallVerifyRequest(candidate));
        var expected = await service.VerifyAsync(new SkillInstallVerifyRequest(candidate, ExpectedName: "git"));

        Assert.True(frontmatter.IsValid);
        Assert.Equal("Git", frontmatter.SkillName);
        Assert.True(expected.IsValid);
        Assert.Equal("git", expected.SkillName);
        Assert.Empty(expected.Errors);
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
    public async Task InstallAsync_WithDuplicateName_ReplacesOnlyWhenOverwriteIsRequested()
    {
        var candidate = WriteCandidate("demo-skill", "Replacement.");
        var loader = CreateLoader();
        Directory.CreateDirectory(Path.Combine(loader.WorkspaceSkillsPath, "demo-skill"));
        File.WriteAllText(Path.Combine(loader.WorkspaceSkillsPath, "demo-skill", "SKILL.md"), ValidSkill("demo-skill", "Existing."));
        var service = new SkillInstallService(loader);

        var rejected = await service.InstallAsync(new SkillInstallRequest(candidate));

        Assert.False(rejected.Success);
        Assert.Contains(rejected.Errors, error => error.Contains("already exists", StringComparison.Ordinal));
        Assert.Contains("Existing.", File.ReadAllText(Path.Combine(loader.WorkspaceSkillsPath, "demo-skill", "SKILL.md")));

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
