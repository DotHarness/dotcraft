using DotCraft.Configuration;

namespace DotCraft.Sessions;

internal static class ThreadConfigurationCloner
{
    internal static ThreadConfiguration Clone(ThreadConfiguration source) => new()
    {
        AgentProfileId = source.AgentProfileId,
        MemoryScope = source.MemoryScope,
        MemoryEnabled = source.MemoryEnabled,
        AgentProfileSource = source.AgentProfileSource,
        AgentProfileFingerprint = source.AgentProfileFingerprint,
        AgentBuilderTargetId = source.AgentBuilderTargetId,
        AgentBuilderTargetSource = source.AgentBuilderTargetSource,
        McpServers = source.McpServers == null ? null : [.. source.McpServers],
        Mode = source.Mode,
        Extensions = source.Extensions == null ? null : [.. source.Extensions],
        CustomTools = source.CustomTools == null ? null : [.. source.CustomTools],
        ProviderId = source.ProviderId,
        Model = source.Model,
        SubAgentModelCatalogSnapshot = source.SubAgentModelCatalogSnapshot == null
            ? null
            : SubAgentModelCatalogSnapshots.Clone(source.SubAgentModelCatalogSnapshot),
        Reasoning = CloneNullableReasoningConfig(source.Reasoning),
        Speed = source.Speed,
        ContextWindow = CloneNullableContextWindowConfig(source.ContextWindow),
        WorkspaceOverride = source.WorkspaceOverride,
        Cwd = source.Cwd,
        RuntimeWorkspaceRoots = source.RuntimeWorkspaceRoots == null ? null : [.. source.RuntimeWorkspaceRoots],
        ExecutionWorkspaceOverride = source.ExecutionWorkspaceOverride,
        ToolProfile = source.ToolProfile,
        UseToolProfileOnly = source.UseToolProfileOnly,
        AgentInstructions = source.AgentInstructions,
        ToolAllowList = source.ToolAllowList == null ? null : [.. source.ToolAllowList],
        ToolDenyList = source.ToolDenyList == null ? null : [.. source.ToolDenyList],
        ToolPolicy = CloneToolPolicy(source.ToolPolicy),
        McpPolicy = CloneMcpPolicy(source.McpPolicy),
        PluginPolicy = ClonePluginPolicy(source.PluginPolicy),
        SkillsPolicy = CloneSkillsPolicy(source.SkillsPolicy),
        AgentControlToolAccess = source.AgentControlToolAccess,
        AllowedAgentControlTools = source.AllowedAgentControlTools == null ? null : [.. source.AllowedAgentControlTools],
        RoleInstructions = source.RoleInstructions,
        DeveloperInstructions = source.DeveloperInstructions,
        OverrideBasePrompt = source.OverrideBasePrompt,
        ApprovalPolicy = source.ApprovalPolicy,
        ApprovalTimeoutSeconds = source.ApprovalTimeoutSeconds,
        AutomationTaskDirectory = source.AutomationTaskDirectory,
        RequireApprovalOutsideWorkspace = source.RequireApprovalOutsideWorkspace
    };

    internal static AppConfig.ReasoningConfig CloneReasoningConfig(AppConfig.ReasoningConfig source) => new()
    {
        Enabled = source.Enabled,
        Effort = source.Effort,
        Output = source.Output
    };

    internal static ThreadContextWindowConfig? CloneNullableContextWindowConfig(ThreadContextWindowConfig? source) =>
        source == null
            ? null
            : new ThreadContextWindowConfig
            {
                Mode = source.Mode
            };

    private static ThreadToolPolicy? CloneToolPolicy(ThreadToolPolicy? source) =>
        source == null
            ? null
            : new ThreadToolPolicy
            {
                Allow = source.Allow == null ? null : [.. source.Allow],
                Deny = source.Deny == null ? null : [.. source.Deny],
                AgentControl = source.AgentControl,
                AllowedAgentControlTools = source.AllowedAgentControlTools == null ? null : [.. source.AllowedAgentControlTools]
            };

    private static ThreadMcpPolicy? CloneMcpPolicy(ThreadMcpPolicy? source) =>
        source == null
            ? null
            : new ThreadMcpPolicy
            {
                Servers = source.Servers == null ? null : [.. source.Servers],
                Tools = CloneNamePolicy(source.Tools)
            };

    private static ThreadPluginPolicy? ClonePluginPolicy(ThreadPluginPolicy? source) =>
        source == null
            ? null
            : new ThreadPluginPolicy
            {
                Allow = source.Allow == null ? null : [.. source.Allow],
                Deny = source.Deny == null ? null : [.. source.Deny]
            };

    private static ThreadSkillsPolicy? CloneSkillsPolicy(ThreadSkillsPolicy? source) =>
        source == null
            ? null
            : new ThreadSkillsPolicy
            {
                Preload = source.Preload == null ? null : [.. source.Preload],
                Allow = source.Allow == null ? null : [.. source.Allow],
                Deny = source.Deny == null ? null : [.. source.Deny],
                AllowManage = source.AllowManage
            };

    private static ThreadNamePolicy? CloneNamePolicy(ThreadNamePolicy? source) =>
        source == null
            ? null
            : new ThreadNamePolicy
            {
                Allow = source.Allow == null ? null : [.. source.Allow],
                Deny = source.Deny == null ? null : [.. source.Deny]
            };

    internal static AppConfig.ReasoningConfig? CloneNullableReasoningConfig(AppConfig.ReasoningConfig? source) =>
        source == null ? null : CloneReasoningConfig(source);
}
