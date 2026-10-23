using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Security;
using DotCraft.Security.ShellCommands;
using DotCraft.Skills;
using Xunit;

namespace DotCraft.Tests.Skills;

public sealed class SkillManageToolTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "dotcraft-skillmanage-tests", Guid.NewGuid().ToString("N"));
    private readonly SkillsLoader _skillsLoader;
    private readonly SkillManageTool _tool;

    public SkillManageToolTests()
    {
        Directory.CreateDirectory(_tempRoot);
        _skillsLoader = new SkillsLoader(_tempRoot);
        _tool = CreateTool();
    }

    [Fact]
    public async Task Create_WritesValidSkillAndRejectsInvalidOrDuplicateInput()
    {
        var created = await Invoke(() => _tool.SkillManage(SkillManageAction.Create, "demo-skill", content: ValidSkill("demo-skill")));
        Assert.True(created.Success);
        Assert.True(File.Exists(SkillFile("demo-skill")));

        var duplicate = await Invoke(() => _tool.SkillManage(SkillManageAction.Create, "demo-skill", content: ValidSkill("demo-skill")));
        Assert.False(duplicate.Success);
        Assert.Contains("already exists", duplicate.Error);

        var missingFrontmatter = await Invoke(() => _tool.SkillManage(SkillManageAction.Create, "bad-skill", content: "# Bad\n\nMissing frontmatter."));
        Assert.False(missingFrontmatter.Success);
        Assert.Contains("frontmatter", missingFrontmatter.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(_skillsLoader.WorkspaceSkillsPath, "bad-skill")));

        var invalidName = await Invoke(() => _tool.SkillManage(SkillManageAction.Create, "Bad Skill", content: ValidSkill("Bad Skill")));
        Assert.False(invalidName.Success);
        Assert.Contains("Invalid skill name", invalidName.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateAndDelete_RequestSkillApprovalAndHonorTheDecision(bool approved)
    {
        var approval = new RecordingApprovalService(approved);
        var tool = CreateTool(approval);
        await Invoke(() => _tool.SkillManage(SkillManageAction.Create, "existing-skill", content: ValidSkill("existing-skill")));

        var created = await Invoke(() => tool.SkillManage(SkillManageAction.Create, "new-skill", content: ValidSkill("new-skill")));
        var deleted = await Invoke(() => tool.SkillManage(SkillManageAction.Delete, "existing-skill"));

        Assert.Equal(
            [("skill", "create", "new-skill"), ("skill", "delete", "existing-skill")],
            approval.Requests);
        Assert.Equal(approved, created.Success);
        Assert.Equal(approved, deleted.Success);
        Assert.Equal(approved, File.Exists(SkillFile("new-skill")));
        Assert.Equal(!approved, Directory.Exists(Path.Combine(_skillsLoader.WorkspaceSkillsPath, "existing-skill")));
        if (!approved)
        {
            Assert.Contains("rejected by user", created.Error);
            Assert.Contains("rejected by user", deleted.Error);
        }
    }

    [Fact]
    public async Task PatchAndEdit_DoNotRequestApproval()
    {
        await Invoke(() => _tool.SkillManage(SkillManageAction.Create, "edit-skill", content: ValidSkill("edit-skill")));
        var approval = new RecordingApprovalService(approved: false);
        var tool = CreateTool(approval);

        var patched = await Invoke(() => tool.SkillManage(SkillManageAction.Patch, "edit-skill", oldString: "Follow these steps.", newString: "Follow these updated steps."));
        var edited = await Invoke(() => tool.SkillManage(SkillManageAction.Edit, "edit-skill", content: ValidSkill("edit-skill", "Changed workflow.")));

        Assert.True(patched.Success);
        Assert.True(edited.Success);
        Assert.Empty(approval.Requests);
        Assert.Contains("Changed workflow.", File.ReadAllText(SkillFile("edit-skill")));
    }

    [Fact]
    public async Task Patch_RequiresNewStringAndUniqueMatchUnlessReplaceAll()
    {
        await Invoke(() => _tool.SkillManage(SkillManageAction.Create, "patch-skill", content: ValidSkill("patch-skill", "Repeat\nRepeat\n")));

        var missingNew = await Invoke(() => _tool.SkillManage(SkillManageAction.Patch, "patch-skill", oldString: "Repeat"));
        Assert.False(missingNew.Success);
        Assert.Contains("newString is required", missingNew.Error);

        var ambiguous = await Invoke(() => _tool.SkillManage(SkillManageAction.Patch, "patch-skill", oldString: "Repeat", newString: "Done"));
        Assert.False(ambiguous.Success);
        Assert.Contains("matched 2 times", ambiguous.Error);

        var replaced = await Invoke(() => _tool.SkillManage(SkillManageAction.Patch, "patch-skill", oldString: "Repeat", newString: "Done", replaceAll: true));
        Assert.True(replaced.Success);
        Assert.Equal(2, replaced.ReplacementCount);
        Assert.DoesNotContain("Repeat", File.ReadAllText(SkillFile("patch-skill")));
    }

    [Fact]
    public async Task Patch_DoesNotWriteWhenPatchWouldBreakFrontmatter()
    {
        await Invoke(() => _tool.SkillManage(SkillManageAction.Create, "safe-skill", content: ValidSkill("safe-skill")));
        var original = File.ReadAllText(SkillFile("safe-skill"));

        var result = await Invoke(() => _tool.SkillManage(SkillManageAction.Patch, "safe-skill", oldString: "name: safe-skill", newString: ""));

        Assert.False(result.Success);
        Assert.Contains("frontmatter", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllText(SkillFile("safe-skill")));
    }

    [Fact]
    public async Task SupportingFiles_AreLimitedToScriptsAndAssetsInsideTheSkill()
    {
        await Invoke(() => _tool.SkillManage(SkillManageAction.Create, "file-skill", content: ValidSkill("file-skill")));
        var skillDir = Path.Combine(_skillsLoader.WorkspaceSkillsPath, "file-skill");

        var written = await Invoke(() => _tool.SkillManage(SkillManageAction.WriteFile, "file-skill", filePath: "scripts/guide.md", fileContent: "Guide"));
        Assert.True(written.Success);
        Assert.True(File.Exists(Path.Combine(skillDir, "scripts", "guide.md")));

        var unsupported = await Invoke(() => _tool.SkillManage(SkillManageAction.WriteFile, "file-skill", filePath: "references/guide.md", fileContent: "Guide"));
        Assert.False(unsupported.Success);
        Assert.Contains("scripts, assets", unsupported.Error);

        var traversal = await Invoke(() => _tool.SkillManage(SkillManageAction.WriteFile, "file-skill", filePath: "../escape.md", fileContent: "bad"));
        Assert.False(traversal.Success);
        Assert.Contains("Path traversal", traversal.Error);

        var removed = await Invoke(() => _tool.SkillManage(SkillManageAction.RemoveFile, "file-skill", filePath: "scripts/guide.md"));
        Assert.True(removed.Success);
        Assert.False(File.Exists(Path.Combine(skillDir, "scripts", "guide.md")));
    }

    [Fact]
    public async Task Edit_RejectsBuiltInSkill()
    {
        var skillDir = Path.Combine(_skillsLoader.WorkspaceSkillsPath, "builtin-skill");
        Directory.CreateDirectory(skillDir);
        await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"), ValidSkill("builtin-skill"));
        await File.WriteAllTextAsync(Path.Combine(skillDir, ".builtin"), "test");

        var result = await Invoke(() => _tool.SkillManage(SkillManageAction.Edit, "builtin-skill", content: ValidSkill("builtin-skill", "Changed.")));

        Assert.False(result.Success);
        Assert.Contains("builtin", result.Error, StringComparison.OrdinalIgnoreCase);
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

    private string SkillFile(string name) => Path.Combine(_skillsLoader.WorkspaceSkillsPath, name, "SKILL.md");

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

    private SkillManageTool CreateTool(IApprovalService? approvalService = null)
    {
        var config = new AppConfig.SelfLearningConfig
        {
            Enabled = true,
            MaxSkillContentChars = 100_000,
            MaxSupportingFileBytes = 1_048_576
        };

        return new SkillManageTool(
            new WorkspaceFileSkillMutationApplier(_skillsLoader),
            config,
            approvalService);
    }

    private static async Task<SkillMutationResult> Invoke(Func<Task<string>> action)
    {
        var json = await action();
        return JsonSerializer.Deserialize<SkillMutationResult>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private sealed class RecordingApprovalService(bool approved) : IApprovalService
    {
        public List<(string Kind, string Operation, string Target)> Requests { get; } = [];

        public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null) =>
            Task.FromResult(approved);

        public Task<bool> RequestShellApprovalAsync(ShellApprovalRequest request, ApprovalContext? context = null) =>
            Task.FromResult(approved);

        public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null)
        {
            Requests.Add((kind, operation, target));
            return Task.FromResult(approved);
        }
    }
}
