using DotCraft.Skills;

namespace DotCraft.Tests.Skills;

public sealed class SkillVariantTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "dotcraft-skillvariant-tests", Guid.NewGuid().ToString("N"));
    private readonly SkillsLoader _skillsLoader;
    private readonly SkillVariantTarget _target;

    public SkillVariantTests()
    {
        Directory.CreateDirectory(_tempRoot);
        _skillsLoader = new SkillsLoader(_tempRoot);
        _target = SkillVariantStore.CreateTarget(
            "test-model",
            _tempRoot,
            sandboxEnabled: false,
            approvalPolicy: "default",
            toolNames: ["SkillView", "SkillManage"]);
    }

    [Fact]
    public void LoadEffectiveSkill_SourceOnly_ReturnsBodyWithoutFrontmatter()
    {
        WriteSourceSkill("demo-skill", "Source body.");

        var effective = _skillsLoader.LoadEffectiveSkill("demo-skill", true, _target);

        Assert.NotNull(effective);
        Assert.Equal("source", effective.Origin);
        Assert.Contains("Source body.", effective.Content);
        Assert.DoesNotContain("name: demo-skill", effective.Content);
    }

    [Fact]
    public async Task VariantMutation_PatchesVariantWithoutChangingSource()
    {
        var sourcePath = WriteSourceSkill("demo-skill", "Source body.");
        var applier = CreateVariantApplier();

        var result = await applier.PatchAsync(new SkillPatchRequest(
            "demo-skill",
            "Source body.",
            "Variant body.",
            null,
            false));

        Assert.True(result.Success);
        Assert.Contains("Source body.", File.ReadAllText(sourcePath));

        var effective = _skillsLoader.LoadEffectiveSkill("demo-skill", true, _target);
        Assert.NotNull(effective);
        Assert.Equal("variant", effective.Origin);
        Assert.Contains("Variant body.", effective.Content);
    }

    [Fact]
    public async Task VariantMutation_ReusesCurrentVariant()
    {
        WriteSourceSkill("demo-skill", "Source body.");
        var applier = CreateVariantApplier();

        await applier.PatchAsync(new SkillPatchRequest("demo-skill", "Source body.", "Variant body.", null, false));
        await applier.PatchAsync(new SkillPatchRequest("demo-skill", "Variant body.", "Second variant body.", null, false));

        var manifestCount = Directory.EnumerateFiles(
                _skillsLoader.VariantStore.VariantsRoot,
                "manifest.json",
                SearchOption.AllDirectories)
            .Count();
        var effective = _skillsLoader.LoadEffectiveSkill("demo-skill", true, _target);

        Assert.Equal(1, manifestCount);
        Assert.NotNull(effective);
        Assert.Contains("Second variant body.", effective.Content);
    }

    [Fact]
    public async Task SourceFingerprintChange_MarksVariantStaleAndFallsBackToSource()
    {
        var sourcePath = WriteSourceSkill("demo-skill", "Source body.");
        var applier = CreateVariantApplier();
        await applier.PatchAsync(new SkillPatchRequest("demo-skill", "Source body.", "Variant body.", null, false));

        File.WriteAllText(sourcePath, ValidSkill("demo-skill", "Changed source body."));

        var effective = _skillsLoader.LoadEffectiveSkill("demo-skill", true, _target);

        Assert.NotNull(effective);
        Assert.Equal("source", effective.Origin);
        Assert.Contains("Changed source body.", effective.Content);
    }

    [Fact]
    public async Task RestoreOriginalSkill_FallsBackToSource()
    {
        WriteSourceSkill("demo-skill", "Source body.");
        var applier = CreateVariantApplier();
        await applier.PatchAsync(new SkillPatchRequest("demo-skill", "Source body.", "Variant body.", null, false));

        var restored = _skillsLoader.RestoreOriginalSkill("demo-skill", _target);
        var effective = _skillsLoader.LoadEffectiveSkill("demo-skill", true, _target);

        Assert.True(restored);
        Assert.NotNull(effective);
        Assert.Equal("source", effective.Origin);
        Assert.Contains("Source body.", effective.Content);
    }

    [Fact]
    public async Task SupportingFileMutation_WritesVariantOnly()
    {
        var sourcePath = WriteSourceSkill("demo-skill", "Use scripts/notes.md.");
        var sourceDir = Path.GetDirectoryName(sourcePath)!;
        Directory.CreateDirectory(Path.Combine(sourceDir, "scripts"));
        File.WriteAllText(Path.Combine(sourceDir, "scripts", "notes.md"), "source notes");
        var applier = CreateVariantApplier();

        var result = await applier.WriteFileAsync(new SkillWriteFileRequest(
            "demo-skill",
            "scripts/notes.md",
            "variant notes"));

        var effective = _skillsLoader.LoadEffectiveSkill("demo-skill", true, _target);
        Assert.True(result.Success);
        Assert.Equal("source notes", File.ReadAllText(Path.Combine(sourceDir, "scripts", "notes.md")));
        Assert.NotNull(effective);
        Assert.Equal("variant", effective.Origin);
        Assert.Equal(
            "variant notes",
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(effective.Path)!, "scripts", "notes.md")));
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

    private VariantSkillMutationApplier CreateVariantApplier() =>
        new(new WorkspaceFileSkillMutationApplier(_skillsLoader), _skillsLoader, _target);

    private string WriteSourceSkill(string name, string body)
    {
        var skillDir = Path.Combine(_skillsLoader.WorkspaceSkillsPath, name);
        Directory.CreateDirectory(skillDir);
        var skillFile = Path.Combine(skillDir, "SKILL.md");
        File.WriteAllText(skillFile, ValidSkill(name, body));
        return skillFile;
    }

    private static string ValidSkill(string name, string body) =>
        $"""
        ---
        name: {name}
        description: Test skill for {name}
        version: 0.1.0
        ---

        # {name}

        {body}
        """;
}
