using DotCraft.Agents;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Agents;

public sealed class AgentProfileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"agent_profile_store_{Guid.NewGuid():N}");
    private readonly string _workspaceCraftPath;
    private readonly string _userCraftPath;

    public AgentProfileStoreTests()
    {
        _workspaceCraftPath = Path.Combine(_root, "workspace", ".craft");
        _userCraftPath = Path.Combine(_root, "user", ".craft");
        Directory.CreateDirectory(Path.Combine(_workspaceCraftPath, "agents"));
        Directory.CreateDirectory(Path.Combine(_workspaceCraftPath, "managed", "agent-profiles"));
        Directory.CreateDirectory(Path.Combine(_userCraftPath, "agents"));
    }

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
    public void List_UsesSourcePriorityAndMarksShadowedProfiles()
    {
        File.WriteAllText(
            Path.Combine(_userCraftPath, "agents", "team-reviewer.md"),
            ValidProfile("team-reviewer", "User reviewer"));
        File.WriteAllText(
            Path.Combine(_workspaceCraftPath, "agents", "team-reviewer.md"),
            ValidProfile("team-reviewer", "Workspace reviewer"));

        var store = new AgentProfileStore(_workspaceCraftPath, _userCraftPath);
        var entries = store.List();
        var effective = store.Read("team-reviewer");

        Assert.Equal(AgentProfileSources.Workspace, effective.Source);
        Assert.Equal("Workspace reviewer", effective.Description);

        var userEntry = entries.Single(entry =>
            entry.Id == "team-reviewer"
            && entry.Source == AgentProfileSources.User);
        Assert.True(userEntry.Shadowed);
        Assert.Equal(AgentProfileSources.Workspace, userEntry.ShadowedBy);

        var builtInEntry = entries.Single(entry =>
            entry.Id == "team-reviewer"
            && entry.Source == AgentProfileSources.BuiltIn);
        Assert.True(builtInEntry.Shadowed);
    }

    [Fact]
    public void ValidateRaw_CatchesRequiredUnknownAndPolicyErrors()
    {
        var store = new AgentProfileStore(_workspaceCraftPath, _userCraftPath);
        var result = store.ValidateRaw(
            """
---
name: bad profile!
surprise: true
tools:
  agentControl: root
---

Body
""",
            AgentProfileSources.Workspace);

        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, d => d.Code == "MissingRequiredField");
        Assert.Contains(result.Diagnostics, d => d.Code == "UnsupportedField");
        Assert.Contains(result.Diagnostics, d => d.Code == "InvalidProfileId");
        Assert.Contains(result.Diagnostics, d => d.Code == "InvalidPolicyValue");
    }

    [Fact]
    public void ValidateRaw_CompilesMarkdownProfileToThreadConfiguration()
    {
        var store = new AgentProfileStore(_workspaceCraftPath, _userCraftPath);
        var result = store.ValidateRaw(
            """
---
name: reviewer-lite
description: Read-only reviewer
avatar: 554
providerId: profile-provider
model: inherit
reasoning:
  effort: high
tools:
  allow: [ReadFile, FindFiles]
  deny: [WriteFile]
  agentControl: allowList
  allowedAgentControlTools: [spawn_agent]
mcp:
  servers: [github-readonly]
  tools:
    allow: [mcp__github-readonly__get_*]
plugins:
  deny: [dangerous-plugin]
skills:
  preload: [code-review]
  allowManage: false
permissions:
  approvalPolicy: interrupt
  requireApprovalOutsideWorkspace: true
teams:
  reservedTools: keep
---

Focus on correctness.
""",
            AgentProfileSources.Workspace);

        Assert.True(result.Valid);
        Assert.Equal(AgentProfileAvatarCodec.Encode(10, 2, 4), result.Avatar);
        var config = Assert.IsType<ThreadConfiguration>(result.CompiledConfiguration);
        Assert.Equal("reviewer-lite", config.AgentProfileId);
        Assert.Equal(AgentProfileSources.Workspace, config.AgentProfileSource);
        Assert.StartsWith("sha256:", config.AgentProfileFingerprint);
        Assert.Equal("profile-provider", config.ProviderId);
        Assert.Null(config.Model);
        Assert.NotNull(config.Reasoning);
        Assert.True(config.Reasoning.Enabled);
        Assert.Equal(ReasoningEffort.High, config.Reasoning.Effort);
        Assert.NotNull(config.ToolPolicy);
        var toolPolicy = config.ToolPolicy!;
        Assert.Equal(new[] { "ReadFile", "FindFiles" }, toolPolicy.Allow ?? Array.Empty<string>());
        Assert.Equal(new[] { "WriteFile" }, toolPolicy.Deny ?? Array.Empty<string>());
        Assert.Equal("allowList", toolPolicy.AgentControl);
        Assert.NotNull(config.McpPolicy);
        var mcpPolicy = config.McpPolicy!;
        Assert.Equal(new[] { "github-readonly" }, mcpPolicy.Servers ?? Array.Empty<string>());
        Assert.NotNull(config.PluginPolicy);
        var pluginPolicy = config.PluginPolicy!;
        Assert.Equal(new[] { "dangerous-plugin" }, pluginPolicy.Deny ?? Array.Empty<string>());
        Assert.NotNull(config.SkillsPolicy);
        var skillsPolicy = config.SkillsPolicy!;
        Assert.Equal(new[] { "code-review" }, skillsPolicy.Preload ?? Array.Empty<string>());
        Assert.False(skillsPolicy.AllowManage);
        Assert.Equal(ApprovalPolicy.Interrupt, config.ApprovalPolicy);
        Assert.True(config.RequireApprovalOutsideWorkspace);
        Assert.Equal("keep", config.TeamsPolicy?.ReservedTools);
        Assert.Equal("Focus on correctness.", config.RoleInstructions);
    }

    [Fact]
    public void Read_UsesManagedPriorityAndAppliesLockedFields()
    {
        File.WriteAllText(
            Path.Combine(_workspaceCraftPath, "agents", "governed.md"),
            ValidProfile("governed", "Workspace governed profile"));
        File.WriteAllText(
            Path.Combine(_workspaceCraftPath, "managed", "agent-profiles", "governed.md"),
            """
---
name: governed
description: Managed governed profile
model: inherit
mcp:
  servers: [approved, extra]
permissions:
  approvalPolicy: autoApprove
locked:
  tools:
    deny: [Exec]
  mcp:
    servers: [approved]
  permissions:
    deniedApprovalPolicies: [autoApprove]
  overrideBasePrompt: false
---

Managed instructions.
""");

        var store = new AgentProfileStore(_workspaceCraftPath, _userCraftPath);
        var profile = store.Read("governed");

        Assert.Equal(AgentProfileSources.Managed, profile.Source);
        Assert.True(profile.ReadOnly);
        Assert.Contains("tools.deny", profile.LockedFields);
        Assert.Contains("mcp.servers", profile.LockedFields);
        Assert.Contains("permissions.approvalPolicy", profile.LockedFields);
        Assert.Contains(profile.Diagnostics, diagnostic => diagnostic.Code == "LockedFieldConflict");
        var config = Assert.IsType<ThreadConfiguration>(profile.CompiledConfiguration);
        Assert.Equal(ApprovalPolicy.Default, config.ApprovalPolicy);
        Assert.Equal(["Exec"], config.ToolDenyList ?? Array.Empty<string>());
        Assert.Equal(["approved"], config.McpPolicy?.Servers ?? Array.Empty<string>());

        var workspace = store.List().Single(entry => entry.Id == "governed" && entry.Source == AgentProfileSources.Workspace);
        Assert.True(workspace.Shadowed);
        Assert.Equal(AgentProfileSources.Managed, workspace.ShadowedBy);
    }

    [Fact]
    public void PluginProfiles_AreReadOnlyAndTrustRestricted()
    {
        var profileDir = Path.Combine(_workspaceCraftPath, "plugins", "sample-plugin", "agent-profiles");
        Directory.CreateDirectory(profileDir);
        File.WriteAllText(
            Path.Combine(profileDir, "plugin-worker.md"),
            """
---
name: plugin-worker
description: Plugin worker profile
model: inherit
tools:
  allow: [ReadFile, Exec]
  agentControl: full
mcp:
  servers: [plugin-server]
skills:
  allowManage: true
permissions:
  approvalPolicy: autoApprove
---

Plugin instructions.
""");

        var store = new AgentProfileStore(_workspaceCraftPath, _userCraftPath);
        var profile = store.Read("plugin-worker");

        Assert.Equal(AgentProfileSources.Plugin, profile.Source);
        Assert.Equal("sample-plugin", profile.PluginId);
        Assert.True(profile.ReadOnly);
        Assert.True(profile.TrustRestricted);
        Assert.Contains("tools.allow", profile.RestrictedFields);
        Assert.Contains("mcp.servers", profile.RestrictedFields);
        Assert.Contains(profile.Diagnostics, diagnostic => diagnostic.Code == "TrustBoundaryRestriction");
        var config = Assert.IsType<ThreadConfiguration>(profile.CompiledConfiguration);
        Assert.Equal("disabled", config.ToolPolicy?.AgentControl);
        Assert.Equal(["ReadFile"], config.ToolPolicy?.Allow ?? Array.Empty<string>());
        Assert.Empty(config.McpPolicy?.Servers ?? Array.Empty<string>());
        Assert.False(config.SkillsPolicy?.AllowManage);
        Assert.Equal(ApprovalPolicy.Default, config.ApprovalPolicy);
    }

    private static string ValidProfile(string id, string description) =>
        $"""
---
name: {id}
description: {description}
model: inherit
---

Instructions for {id}.
""";
}
