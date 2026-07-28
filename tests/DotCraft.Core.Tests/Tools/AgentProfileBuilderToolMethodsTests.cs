using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Tools;

namespace DotCraft.Tests.Tools;

public sealed class AgentProfileBuilderToolMethodsTests
{
    private static string NewThread() => $"builder-thread-{Guid.NewGuid():N}";

    private static AgentProfileBuilderToolMethods Seed(string threadId, string markdown = "")
    {
        ProfileBuilderDraftStore.Seed(threadId, "draft-agent", "workspace", markdown);
        // Null catalogs => skill/MCP validation accepts names as-is (no catalog available).
        return new AgentProfileBuilderToolMethods(threadId, skillsLoader: null, mcpClientManager: null);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void SetAgentName_UpdatesDraftAndReportsField()
    {
        var threadId = NewThread();
        var methods = Seed(threadId);

        var result = Parse(methods.SetAgentName("  triage-bot  "));

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal("name", result.GetProperty("field").GetString());
        Assert.Equal("set", result.GetProperty("change").GetProperty("op").GetString());

        var draft = AgentProfileDraftEditor.Parse(ProfileBuilderDraftStore.TryGet(threadId)!.Markdown);
        Assert.Equal("triage-bot", draft.Name);

        ProfileBuilderDraftStore.Remove(threadId);
    }

    [Fact]
    public void AddAgentTools_AcceptsKnownToolAndRejectsUnknown()
    {
        var threadId = NewThread();
        var methods = Seed(threadId);
        var knownTool = BuiltInToolCatalog.Enumerate()[0].Name;

        var result = Parse(methods.AddAgentTools([knownTool, "TotallyNotARealTool"]));
        var change = result.GetProperty("change");

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal("tools.allow", result.GetProperty("field").GetString());
        Assert.Equal(knownTool, change.GetProperty("values")[0].GetString());
        Assert.Equal("TotallyNotARealTool", change.GetProperty("rejected")[0].GetString());

        var draft = AgentProfileDraftEditor.Parse(ProfileBuilderDraftStore.TryGet(threadId)!.Markdown);
        Assert.Equal([knownTool], draft.ToolsAllow);

        ProfileBuilderDraftStore.Remove(threadId);
    }

    [Fact]
    public void RemoveAgentTools_RemovesFromAllowList()
    {
        var threadId = NewThread();
        var knownTool = BuiltInToolCatalog.Enumerate()[0].Name;
        var methods = Seed(threadId);
        methods.AddAgentTools([knownTool]);

        var result = Parse(methods.RemoveAgentTools([knownTool]));

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal(knownTool, result.GetProperty("change").GetProperty("values")[0].GetString());

        var draft = AgentProfileDraftEditor.Parse(ProfileBuilderDraftStore.TryGet(threadId)!.Markdown);
        Assert.Empty(draft.ToolsAllow);

        ProfileBuilderDraftStore.Remove(threadId);
    }

    [Fact]
    public void SetAgentToolControl_RejectsInvalidValue()
    {
        var threadId = NewThread();
        var methods = Seed(threadId);

        var result = Parse(methods.SetAgentToolControl("bogus"));

        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Equal("tools.agentControl", result.GetProperty("field").GetString());

        ProfileBuilderDraftStore.Remove(threadId);
    }

    [Fact]
    public void SetAgentApproval_RejectsInvalidPolicy_AndAcceptsValid()
    {
        var threadId = NewThread();
        var methods = Seed(threadId);

        Assert.False(Parse(methods.SetAgentApproval("nope")).GetProperty("ok").GetBoolean());

        var ok = Parse(methods.SetAgentApproval("interrupt", requireApprovalOutsideWorkspace: true));
        Assert.True(ok.GetProperty("ok").GetBoolean());

        var draft = AgentProfileDraftEditor.Parse(ProfileBuilderDraftStore.TryGet(threadId)!.Markdown);
        Assert.Equal("interrupt", draft.ApprovalPolicy);
        Assert.True(draft.RequireApprovalOutsideWorkspace);

        ProfileBuilderDraftStore.Remove(threadId);
    }

    [Fact]
    public void SetAgentProviderPreference_SetsCompletePreference()
    {
        var threadId = NewThread();
        var methods = Seed(threadId);

        var result = Parse(methods.SetAgentProviderPreference(
            providerId: "openai",
            model: "gpt-5.6",
            reasoningEnabled: true,
            reasoningEffort: "high",
            speed: "fast",
            contextWindowMode: "max"));

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal("providerPreference", result.GetProperty("field").GetString());
        var changedPreference = result.GetProperty("change").GetProperty("providerPreference");
        Assert.Equal("openai", changedPreference.GetProperty("providerId").GetString());
        Assert.Equal("gpt-5.6", changedPreference.GetProperty("model").GetString());
        Assert.True(changedPreference.GetProperty("reasoning").GetProperty("enabled").GetBoolean());
        Assert.Equal("high", changedPreference.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.False(changedPreference.GetProperty("reasoning").TryGetProperty("output", out _));
        Assert.Equal("fast", changedPreference.GetProperty("speed").GetString());
        Assert.Equal("max", changedPreference.GetProperty("contextWindow").GetProperty("mode").GetString());
        var draft = AgentProfileDraftEditor.Parse(ProfileBuilderDraftStore.TryGet(threadId)!.Markdown);
        Assert.True(draft.HasProviderPreference);
        Assert.Equal("openai", draft.ProviderId);
        Assert.Equal("gpt-5.6", draft.Model);
        Assert.True(draft.ReasoningEnabled);
        Assert.Equal("high", draft.ReasoningEffort);
        Assert.Equal("fast", draft.Speed);
        Assert.Equal("max", draft.ContextWindowMode);

        ProfileBuilderDraftStore.Remove(threadId);
    }

    [Fact]
    public void SetAgentProviderPreference_RejectsInvalidCompletePreference()
    {
        var threadId = NewThread();
        var methods = Seed(threadId);

        var result = Parse(methods.SetAgentProviderPreference(
            providerId: "openai",
            model: "gpt-5.6",
            reasoningEnabled: false,
            reasoningEffort: "",
            speed: "standard",
            contextWindowMode: "default"));

        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Equal("providerPreference", result.GetProperty("field").GetString());

        ProfileBuilderDraftStore.Remove(threadId);
    }

    [Fact]
    public void ClearAgentProviderPreference_RemovesPreference()
    {
        var threadId = NewThread();
        var methods = Seed(threadId);
        _ = methods.SetAgentProviderPreference(
            providerId: "openai",
            model: "gpt-5.6",
            reasoningEnabled: false,
            reasoningEffort: "medium",
            speed: "standard",
            contextWindowMode: "default");

        var result = Parse(methods.ClearAgentProviderPreference());

        Assert.True(result.GetProperty("ok").GetBoolean());
        var draft = AgentProfileDraftEditor.Parse(ProfileBuilderDraftStore.TryGet(threadId)!.Markdown);
        Assert.False(draft.HasProviderPreference);
        Assert.DoesNotContain("providerPreference:", ProfileBuilderDraftStore.TryGet(threadId)!.Markdown);

        ProfileBuilderDraftStore.Remove(threadId);
    }

    [Fact]
    public async Task AddAgentSkills_WithoutCatalog_AcceptsNames()
    {
        var threadId = NewThread();
        var methods = Seed(threadId);

        var result = Parse(await methods.AddAgentSkills(["pdf", "docx"]));

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal("skills.preload", result.GetProperty("field").GetString());

        var draft = AgentProfileDraftEditor.Parse(ProfileBuilderDraftStore.TryGet(threadId)!.Markdown);
        Assert.Equal(["pdf", "docx"], draft.SkillsPreload);

        ProfileBuilderDraftStore.Remove(threadId);
    }

    [Fact]
    public void AppendAgentInstructions_AppendsToExistingBody()
    {
        var threadId = NewThread();
        var methods = Seed(threadId);
        methods.SetAgentInstructions("First paragraph.");

        var result = Parse(methods.AppendAgentInstructions("Second paragraph."));

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal("append", result.GetProperty("change").GetProperty("op").GetString());

        var draft = AgentProfileDraftEditor.Parse(ProfileBuilderDraftStore.TryGet(threadId)!.Markdown);
        Assert.Equal("First paragraph.\n\nSecond paragraph.", draft.RoleInstructions);

        ProfileBuilderDraftStore.Remove(threadId);
    }

    [Fact]
    public void Mutate_OnNonBuilderThread_ReturnsError()
    {
        var methods = new AgentProfileBuilderToolMethods("never-seeded-thread", null, null);

        var result = Parse(methods.SetAgentName("x"));

        Assert.False(result.GetProperty("ok").GetBoolean());
    }
}
