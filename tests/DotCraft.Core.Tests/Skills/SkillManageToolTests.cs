using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Security;
using DotCraft.Skills;

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
    public async Task SkillManage_CreateWithValidContent_WritesWorkspaceSkill()
    {
        var result = await Invoke(() => _tool.SkillManage("create", "demo-skill", content: ValidSkill("demo-skill")));

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(_skillsLoader.WorkspaceSkillsPath, "demo-skill", "SKILL.md")));
    }

    [Fact]
    public async Task SkillManage_CreateWithMissingFrontmatter_ReturnsError()
    {
        var result = await Invoke(() => _tool.SkillManage("create", "bad-skill", content: "# Bad\n\nMissing frontmatter."));

        Assert.False(result.Success);
        Assert.Contains("frontmatter", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(_skillsLoader.WorkspaceSkillsPath, "bad-skill")));
    }

    [Fact]
    public async Task SkillManage_CreateWithInvalidName_ReturnsError()
    {
        var result = await Invoke(() => _tool.SkillManage("create", "Bad Skill", content: ValidSkill("Bad Skill")));

        Assert.False(result.Success);
        Assert.Contains("Invalid skill name", result.Error);
    }

    [Fact]
    public async Task SkillManage_CreateWithDuplicateName_ReturnsError()
    {
        await Invoke(() => _tool.SkillManage("create", "demo-skill", content: ValidSkill("demo-skill")));

        var result = await Invoke(() => _tool.SkillManage("create", "demo-skill", content: ValidSkill("demo-skill")));

        Assert.False(result.Success);
        Assert.Contains("already exists", result.Error);
    }

    [Fact]
    public async Task Create_RequestsSkillApproval_AndExecutesOnAccept()
    {
        var approval = new RecordingApprovalService(approved: true);
        var tool = CreateTool(approval);

        var result = await Invoke(() => tool.SkillManage("create", "approved-skill", content: ValidSkill("approved-skill")));

        Assert.True(result.Success);
        Assert.Equal(("skill", "create", "approved-skill"), Assert.Single(approval.Requests));
        Assert.True(File.Exists(Path.Combine(_skillsLoader.WorkspaceSkillsPath, "approved-skill", "SKILL.md")));
    }

    [Fact]
    public async Task Create_RejectedApproval_DoesNotWriteSkill_ReturnsRejectionMessage()
    {
        var approval = new RecordingApprovalService(approved: false);
        var tool = CreateTool(approval);

        var result = await Invoke(() => tool.SkillManage("create", "rejected-skill", content: ValidSkill("rejected-skill")));

        Assert.False(result.Success);
        Assert.Contains("rejected by user", result.Error);
        Assert.Equal(("skill", "create", "rejected-skill"), Assert.Single(approval.Requests));
        Assert.False(Directory.Exists(Path.Combine(_skillsLoader.WorkspaceSkillsPath, "rejected-skill")));
    }

    [Fact]
    public async Task SkillManage_UnknownAction_ReturnsError()
    {
        var result = await Invoke(() => _tool.SkillManage("unknown", "demo-skill"));

        Assert.False(result.Success);
        Assert.Contains("Unknown action", result.Error);
    }

    [Fact]
    public async Task SkillManage_PatchRequiresOldAndNewStrings()
    {
        await Invoke(() => _tool.SkillManage("create", "patch-skill", content: ValidSkill("patch-skill")));

        var result = await Invoke(() => _tool.SkillManage("patch", "patch-skill", oldString: "Follow"));

        Assert.False(result.Success);
        Assert.Contains("newString is required", result.Error);
    }

    [Fact]
    public async Task SkillManage_PatchRequiresUniqueMatchUnlessReplaceAll()
    {
        await Invoke(() => _tool.SkillManage("create", "patch-skill", content: ValidSkill("patch-skill", "Repeat\nRepeat\n")));

        var result = await Invoke(() => _tool.SkillManage("patch", "patch-skill", oldString: "Repeat", newString: "Done"));

        Assert.False(result.Success);
        Assert.Contains("matched 2 times", result.Error);
    }

    [Fact]
    public async Task SkillManage_PatchWithReplaceAll_UpdatesAllMatches()
    {
        await Invoke(() => _tool.SkillManage("create", "patch-skill", content: ValidSkill("patch-skill", "Repeat\nRepeat\n")));

        var result = await Invoke(() => _tool.SkillManage("patch", "patch-skill", oldString: "Repeat", newString: "Done", replaceAll: true));
        var content = File.ReadAllText(Path.Combine(_skillsLoader.WorkspaceSkillsPath, "patch-skill", "SKILL.md"));

        Assert.True(result.Success);
        Assert.DoesNotContain("Repeat", content);
        Assert.Equal(2, result.ReplacementCount);
    }

    [Fact]
    public async Task Patch_DoesNotRequestApproval()
    {
        await Invoke(() => _tool.SkillManage("create", "patch-skill", content: ValidSkill("patch-skill")));
        var approval = new RecordingApprovalService(approved: false);
        var tool = CreateTool(approval);

        var result = await Invoke(() => tool.SkillManage("patch", "patch-skill", oldString: "Follow these steps.", newString: "Follow these updated steps."));

        Assert.True(result.Success);
        Assert.Empty(approval.Requests);
    }

    [Fact]
    public async Task SkillManage_Edit_ReplacesSkillContent()
    {
        await Invoke(() => _tool.SkillManage("create", "edit-skill", content: ValidSkill("edit-skill")));

        var result = await Invoke(() => _tool.SkillManage("edit", "edit-skill", content: ValidSkill("edit-skill", "Changed workflow.")));
        var content = File.ReadAllText(Path.Combine(_skillsLoader.WorkspaceSkillsPath, "edit-skill", "SKILL.md"));

        Assert.True(result.Success);
        Assert.Contains("Changed workflow.", content);
    }

    [Fact]
    public async Task Edit_DoesNotRequestApproval()
    {
        await Invoke(() => _tool.SkillManage("create", "edit-skill", content: ValidSkill("edit-skill")));
        var approval = new RecordingApprovalService(approved: false);
        var tool = CreateTool(approval);

        var result = await Invoke(() => tool.SkillManage("edit", "edit-skill", content: ValidSkill("edit-skill", "Changed workflow.")));

        Assert.True(result.Success);
        Assert.Empty(approval.Requests);
    }

    [Fact]
    public async Task SkillManage_WriteFile_WritesSupportingFile()
    {
        await Invoke(() => _tool.SkillManage("create", "file-skill", content: ValidSkill("file-skill")));

        var result = await Invoke(() => _tool.SkillManage("write_file", "file-skill", filePath: "scripts/guide.md", fileContent: "Guide"));

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(_skillsLoader.WorkspaceSkillsPath, "file-skill", "scripts", "guide.md")));
    }

    [Theory]
    [InlineData("references/guide.md")]
    [InlineData("templates/template.md")]
    public async Task SkillManage_WriteFileRejectsUnsupportedSupportingPath(string filePath)
    {
        await Invoke(() => _tool.SkillManage("create", "file-skill", content: ValidSkill("file-skill")));

        var result = await Invoke(() => _tool.SkillManage("write_file", "file-skill", filePath: filePath, fileContent: "Guide"));

        Assert.False(result.Success);
        Assert.Contains("scripts, assets", result.Error);
    }

    [Fact]
    public async Task SkillManage_WriteFileRejectsTraversal()
    {
        await Invoke(() => _tool.SkillManage("create", "file-skill", content: ValidSkill("file-skill")));

        var result = await Invoke(() => _tool.SkillManage("write_file", "file-skill", filePath: "../escape.md", fileContent: "bad"));

        Assert.False(result.Success);
        Assert.Contains("Path traversal", result.Error);
    }

    [Fact]
    public async Task SkillManage_RemoveFile_RemovesSupportingFile()
    {
        await Invoke(() => _tool.SkillManage("create", "file-skill", content: ValidSkill("file-skill")));
        await Invoke(() => _tool.SkillManage("write_file", "file-skill", filePath: "assets/guide.md", fileContent: "Guide"));

        var result = await Invoke(() => _tool.SkillManage("remove_file", "file-skill", filePath: "assets/guide.md"));

        Assert.True(result.Success);
        Assert.False(File.Exists(Path.Combine(_skillsLoader.WorkspaceSkillsPath, "file-skill", "assets", "guide.md")));
    }

    [Fact]
    public async Task SkillManage_Delete_RemovesWorkspaceSkill()
    {
        await Invoke(() => _tool.SkillManage("create", "delete-skill", content: ValidSkill("delete-skill")));

        var result = await Invoke(() => _tool.SkillManage("delete", "delete-skill"));

        Assert.True(result.Success);
        Assert.False(Directory.Exists(Path.Combine(_skillsLoader.WorkspaceSkillsPath, "delete-skill")));
    }

    [Fact]
    public async Task Delete_RequestsSkillApproval_AndExecutesOnAccept()
    {
        await Invoke(() => _tool.SkillManage("create", "delete-skill", content: ValidSkill("delete-skill")));
        var approval = new RecordingApprovalService(approved: true);
        var tool = CreateTool(approval);

        var result = await Invoke(() => tool.SkillManage("delete", "delete-skill"));

        Assert.True(result.Success);
        Assert.Equal(("skill", "delete", "delete-skill"), Assert.Single(approval.Requests));
        Assert.False(Directory.Exists(Path.Combine(_skillsLoader.WorkspaceSkillsPath, "delete-skill")));
    }

    [Fact]
    public async Task Delete_RejectedApproval_KeepsSkillOnDisk_ReturnsRejectionMessage()
    {
        await Invoke(() => _tool.SkillManage("create", "delete-skill", content: ValidSkill("delete-skill")));
        var approval = new RecordingApprovalService(approved: false);
        var tool = CreateTool(approval);

        var result = await Invoke(() => tool.SkillManage("delete", "delete-skill"));

        Assert.False(result.Success);
        Assert.Contains("rejected by user", result.Error);
        Assert.Equal(("skill", "delete", "delete-skill"), Assert.Single(approval.Requests));
        Assert.True(Directory.Exists(Path.Combine(_skillsLoader.WorkspaceSkillsPath, "delete-skill")));
    }

    [Fact]
    public async Task SkillManage_EditRejectsBuiltInSkill()
    {
        var skillDir = Path.Combine(_skillsLoader.WorkspaceSkillsPath, "builtin-skill");
        Directory.CreateDirectory(skillDir);
        await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"), ValidSkill("builtin-skill"));
        await File.WriteAllTextAsync(Path.Combine(skillDir, ".builtin"), "test");

        var result = await Invoke(() => _tool.SkillManage("edit", "builtin-skill", content: ValidSkill("builtin-skill", "Changed.")));

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

        public Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null) =>
            Task.FromResult(approved);

        public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null)
        {
            Requests.Add((kind, operation, target));
            return Task.FromResult(approved);
        }
    }
}
