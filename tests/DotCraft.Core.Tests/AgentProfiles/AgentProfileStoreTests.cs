using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using System.Text.Json;
using DotCraft.Sessions;
using DotCraft.Tools;
using ThreadConfiguration = DotCraft.Sessions.ThreadConfiguration;
using Xunit;

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
            Path.Combine(_userCraftPath, "agents", "reviewer.md"),
            ValidProfile("reviewer", "User reviewer"));
        File.WriteAllText(
            Path.Combine(_workspaceCraftPath, "agents", "reviewer.md"),
            ValidProfile("reviewer", "Workspace reviewer"));

        var store = new AgentProfileStore(_workspaceCraftPath, _userCraftPath);
        var entries = store.List();
        var effective = store.Read("reviewer");

        Assert.Equal(AgentProfileSources.Workspace, effective.Source);
        Assert.Equal("Workspace reviewer", effective.Description);

        var userEntry = entries.Single(entry =>
            entry.Id == "reviewer"
            && entry.Source == AgentProfileSources.User);
        Assert.True(userEntry.Shadowed);
        Assert.Equal(AgentProfileSources.Workspace, userEntry.ShadowedBy);

        var builtInEntry = entries.Single(entry =>
            entry.Id == "reviewer"
            && entry.Source == AgentProfileSources.BuiltIn);
        Assert.True(builtInEntry.Shadowed);
    }

    [Theory]
    [InlineData("leader", 128, "ReadFile,FindFiles,GrepFiles,LSP,WebSearch,WebFetch,RequestUserInput,TodoWrite,UpdateTodos,SpawnAgent,SendMessage,FollowupTask,WaitAgent,ListAgents,CloseAgent", null, AgentControlToolAccess.Full, ApprovalPolicy.Default)]
    [InlineData("explorer", 555, "ReadFile,FindFiles,GrepFiles,LSP,WebSearch,WebFetch", null, AgentControlToolAccess.Disabled, ApprovalPolicy.Default)]
    [InlineData("builder", 274, "ReadFile,FindFiles,GrepFiles,LSP,Exec,WriteStdin,WriteFile,EditFile,WebSearch,WebFetch,RequestUserInput,TodoWrite,UpdateTodos", null, AgentControlToolAccess.Disabled, ApprovalPolicy.Default)]
    [InlineData("reviewer", 457, "ReadFile,FindFiles,GrepFiles,LSP,WebSearch,WebFetch", null, AgentControlToolAccess.Disabled, ApprovalPolicy.Default)]
    [InlineData("operator", 695, null, "WriteFile,EditFile,Exec,WriteStdin,Cron,CreatePlan,TodoWrite,UpdateTodos,GetGoal,CreateGoal,UpdateGoal,imagegen", AgentControlToolAccess.Disabled, ApprovalPolicy.Prompt)]
    public void BuiltInProfiles_CompileRoleCapabilityPolicies(
        string profileId,
        int avatar,
        string? allowedTools,
        string? deniedTools,
        AgentControlToolAccess agentControl,
        ApprovalPolicy approvalPolicy)
    {
        var store = new AgentProfileStore(_workspaceCraftPath, _userCraftPath);
        var profile = store.Read(profileId);
        var config = Assert.IsType<ThreadConfiguration>(profile.CompiledConfiguration);

        Assert.Equal(avatar, profile.Avatar);
        Assert.Equal(SplitTools(allowedTools), config.ToolPolicy?.Allow);
        Assert.Equal(SplitTools(deniedTools), config.ToolPolicy?.Deny);
        Assert.Equal(agentControl, config.AgentControlToolAccess);
        Assert.Equal(approvalPolicy, config.ApprovalPolicy);
        Assert.Equal(false, config.SkillsPolicy?.AllowManage);
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

    private static string[]? SplitTools(string? value) =>
        value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
providerPreference:
  providerId: profile-provider
  model: profile-model
  reasoning:
    enabled: true
    effort: high
  speed: standard
  contextWindow:
    mode: default
tools:
  allow: [ReadFile, FindFiles]
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
        Assert.Null(config.ProviderId);
        Assert.Null(config.Model);
        Assert.Null(config.Reasoning);
        var preset = Assert.IsType<AgentProfileProviderPreference>(result.ProviderPreference);
        Assert.Equal("profile-provider", preset.ProviderId);
        Assert.Equal("profile-model", preset.Model);
        Assert.True(preset.Reasoning.Enabled);
        Assert.Equal(ReasoningEffort.High, preset.Reasoning.Effort);
        Assert.NotNull(config.ToolPolicy);
        var toolPolicy = config.ToolPolicy!;
        Assert.Equal(new[] { "ReadFile", "FindFiles" }, toolPolicy.Allow ?? Array.Empty<string>());
        Assert.Null(toolPolicy.Deny);
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
        Assert.Equal("Focus on correctness.", config.RoleInstructions);
    }

    [Fact]
    public void ValidateRaw_RejectsCombinedToolAllowAndDeny()
    {
        var store = new AgentProfileStore(_workspaceCraftPath, _userCraftPath);
        var result = store.ValidateRaw(
            """
---
name: conflicting-tools
description: Invalid combined policy
tools:
  allow: []
  deny: [WriteFile]
---

Body
""",
            AgentProfileSources.Workspace);

        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, d => d.Code == "ConflictingToolPolicy");
    }

    [Fact]
    public void ValidateRaw_CompilesReducedProviderPreference()
    {
        var store = new AgentProfileStore(_workspaceCraftPath, _userCraftPath);
        var result = store.ValidateRaw(
            """
---
name: pinned-reviewer
description: Reviewer with a pinned model preference
providerPreference:
  providerId: openai
  model: gpt-5.6
  reasoning:
    enabled: true
    effort: high
  speed: fast
  contextWindow:
    mode: max
---

Review carefully.
""",
            AgentProfileSources.Workspace);

        Assert.True(result.Valid);
        var providerPreference = Assert.IsType<AgentProfileProviderPreference>(result.ProviderPreference);
        Assert.Equal("openai", providerPreference.ProviderId);
        Assert.Equal("gpt-5.6", providerPreference.Model);
        Assert.True(providerPreference.Reasoning.Enabled);
        Assert.Equal(ReasoningEffort.High, providerPreference.Reasoning.Effort);
        Assert.Equal(InferenceSpeed.Fast, providerPreference.Speed);
        Assert.Equal(ContextWindowMode.Max, providerPreference.ContextWindow.Mode);

        var config = Assert.IsType<ThreadConfiguration>(result.CompiledConfiguration);
        Assert.Null(config.ProviderId);
        Assert.Null(config.Model);
        Assert.Null(config.Reasoning);
        Assert.Null(config.Speed);
        Assert.Null(config.ContextWindow);
    }

    [Fact]
    public void ValidateRaw_OmittedProviderPreferenceLeavesModelFieldsUnresolved()
    {
        var store = new AgentProfileStore(_workspaceCraftPath, _userCraftPath);
        var result = store.ValidateRaw(
            """
---
name: inherited-reviewer
description: Reviewer that inherits the workspace preference
---

Review carefully.
""",
            AgentProfileSources.Workspace);

        Assert.True(result.Valid);
        Assert.Null(result.ProviderPreference);
        var config = Assert.IsType<ThreadConfiguration>(result.CompiledConfiguration);
        Assert.Null(config.ProviderId);
        Assert.Null(config.Model);
        Assert.Null(config.Reasoning);
        Assert.Null(config.Speed);
        Assert.Null(config.ContextWindow);
    }

    [Fact]
    public void ResolveThreadStartConfiguration_ModelOverlayReseedsPinnedOptions()
    {
        var store = new AgentProfileStore(_workspaceCraftPath, _userCraftPath);
        store.Upsert(
            "pinned-reviewer",
            AgentProfileSources.Workspace,
            """
---
name: pinned-reviewer
description: Pinned reviewer
providerPreference:
  providerId: openai
  model: gpt-pinned
  reasoning:
    enabled: true
    effort: high
  speed: fast
  contextWindow:
    mode: max
---

Review.
""");
        using var document = JsonDocument.Parse(
            """{"agentProfileId":"pinned-reviewer","model":"gpt-overlay"}""");

        var resolved = store.ResolveThreadStartConfiguration(
            new ThreadConfiguration
            {
                AgentProfileId = "pinned-reviewer",
                Model = "gpt-overlay"
            },
            RuntimeConfig(),
            document.RootElement);

        Assert.Equal("openai", resolved.ProviderId);
        Assert.Equal("gpt-overlay", resolved.Model);
        Assert.Null(resolved.Reasoning);
        Assert.Null(resolved.Speed);
        Assert.Null(resolved.ContextWindow);
    }

    [Fact]
    public void ResolveProfileConfiguration_DerivesReasoningOutputFromModelCatalog()
    {
        var store = new AgentProfileStore(_workspaceCraftPath, _userCraftPath);
        store.Upsert(
            "pinned-reviewer",
            AgentProfileSources.Workspace,
            """
---
name: pinned-reviewer
description: Pinned reviewer
providerPreference:
  providerId: openai
  model: custom-reasoner-v1
  reasoning:
    enabled: true
    effort: high
  speed: fast
  contextWindow:
    mode: default
---

Review.
""");

        var config = RuntimeConfig();
        File.WriteAllText(
            Path.Combine(_workspaceCraftPath, ModelThinkingAdapterCatalog.FileName),
            """
{
  "reasoningCapabilities": {
    "adapters": [{
      "protocols": ["openai-responses"],
      "models": ["custom-reasoner-"],
      "supportsDisable": true,
      "supportedEfforts": ["high"],
      "defaultEffort": "high",
      "supportedOutputs": ["summary"],
      "defaultOutput": "summary"
    }]
  }
}
""");

        var resolved = store.ResolveProfileConfiguration("pinned-reviewer", config);

        Assert.Equal("openai", resolved.ProviderId);
        Assert.Equal("custom-reasoner-v1", resolved.Model);
        Assert.Equal(ReasoningOutput.Summary, resolved.Reasoning?.Output);
        Assert.Equal(InferenceSpeed.Fast, resolved.Speed);
    }

    [Fact]
    public void ResolveThreadStartConfiguration_ExplicitReasoningOutputOverridesCatalogDefault()
    {
        var store = new AgentProfileStore(_workspaceCraftPath, _userCraftPath);
        store.Upsert(
            "pinned-reviewer",
            AgentProfileSources.Workspace,
            """
---
name: pinned-reviewer
description: Pinned reviewer
providerPreference:
  providerId: openai
  model: gpt-5.6-sol
  reasoning:
    enabled: true
    effort: high
  speed: standard
  contextWindow:
    mode: default
---

Review.
""");
        using var document = JsonDocument.Parse(
            """{"agentProfileId":"pinned-reviewer","reasoning":{"enabled":true,"effort":"medium","output":"none"}}""");

        var resolved = store.ResolveThreadStartConfiguration(
            new ThreadConfiguration
            {
                AgentProfileId = "pinned-reviewer",
                Reasoning = new AppConfig.ReasoningConfig
                {
                    Enabled = true,
                    Effort = ReasoningEffort.Medium,
                    Output = ReasoningOutput.None
                }
            },
            RuntimeConfig(),
            document.RootElement);

        Assert.Equal(ReasoningOutput.None, resolved.Reasoning?.Output);
        Assert.Equal(ReasoningEffort.Medium, resolved.Reasoning?.Effort);
    }

    [Fact]
    public void ValidateRaw_RejectsReasoningOutputField()
    {
        var store = new AgentProfileStore(_workspaceCraftPath, _userCraftPath);
        var result = store.ValidateRaw(
            """
---
name: obsolete-output
description: Uses an obsolete Profile field
providerPreference:
  providerId: openai
  model: gpt-5.6-sol
  reasoning:
    enabled: true
    effort: high
    output: summary
  speed: standard
  contextWindow:
    mode: default
---

Body.
""",
            AgentProfileSources.Workspace);

        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "UnsupportedField"
            && diagnostic.Message.Contains("reasoning.output", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRaw_RejectsIncompleteProviderPreference()
    {
        var store = new AgentProfileStore(_workspaceCraftPath, _userCraftPath);
        var result = store.ValidateRaw(
            """
---
name: incomplete
description: Missing complete model options
providerPreference:
  providerId: openai
  model: gpt-5.6
---

Body.
""",
            AgentProfileSources.Workspace);

        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "MissingRequiredField"
            && diagnostic.Message.Contains("reasoning", StringComparison.Ordinal));
        Assert.Null(result.CompiledConfiguration);
    }

    [Fact]
    public void ValidateRaw_RejectsEmptyProviderPreference()
    {
        var store = new AgentProfileStore(_workspaceCraftPath, _userCraftPath);
        var result = store.ValidateRaw(
            """
---
name: empty-preference
description: Empty provider preference
providerPreference: {}
---

Body.
""",
            AgentProfileSources.Workspace);

        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "MissingRequiredField");
        Assert.Null(result.CompiledConfiguration);
    }

    [Fact]
    public void ValidateRaw_RejectsRemovedProviderPreferenceModeShape()
    {
        var store = new AgentProfileStore(_workspaceCraftPath, _userCraftPath);
        var result = store.ValidateRaw(
            """
---
name: removed-mode
description: Uses removed provider preference mode
providerPreference:
  mode: inherit
---

Body.
""",
            AgentProfileSources.Workspace);

        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "UnsupportedField");
        Assert.Null(result.CompiledConfiguration);
    }

    [Fact]
    public void ValidateRaw_RejectsRemovedTopLevelModelFields()
    {
        var store = new AgentProfileStore(_workspaceCraftPath, _userCraftPath);
        var result = store.ValidateRaw(
            """
---
name: removed-model-fields
description: Uses removed model fields
model: inherit
reasoning:
  effort: high
---

Body.
""",
            AgentProfileSources.Workspace);

        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "UnsupportedField");
        Assert.Null(result.CompiledConfiguration);
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
---

Instructions for {id}.
""";

    private AppConfig RuntimeConfig()
    {
        var config = new AppConfig
        {
            ProviderId = "openai",
            WorkspaceConfigPath = Path.Combine(_workspaceCraftPath, "config.json"),
            ProviderPreferences = new()
            {
                ["openai"] = ModelPreferenceRules.CreateManual("gpt-default")
            }
        };
        config.Providers["openai"] = new AppConfig.ModelProviderConfig
        {
            DisplayName = "OpenAI",
            Protocol = ModelProviderProtocols.OpenAIResponses,
            ApiKey = "test"
        };
        return config;
    }
}
