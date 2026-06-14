using DotCraft.Agents;

namespace DotCraft.Tests.Agents;

public sealed class AgentProfileDraftEditorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"agent_draft_editor_{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void Parse_NullOrEmpty_YieldsDefaults()
    {
        var draft = AgentProfileDraftEditor.Parse(null);

        Assert.Equal(string.Empty, draft.Name);
        Assert.Equal("inherit", draft.Model);
        Assert.Equal("medium", draft.ReasoningEffort);
        Assert.Equal("full", draft.AgentControl);
        Assert.Equal("default", draft.ApprovalPolicy);
        Assert.False(draft.RequireApprovalOutsideWorkspace);
        Assert.Empty(draft.ToolsAllow);
    }

    [Fact]
    public void Parse_BodyWithoutFrontmatter_BecomesRoleInstructions()
    {
        var draft = AgentProfileDraftEditor.Parse("Just a role description, no frontmatter.");

        Assert.Equal("Just a role description, no frontmatter.", draft.RoleInstructions);
        Assert.Equal(string.Empty, draft.Name);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var draft = new AgentProfileDraft
        {
            Name = "release-notes-writer",
            Description = "Drafts release notes from merged PRs",
            Model = "claude-opus-4-8",
            ReasoningEffort = "high",
            ToolsAllow = ["ReadFile", "RunShellCommand"],
            ToolsDeny = ["DeleteFile"],
            AgentControl = "allowList",
            McpServers = ["github"],
            McpToolsAllow = ["search"],
            McpToolsDeny = ["delete"],
            SkillsPreload = ["pdf", "docx"],
            SkillsAllow = ["xlsx"],
            SkillsDeny = ["pptx"],
            ApprovalPolicy = "interrupt",
            RequireApprovalOutsideWorkspace = true,
            RoleInstructions = "You write crisp, accurate release notes."
        };

        var roundTripped = AgentProfileDraftEditor.Parse(AgentProfileDraftEditor.ToMarkdown(draft));

        Assert.Equal(draft.Name, roundTripped.Name);
        Assert.Equal(draft.Description, roundTripped.Description);
        Assert.Equal(draft.Model, roundTripped.Model);
        Assert.Equal(draft.ReasoningEffort, roundTripped.ReasoningEffort);
        Assert.Equal(draft.ToolsAllow, roundTripped.ToolsAllow);
        Assert.Equal(draft.ToolsDeny, roundTripped.ToolsDeny);
        Assert.Equal(draft.AgentControl, roundTripped.AgentControl);
        Assert.Equal(draft.McpServers, roundTripped.McpServers);
        Assert.Equal(draft.McpToolsAllow, roundTripped.McpToolsAllow);
        Assert.Equal(draft.McpToolsDeny, roundTripped.McpToolsDeny);
        Assert.Equal(draft.SkillsPreload, roundTripped.SkillsPreload);
        Assert.Equal(draft.SkillsAllow, roundTripped.SkillsAllow);
        Assert.Equal(draft.SkillsDeny, roundTripped.SkillsDeny);
        Assert.Equal(draft.ApprovalPolicy, roundTripped.ApprovalPolicy);
        Assert.True(roundTripped.RequireApprovalOutsideWorkspace);
        Assert.Equal(draft.RoleInstructions, roundTripped.RoleInstructions);
    }

    [Fact]
    public void ToMarkdown_OmitsDefaultSectionsButAlwaysEmitsPermissions()
    {
        var draft = new AgentProfileDraft { Name = "minimal", Description = "A minimal agent" };

        var md = AgentProfileDraftEditor.ToMarkdown(draft);

        Assert.Contains("name: minimal", md);
        Assert.Contains("model: inherit", md);
        Assert.DoesNotContain("reasoning:", md);
        Assert.DoesNotContain("tools:", md);
        Assert.DoesNotContain("mcp:", md);
        Assert.Contains("permissions:", md);
        Assert.Contains("approvalPolicy: default", md);
    }

    [Fact]
    public void AddTo_DedupesAndReportsOnlyNewItems()
    {
        var list = new List<string> { "ReadFile" };

        var added = AgentProfileDraftEditor.AddTo(list, ["ReadFile", "WriteFile", " WriteFile ", ""]);

        Assert.Equal(["WriteFile"], added);
        Assert.Equal(["ReadFile", "WriteFile"], list);
    }

    [Fact]
    public void RemoveFrom_ReportsOnlyItemsActuallyRemoved()
    {
        var list = new List<string> { "ReadFile", "WriteFile" };

        var removed = AgentProfileDraftEditor.RemoveFrom(list, ["WriteFile", "Missing"]);

        Assert.Equal(["WriteFile"], removed);
        Assert.Equal(["ReadFile"], list);
    }

    [Theory]
    [InlineData("default", true)]
    [InlineData("autoApprove", true)]
    [InlineData("interrupt", true)]
    [InlineData("restricted", false)]
    [InlineData("nonsense", false)]
    public void IsApprovalPolicy_ValidatesAgainstKnownSet(string value, bool expected) =>
        Assert.Equal(expected, AgentProfileDraftEditor.IsApprovalPolicy(value));

    [Fact]
    public void EditorOutput_IsAcceptedByAgentProfileStore()
    {
        // The conversational editor must emit YAML the real profile store can parse and validate.
        var craftPath = Path.Combine(_root, "workspace", ".craft");
        Directory.CreateDirectory(Path.Combine(craftPath, "agents"));

        var draft = new AgentProfileDraft
        {
            Name = "doc-writer",
            Description = "Writes documentation",
            ToolsAllow = ["ReadFile"],
            SkillsPreload = ["docx"],
            ApprovalPolicy = "interrupt",
            RoleInstructions = "You write clear documentation."
        };
        File.WriteAllText(
            Path.Combine(craftPath, "agents", "doc-writer.md"),
            AgentProfileDraftEditor.ToMarkdown(draft));

        var store = new AgentProfileStore(craftPath);
        var entry = store.Read("doc-writer");

        Assert.True(entry.Valid, string.Join("; ", entry.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        Assert.Equal("Writes documentation", entry.Description);
        Assert.NotNull(entry.CompiledConfiguration);
        Assert.Contains("ReadFile", entry.CompiledConfiguration!.ToolPolicy?.Allow ?? []);
    }
}
