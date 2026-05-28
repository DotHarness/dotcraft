using DotCraft.Skills;

namespace DotCraft.Tests.Skills;

public sealed class WorkspaceFileSkillMutationApplierTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "dotcraft-skillapplier-tests", Guid.NewGuid().ToString("N"));
    private readonly SkillsLoader _skillsLoader;
    private readonly WorkspaceFileSkillMutationApplier _applier;

    public WorkspaceFileSkillMutationApplierTests()
    {
        Directory.CreateDirectory(_tempRoot);
        _skillsLoader = new SkillsLoader(_tempRoot);
        _applier = new WorkspaceFileSkillMutationApplier(_skillsLoader);
    }

    [Fact]
    public async Task EditAsync_ReplacesSkillContent()
    {
        await _applier.CreateAsync(new SkillCreateRequest("edit-skill", ValidSkill("edit-skill", "Original.")));

        var result = await _applier.EditAsync(new SkillEditRequest("edit-skill", ValidSkill("edit-skill", "Updated.")));
        var content = File.ReadAllText(Path.Combine(_skillsLoader.WorkspaceSkillsPath, "edit-skill", "SKILL.md"));

        Assert.True(result.Success);
        Assert.Contains("Updated.", content);
        Assert.DoesNotContain("Original.", content);
    }

    [Fact]
    public async Task PatchAsync_DoesNotWriteWhenPatchWouldBreakFrontmatter()
    {
        await _applier.CreateAsync(new SkillCreateRequest("safe-skill", ValidSkill("safe-skill", "Original.")));
        var path = Path.Combine(_skillsLoader.WorkspaceSkillsPath, "safe-skill", "SKILL.md");
        var original = File.ReadAllText(path);

        var result = await _applier.PatchAsync(new SkillPatchRequest("safe-skill", "name: safe-skill", "", null, false));

        Assert.False(result.Success);
        Assert.Contains("frontmatter", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public async Task WriteFileAsync_WritesOnlyAllowedSupportingPath()
    {
        await _applier.CreateAsync(new SkillCreateRequest("file-skill", ValidSkill("file-skill")));

        var result = await _applier.WriteFileAsync(
            new SkillWriteFileRequest("file-skill", "scripts/notes.md", "notes"));

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(_skillsLoader.WorkspaceSkillsPath, "file-skill", "scripts", "notes.md")));
    }

    [Theory]
    [InlineData("references/notes.md")]
    [InlineData("templates/template.md")]
    public async Task WriteFileAsync_RejectsUnsupportedSupportingPath(string filePath)
    {
        await _applier.CreateAsync(new SkillCreateRequest("file-skill", ValidSkill("file-skill")));

        var result = await _applier.WriteFileAsync(new SkillWriteFileRequest("file-skill", filePath, "notes"));

        Assert.False(result.Success);
        Assert.Contains("scripts, assets", result.Error);
    }

    [Fact]
    public async Task RemoveFileAsync_RemovesSupportingFile()
    {
        await _applier.CreateAsync(new SkillCreateRequest("file-skill", ValidSkill("file-skill")));
        await _applier.WriteFileAsync(new SkillWriteFileRequest("file-skill", "assets/notes.md", "notes"));

        var result = await _applier.RemoveFileAsync(new SkillRemoveFileRequest("file-skill", "assets/notes.md"));

        Assert.True(result.Success);
        Assert.False(File.Exists(Path.Combine(_skillsLoader.WorkspaceSkillsPath, "file-skill", "assets", "notes.md")));
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

    private static string ValidSkill(string name, string body = "Follow these steps.\nVerify the result.") =>
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
