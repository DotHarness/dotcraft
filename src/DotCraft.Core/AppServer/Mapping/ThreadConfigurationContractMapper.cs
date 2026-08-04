using System.Text.Json;
using DotCraft.Configuration;
using Domain = DotCraft.Sessions;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions.Wire;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using ModelPreferenceContextWindow = DotCraft.Configuration.ModelPreferenceContextWindow;

namespace DotCraft.AppServer;

/// <summary>Maps persisted thread configuration to the public AppServer contract.</summary>
internal static class ThreadConfigurationContractMapper
{
    public static Contract.ThreadConfiguration ToContract(Domain.ThreadConfiguration value) => new()
    {
        AgentProfileId = OmitIfNull(value.AgentProfileId),
        AgentProfileSource = OmitIfNull(value.AgentProfileSource),
        AgentProfileFingerprint = OmitIfNull(value.AgentProfileFingerprint),
        AgentBuilderTargetId = OmitIfNull(value.AgentBuilderTargetId),
        AgentBuilderTargetSource = OmitIfNull(value.AgentBuilderTargetSource),
        McpServers = value.McpServers is null
            ? default
            : Protocol.Optional<IReadOnlyList<Contract.ThreadMcpServerConfig>?>.FromValue(
                value.McpServers.Select(ToContract).ToArray()),
        Mode = value.Mode,
        Extensions = OmitIfNull<IReadOnlyList<string>>(value.Extensions),
        CustomTools = OmitIfNull<IReadOnlyList<string>>(value.CustomTools),
        ProviderId = OmitIfNull(value.ProviderId),
        Model = OmitIfNull(value.Model),
        Speed = value.Speed is null ? default : OmitIfNull(WireString(value.Speed.Value)),
        Reasoning = value.Reasoning is null
            ? default
            : Protocol.Optional<Contract.ReasoningConfig?>.FromValue(ToContract(value.Reasoning)),
        ContextWindow = value.ContextWindow is null
            ? default
            : Protocol.Optional<Contract.ThreadContextWindowConfig?>.FromValue(new Contract.ThreadContextWindowConfig
            {
                Mode = WireString(value.ContextWindow.Mode)
            }),
        WorkspaceOverride = OmitIfNull(value.WorkspaceOverride),
        Cwd = OmitIfNull(value.Cwd),
        RuntimeWorkspaceRoots = OmitIfNull<IReadOnlyList<string>>(value.RuntimeWorkspaceRoots),
        ExecutionWorkspaceOverride = OmitIfNull(value.ExecutionWorkspaceOverride),
        ToolProfile = OmitIfNull(value.ToolProfile),
        UseToolProfileOnly = value.UseToolProfileOnly,
        AgentInstructions = OmitIfNull(value.AgentInstructions),
        ToolAllowList = OmitIfNull<IReadOnlyList<string>>(value.ToolAllowList),
        ToolDenyList = OmitIfNull<IReadOnlyList<string>>(value.ToolDenyList),
        ToolPolicy = value.ToolPolicy is null
            ? default
            : Protocol.Optional<Contract.ThreadToolPolicy?>.FromValue(ToContract(value.ToolPolicy)),
        McpPolicy = value.McpPolicy is null
            ? default
            : Protocol.Optional<Contract.ThreadMcpPolicy?>.FromValue(ToContract(value.McpPolicy)),
        PluginPolicy = value.PluginPolicy is null
            ? default
            : Protocol.Optional<Contract.ThreadPluginPolicy?>.FromValue(ToContract(value.PluginPolicy)),
        SkillsPolicy = value.SkillsPolicy is null
            ? default
            : Protocol.Optional<Contract.ThreadSkillsPolicy?>.FromValue(ToContract(value.SkillsPolicy)),
        TeamsPolicy = value.TeamsPolicy is null
            ? default
            : Protocol.Optional<Contract.ThreadTeamsPolicy?>.FromValue(ToContract(value.TeamsPolicy)),
        AgentControlToolAccess = value.AgentControlToolAccess is null
            ? default
            : OmitIfNull(WireString(value.AgentControlToolAccess.Value)),
        AllowedAgentControlTools = OmitIfNull<IReadOnlyList<string>>(value.AllowedAgentControlTools),
        PromptProfile = OmitIfNull(value.PromptProfile),
        RoleInstructions = OmitIfNull(value.RoleInstructions),
        OverrideBasePrompt = value.OverrideBasePrompt,
        ApprovalPolicy = WireString(value.ApprovalPolicy),
        ApprovalTimeoutSeconds = OmitIfNull(value.ApprovalTimeoutSeconds),
        AutomationTaskDirectory = OmitIfNull(value.AutomationTaskDirectory),
        RequireApprovalOutsideWorkspace = OmitIfNull(value.RequireApprovalOutsideWorkspace)
    };

    public static Contract.ModelPreferenceContextWindow ToContract(ModelPreferenceContextWindow value) => new()
    {
        Mode = WireString(value.Mode)
    };

    public static Contract.ModelPreference ToContract(ModelPreference value) => new()
    {
        Model = value.Model,
        Reasoning = ToContract(value.Reasoning),
        Speed = WireString(value.Speed),
        ContextWindow = ToContract(value.ContextWindow)
    };

    public static ModelPreference FromContract(Contract.ModelPreference value) => new()
    {
        Model = ValueOrDefault(value.Model) ?? string.Empty,
        Reasoning = ValueOrDefault(value.Reasoning) is { } reasoning
            ? FromContract(reasoning)
            : new AppConfig.ReasoningConfig(),
        Speed = ParseEnum(ValueOrDefault(value.Speed), InferenceSpeed.Standard),
        ContextWindow = ValueOrDefault(value.ContextWindow) is { } contextWindow
            ? new ModelPreferenceContextWindow
            {
                Mode = ParseEnum(ValueOrDefault(contextWindow.Mode), ContextWindowMode.Default)
            }
            : new ModelPreferenceContextWindow()
    };

    public static Domain.ThreadConfiguration FromContract(Contract.ThreadConfiguration value) => new()
    {
        AgentProfileId = ValueOrDefault(value.AgentProfileId),
        AgentProfileSource = ValueOrDefault(value.AgentProfileSource),
        AgentProfileFingerprint = ValueOrDefault(value.AgentProfileFingerprint),
        AgentBuilderTargetId = ValueOrDefault(value.AgentBuilderTargetId),
        AgentBuilderTargetSource = ValueOrDefault(value.AgentBuilderTargetSource),
        McpServers = ValueOrDefault(value.McpServers)?.Select(FromContract).ToArray(),
        Mode = ValueOrDefault(value.Mode) ?? "agent",
        Extensions = ValueOrDefault(value.Extensions)?.ToArray(),
        CustomTools = ValueOrDefault(value.CustomTools)?.ToArray(),
        ProviderId = ValueOrDefault(value.ProviderId),
        Model = ValueOrDefault(value.Model),
        Speed = ParseNullableEnum<InferenceSpeed>(ValueOrDefault(value.Speed)),
        Reasoning = ValueOrDefault(value.Reasoning) is { } reasoning ? FromContract(reasoning) : null,
        ContextWindow = ValueOrDefault(value.ContextWindow) is { } contextWindow
            ? new Domain.ThreadContextWindowConfig
            {
                Mode = ParseEnum(ValueOrDefault(contextWindow.Mode), ContextWindowMode.Default)
            }
            : null,
        WorkspaceOverride = ValueOrDefault(value.WorkspaceOverride),
        Cwd = ValueOrDefault(value.Cwd),
        RuntimeWorkspaceRoots = ValueOrDefault(value.RuntimeWorkspaceRoots)?.ToArray(),
        ExecutionWorkspaceOverride = ValueOrDefault(value.ExecutionWorkspaceOverride),
        ToolProfile = ValueOrDefault(value.ToolProfile),
        UseToolProfileOnly = ValueOrDefault(value.UseToolProfileOnly),
        AgentInstructions = ValueOrDefault(value.AgentInstructions),
        ToolAllowList = ValueOrDefault(value.ToolAllowList)?.ToArray(),
        ToolDenyList = ValueOrDefault(value.ToolDenyList)?.ToArray(),
        ToolPolicy = ValueOrDefault(value.ToolPolicy) is { } toolPolicy ? FromContract(toolPolicy) : null,
        McpPolicy = ValueOrDefault(value.McpPolicy) is { } mcpPolicy ? FromContract(mcpPolicy) : null,
        PluginPolicy = ValueOrDefault(value.PluginPolicy) is { } pluginPolicy ? FromContract(pluginPolicy) : null,
        SkillsPolicy = ValueOrDefault(value.SkillsPolicy) is { } skillsPolicy ? FromContract(skillsPolicy) : null,
        TeamsPolicy = ValueOrDefault(value.TeamsPolicy) is { } teamsPolicy ? FromContract(teamsPolicy) : null,
        AgentControlToolAccess = ParseNullableEnum<Tools.AgentControlToolAccess>(ValueOrDefault(value.AgentControlToolAccess)),
        AllowedAgentControlTools = ValueOrDefault(value.AllowedAgentControlTools)?.ToArray(),
        PromptProfile = ValueOrDefault(value.PromptProfile),
        RoleInstructions = ValueOrDefault(value.RoleInstructions),
        OverrideBasePrompt = ValueOrDefault(value.OverrideBasePrompt),
        ApprovalPolicy = ParseEnum(ValueOrDefault(value.ApprovalPolicy), Domain.ApprovalPolicy.Default),
        ApprovalTimeoutSeconds = ValueOrDefault(value.ApprovalTimeoutSeconds),
        AutomationTaskDirectory = ValueOrDefault(value.AutomationTaskDirectory),
        RequireApprovalOutsideWorkspace = ValueOrDefault(value.RequireApprovalOutsideWorkspace)
    };

    private static Contract.ThreadMcpServerConfig ToContract(McpServerConfig value) => new()
    {
        Name = value.Name,
        Enabled = value.Enabled,
        Transport = value.Transport,
        Command = value.Command,
        Arguments = value.Arguments,
        EnvironmentVariables = value.EnvironmentVariables,
        EnvVars = value.EnvVars,
        Cwd = OmitIfNull(value.Cwd),
        Url = value.Url,
        Headers = value.Headers,
        EnvHttpHeaders = value.EnvHttpHeaders,
        BearerTokenEnvVar = OmitIfNull(value.BearerTokenEnvVar),
        StartupTimeoutSec = OmitIfNull(value.StartupTimeoutSec),
        ToolTimeoutSec = OmitIfNull(value.ToolTimeoutSec)
    };

    private static McpServerConfig FromContract(Contract.ThreadMcpServerConfig value) => new()
    {
        Name = ValueOrDefault(value.Name) ?? string.Empty,
        Enabled = value.Enabled.IsSet ? value.Enabled.Value : true,
        Transport = ValueOrDefault(value.Transport) ?? "stdio",
        Command = ValueOrDefault(value.Command) ?? string.Empty,
        Arguments = ValueOrDefault(value.Arguments)?.ToList() ?? [],
        EnvironmentVariables = ValueOrDefault(value.EnvironmentVariables)?.ToDictionary() ?? [],
        EnvVars = ValueOrDefault(value.EnvVars)?.ToList() ?? [],
        Cwd = ValueOrDefault(value.Cwd),
        Url = ValueOrDefault(value.Url) ?? string.Empty,
        Headers = ValueOrDefault(value.Headers)?.ToDictionary() ?? [],
        EnvHttpHeaders = ValueOrDefault(value.EnvHttpHeaders)?.ToDictionary() ?? [],
        BearerTokenEnvVar = ValueOrDefault(value.BearerTokenEnvVar),
        StartupTimeoutSec = ValueOrDefault(value.StartupTimeoutSec),
        ToolTimeoutSec = ValueOrDefault(value.ToolTimeoutSec)
    };

    private static Contract.ReasoningConfig ToContract(AppConfig.ReasoningConfig value) => new()
    {
        Enabled = value.Enabled,
        Effort = WireString(value.Effort),
        Output = WireString(value.Output)
    };

    private static AppConfig.ReasoningConfig FromContract(Contract.ReasoningConfig value) => new()
    {
        Enabled = ValueOrDefault(value.Enabled),
        Effort = ParseEnum(ValueOrDefault(value.Effort), Microsoft.Extensions.AI.ReasoningEffort.Medium),
        Output = ParseEnum(ValueOrDefault(value.Output), Microsoft.Extensions.AI.ReasoningOutput.Full)
    };

    private static Contract.ThreadToolPolicy ToContract(Domain.ThreadToolPolicy value) => new()
    {
        Allow = OmitIfNull<IReadOnlyList<string>>(value.Allow),
        Deny = OmitIfNull<IReadOnlyList<string>>(value.Deny),
        AgentControl = OmitIfNull(value.AgentControl),
        AllowedAgentControlTools = OmitIfNull<IReadOnlyList<string>>(value.AllowedAgentControlTools)
    };

    private static Domain.ThreadToolPolicy FromContract(Contract.ThreadToolPolicy value) => new()
    {
        Allow = ValueOrDefault(value.Allow)?.ToArray(),
        Deny = ValueOrDefault(value.Deny)?.ToArray(),
        AgentControl = ValueOrDefault(value.AgentControl),
        AllowedAgentControlTools = ValueOrDefault(value.AllowedAgentControlTools)?.ToArray()
    };

    private static Contract.ThreadMcpPolicy ToContract(Domain.ThreadMcpPolicy value) => new()
    {
        Servers = OmitIfNull<IReadOnlyList<string>>(value.Servers),
        Tools = value.Tools is null
            ? default
            : Protocol.Optional<Contract.ThreadNamePolicy?>.FromValue(ToContract(value.Tools))
    };

    private static Domain.ThreadMcpPolicy FromContract(Contract.ThreadMcpPolicy value) => new()
    {
        Servers = ValueOrDefault(value.Servers)?.ToArray(),
        Tools = ValueOrDefault(value.Tools) is { } tools ? FromContract(tools) : null
    };

    private static Contract.ThreadNamePolicy ToContract(Domain.ThreadNamePolicy value) => new()
    {
        Allow = OmitIfNull<IReadOnlyList<string>>(value.Allow),
        Deny = OmitIfNull<IReadOnlyList<string>>(value.Deny)
    };

    private static Domain.ThreadNamePolicy FromContract(Contract.ThreadNamePolicy value) => new()
    {
        Allow = ValueOrDefault(value.Allow)?.ToArray(),
        Deny = ValueOrDefault(value.Deny)?.ToArray()
    };

    private static Contract.ThreadPluginPolicy ToContract(Domain.ThreadPluginPolicy value) => new()
    {
        Allow = OmitIfNull<IReadOnlyList<string>>(value.Allow),
        Deny = OmitIfNull<IReadOnlyList<string>>(value.Deny)
    };

    private static Domain.ThreadPluginPolicy FromContract(Contract.ThreadPluginPolicy value) => new()
    {
        Allow = ValueOrDefault(value.Allow)?.ToArray(),
        Deny = ValueOrDefault(value.Deny)?.ToArray()
    };

    private static Contract.ThreadSkillsPolicy ToContract(Domain.ThreadSkillsPolicy value) => new()
    {
        Preload = OmitIfNull<IReadOnlyList<string>>(value.Preload),
        Allow = OmitIfNull<IReadOnlyList<string>>(value.Allow),
        Deny = OmitIfNull<IReadOnlyList<string>>(value.Deny),
        AllowManage = OmitIfNull(value.AllowManage)
    };

    private static Domain.ThreadSkillsPolicy FromContract(Contract.ThreadSkillsPolicy value) => new()
    {
        Preload = ValueOrDefault(value.Preload)?.ToArray(),
        Allow = ValueOrDefault(value.Allow)?.ToArray(),
        Deny = ValueOrDefault(value.Deny)?.ToArray(),
        AllowManage = ValueOrDefault(value.AllowManage)
    };

    private static Contract.ThreadTeamsPolicy ToContract(Domain.ThreadTeamsPolicy value) => new()
    {
        ReservedTools = OmitIfNull(value.ReservedTools)
    };

    private static Domain.ThreadTeamsPolicy FromContract(Contract.ThreadTeamsPolicy value) => new()
    {
        ReservedTools = ValueOrDefault(value.ReservedTools)
    };

    private static string WireString<T>(T value) where T : struct, Enum =>
        JsonSerializer.SerializeToElement(value, SessionWireJsonOptions.Default).GetString()
        ?? throw new JsonException($"Could not serialize {typeof(T).Name}.");

    private static T ParseEnum<T>(string? value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : fallback;

    private static T? ParseNullableEnum<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : null;

    private static T? ValueOrDefault<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private static Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : Protocol.Optional<T?>.FromValue(value);
}
