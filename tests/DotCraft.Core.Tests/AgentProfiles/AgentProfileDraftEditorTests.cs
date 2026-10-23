using DotCraft.Agents;
using Xunit;

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
        }
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var draft = new AgentProfileDraft
        {
            Name = "release-notes-writer",
            Description = "Drafts release notes from merged PRs",
            HasProviderPreference = true,
            ProviderId = "anthropic",
            Model = "claude-opus-4-8",
            ReasoningEnabled = true,
            ReasoningEffort = "high",
            Speed = "fast",
            ToolPolicyMode = "denyList",
            ToolsDeny = ["DeleteFile"],
            AgentControl = "allowList",
            McpServers = ["github"],
            McpToolsAllow = ["search"],
            McpToolsDeny = ["delete"],
            SkillsAllow = ["xlsx"],
            SkillsDeny = ["pptx"],
            ApprovalPolicy = "deny",
            RequireApprovalOutsideWorkspace = true,
            Memory = "own",
            RoleInstructions = "You write crisp, accurate release notes."
        };

        var roundTripped = AgentProfileDraftEditor.Parse(AgentProfileDraftEditor.ToMarkdown(draft));

        Assert.Equal(draft.Name, roundTripped.Name);
        Assert.Equal(draft.Description, roundTripped.Description);
        Assert.Equal(draft.HasProviderPreference, roundTripped.HasProviderPreference);
        Assert.Equal(draft.ProviderId, roundTripped.ProviderId);
        Assert.Equal(draft.Model, roundTripped.Model);
        Assert.Equal(draft.ReasoningEnabled, roundTripped.ReasoningEnabled);
        Assert.Equal(draft.ReasoningEffort, roundTripped.ReasoningEffort);
        Assert.Equal(draft.Speed, roundTripped.Speed);
        Assert.Equal(draft.ToolPolicyMode, roundTripped.ToolPolicyMode);
        Assert.Equal(draft.ToolsDeny, roundTripped.ToolsDeny);
        Assert.Equal(draft.AgentControl, roundTripped.AgentControl);
        Assert.Equal(draft.McpServers, roundTripped.McpServers);
        Assert.Equal(draft.McpToolsAllow, roundTripped.McpToolsAllow);
        Assert.Equal(draft.McpToolsDeny, roundTripped.McpToolsDeny);
        Assert.Equal(draft.SkillsAllow, roundTripped.SkillsAllow);
        Assert.Equal(draft.SkillsDeny, roundTripped.SkillsDeny);
        Assert.Equal(draft.ApprovalPolicy, roundTripped.ApprovalPolicy);
        Assert.True(roundTripped.RequireApprovalOutsideWorkspace);
        Assert.Equal(draft.Memory, roundTripped.Memory);
        Assert.Equal(draft.RoleInstructions, roundTripped.RoleInstructions);
    }

    [Fact]
    public void Parse_ReadsListsInAnyYamlLayout()
    {
        const string markdown = """
            ---
            name: "Research Agent"
            tools:
                allow:
                    [
                        ReadFile,
                        "WebSearch"
                    ]
                agentControl: disabled
            skills:
                allow:
                    - research
            ---
            Body
            """;

        var draft = AgentProfileDraftEditor.Parse(markdown);

        Assert.Equal("Research Agent", draft.Name);
        Assert.Equal("allowList", draft.ToolPolicyMode);
        Assert.Equal(["ReadFile", "WebSearch"], draft.ToolsAllow);
        Assert.Equal("disabled", draft.AgentControl);
        Assert.Equal(["research"], draft.SkillsAllow);
        Assert.Equal("Body", draft.RoleInstructions);
    }

    [Theory]
    [InlineData("all", null)]
    [InlineData("allowList", "  allow: []")]
    [InlineData("denyList", "  deny: []")]
    public void ToolPolicyMode_RoundTripsIncludingExplicitEmptyLists(string mode, string? expectedLine)
    {
        var draft = new AgentProfileDraft
        {
            Name = "tool-policy",
            Description = "Tool policy",
            ToolPolicyMode = mode
        };

        var markdown = AgentProfileDraftEditor.ToMarkdown(draft);
        var roundTripped = AgentProfileDraftEditor.Parse(markdown);

        Assert.Equal(mode, roundTripped.ToolPolicyMode);
        if (expectedLine == null)
            Assert.DoesNotContain("tools:", markdown);
        else
            Assert.Contains(expectedLine, markdown);
    }

    [Fact]
    public void AddTo_DedupesAndReportsOnlyNewItems()
    {
        var list = new List<string> { "ReadFile" };

        var added = AgentProfileDraftEditor.AddTo(list, ["ReadFile", "WriteFile", " WriteFile ", ""]);

        Assert.Equal(["WriteFile"], added);
        Assert.Equal(["ReadFile", "WriteFile"], list);
    }

    [Theory]
    [InlineData("autoApprove", true)]
    [InlineData("restricted", false)]
    public void IsApprovalPolicy_ValidatesAgainstKnownSet(string value, bool expected) =>
        Assert.Equal(expected, AgentProfileDraftEditor.IsApprovalPolicy(value));

    [Fact]
    public void EditorOutput_IsAcceptedByAgentProfileStore()
    {
        var craftPath = Path.Combine(_root, "workspace", ".craft");
        Directory.CreateDirectory(Path.Combine(craftPath, "agents"));

        var draft = new AgentProfileDraft
        {
            Name = "doc-writer",
            Description = "Writes documentation",
            ToolPolicyMode = "allowList",
            ToolsAllow = ["ReadFile"],
            SkillsAllow = ["docx"],
            ApprovalPolicy = "deny",
            Memory = "own",
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
