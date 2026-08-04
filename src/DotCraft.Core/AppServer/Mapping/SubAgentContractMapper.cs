using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using Contract = DotCraft.Protocol.AppServer;
using Domain = DotCraft.Sessions;
using ModelPreference = DotCraft.Configuration.ModelPreference;

namespace DotCraft.AppServer;

/// <summary>Maps SubAgent runtime and persistence models to the public AppServer contract.</summary>
internal static class SubAgentContractMapper
{
    public static Contract.SubAgentControlResult ToContract(Domain.SubAgentControlResult value) => new()
    {
        AgentPath = OmitIfNull(value.AgentPath),
        TaskName = OmitIfNull(value.TaskName),
        Status = value.Status,
        Message = OptionalNull(value.Message),
        AgentNickname = OptionalNull(value.AgentNickname),
        AgentRole = OptionalNull(value.AgentRole),
        ProfileName = OptionalNull(value.ProfileName),
        RuntimeType = OptionalNull(value.RuntimeType),
        SupportsSendMessage = value.SupportsSendMessage,
        SupportsFollowupTask = value.SupportsFollowupTask,
        SupportsClose = value.SupportsClose
    };

    public static Contract.ThreadSpawnEdge ToContract(Domain.ThreadSpawnEdge value) => new()
    {
        ParentThreadId = value.ParentThreadId,
        ChildThreadId = value.ChildThreadId,
        ParentTurnId = OmitIfNull(value.ParentTurnId),
        Depth = value.Depth,
        AgentPath = OmitIfNull(value.AgentPath),
        TaskName = OmitIfNull(value.TaskName),
        AgentNickname = OmitIfNull(value.AgentNickname),
        AgentRole = OmitIfNull(value.AgentRole),
        ProfileName = OmitIfNull(value.ProfileName),
        RuntimeType = OmitIfNull(value.RuntimeType),
        SupportsSendInput = value.SupportsSendInput,
        SupportsResume = value.SupportsResume,
        SupportsSendMessage = value.SupportsSendMessage,
        SupportsFollowupTask = value.SupportsFollowupTask,
        SupportsClose = value.SupportsClose,
        Status = value.Status,
        CreatedAt = value.CreatedAt,
        UpdatedAt = value.UpdatedAt
    };

    public static Contract.SubAgentProfileWrite ToContract(SubAgentProfile value) => new()
    {
        Runtime = value.Runtime,
        Bin = OmitIfNull(value.Bin),
        Args = OmitIfNull<IReadOnlyList<string>>(value.Args is { Count: > 0 } ? [.. value.Args] : null),
        Env = OmitIfNull<IReadOnlyDictionary<string, string>>(
            value.Env is { Count: > 0 } ? new Dictionary<string, string>(value.Env, StringComparer.Ordinal) : null),
        EnvPassthrough = OmitIfNull<IReadOnlyList<string>>(
            value.EnvPassthrough is { Count: > 0 } ? [.. value.EnvPassthrough] : null),
        WorkingDirectoryMode = value.WorkingDirectoryMode,
        SupportsStreaming = OmitIfNull(value.SupportsStreaming),
        SupportsResume = OmitIfNull(value.SupportsResume),
        SupportsModelSelection = OmitIfNull(value.SupportsModelSelection),
        InputFormat = OmitIfNull(value.InputFormat),
        OutputFormat = OmitIfNull(value.OutputFormat),
        InputMode = OmitIfNull(value.InputMode),
        InputArgTemplate = OmitIfNull(value.InputArgTemplate),
        InputEnvKey = OmitIfNull(value.InputEnvKey),
        ResumeArgTemplate = OmitIfNull(value.ResumeArgTemplate),
        ResumeSessionIdJsonPath = OmitIfNull(value.ResumeSessionIdJsonPath),
        ResumeSessionIdRegex = OmitIfNull(value.ResumeSessionIdRegex),
        OutputJsonPath = OmitIfNull(value.OutputJsonPath),
        OutputInputTokensJsonPath = OmitIfNull(value.OutputInputTokensJsonPath),
        OutputOutputTokensJsonPath = OmitIfNull(value.OutputOutputTokensJsonPath),
        OutputTotalTokensJsonPath = OmitIfNull(value.OutputTotalTokensJsonPath),
        OutputFileArgTemplate = OmitIfNull(value.OutputFileArgTemplate),
        ReadOutputFile = OmitIfNull(value.ReadOutputFile),
        DeleteOutputFileAfterRead = OmitIfNull(value.DeleteOutputFileAfterRead),
        MaxOutputBytes = OmitIfNull(value.MaxOutputBytes),
        Timeout = OmitIfNull(value.Timeout),
        TrustLevel = OmitIfNull(value.TrustLevel),
        PermissionModeMapping = OmitIfNull<IReadOnlyDictionary<string, string>>(
            value.PermissionModeMapping is { Count: > 0 }
                ? new Dictionary<string, string>(value.PermissionModeMapping, StringComparer.OrdinalIgnoreCase)
                : null),
        SanitizationRules = value.SanitizationRules is null
            ? default
            : Protocol.Optional<JsonElement?>.FromValue(
                JsonSerializer.SerializeToElement<JsonObject?>(value.SanitizationRules))
    };

    public static SubAgentProfile FromContract(string name, Contract.SubAgentProfileWrite value) => new()
    {
        Name = name.Trim(),
        Runtime = Read(value.Runtime)?.Trim() ?? string.Empty,
        Bin = Normalize(Read(value.Bin)),
        Args = Read(value.Args) is { Count: > 0 } args
            ? [.. args.Where(arg => !string.IsNullOrWhiteSpace(arg)).Select(arg => arg.Trim())]
            : null,
        Env = Read(value.Env) is { Count: > 0 } env
            ? new Dictionary<string, string>(env, StringComparer.Ordinal)
            : null,
        EnvPassthrough = Read(value.EnvPassthrough) is { Count: > 0 } envPassthrough
            ? [.. envPassthrough.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim())]
            : null,
        WorkingDirectoryMode = Normalize(Read(value.WorkingDirectoryMode)) ?? "workspace",
        SupportsStreaming = Read(value.SupportsStreaming),
        SupportsResume = Read(value.SupportsResume),
        SupportsModelSelection = Read(value.SupportsModelSelection),
        InputFormat = Normalize(Read(value.InputFormat)),
        OutputFormat = Normalize(Read(value.OutputFormat)),
        InputMode = Normalize(Read(value.InputMode)),
        InputArgTemplate = NullIfWhiteSpace(Read(value.InputArgTemplate)),
        InputEnvKey = Normalize(Read(value.InputEnvKey)),
        ResumeArgTemplate = NullIfWhiteSpace(Read(value.ResumeArgTemplate)),
        ResumeSessionIdJsonPath = Normalize(Read(value.ResumeSessionIdJsonPath)),
        ResumeSessionIdRegex = NullIfWhiteSpace(Read(value.ResumeSessionIdRegex)),
        OutputJsonPath = Normalize(Read(value.OutputJsonPath)),
        OutputInputTokensJsonPath = Normalize(Read(value.OutputInputTokensJsonPath)),
        OutputOutputTokensJsonPath = Normalize(Read(value.OutputOutputTokensJsonPath)),
        OutputTotalTokensJsonPath = Normalize(Read(value.OutputTotalTokensJsonPath)),
        OutputFileArgTemplate = NullIfWhiteSpace(Read(value.OutputFileArgTemplate)),
        ReadOutputFile = Read(value.ReadOutputFile),
        DeleteOutputFileAfterRead = Read(value.DeleteOutputFileAfterRead),
        MaxOutputBytes = Read(value.MaxOutputBytes),
        Timeout = Read(value.Timeout),
        TrustLevel = Normalize(Read(value.TrustLevel)),
        PermissionModeMapping = Read(value.PermissionModeMapping) is { Count: > 0 } mapping
            ? new Dictionary<string, string>(mapping, StringComparer.OrdinalIgnoreCase)
            : null,
        SanitizationRules = Read(value.SanitizationRules) is { } rules
            ? JsonNode.Parse(rules.GetRawText()) as JsonObject
            : null
    };

    public static Contract.SubAgentSettings ToContract(
        bool resumeEnabled,
        IReadOnlyDictionary<string, ModelPreference> providerPreferences,
        SubAgentWaitAgentTimeoutOptions waitTimeouts) => new()
    {
        ExternalCliSessionResumeEnabled = resumeEnabled,
        ProviderPreferences = providerPreferences.Count == 0
            ? default
            : new Protocol.Optional<IReadOnlyDictionary<string, Contract.ModelPreference>?>(
                providerPreferences.ToDictionary(
                    pair => pair.Key,
                    pair => ThreadConfigurationContractMapper.ToContract(pair.Value),
                    StringComparer.Ordinal)),
        MinWaitTimeoutMs = waitTimeouts.MinTimeoutMs,
        DefaultWaitTimeoutMs = waitTimeouts.DefaultTimeoutMs,
        MaxWaitTimeoutMs = waitTimeouts.MaxTimeoutMs
    };

    public static Dictionary<string, ModelPreference>? FromContract(
        IReadOnlyDictionary<string, Contract.ModelPreference>? values) =>
        values?.ToDictionary(
            pair => pair.Key,
            pair => ThreadConfigurationContractMapper.FromContract(pair.Value),
            StringComparer.Ordinal);

    public static T? Read<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value?.Trim()) ? null : value.Trim();

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : Protocol.Optional<T?>.FromValue(value);

    private static Protocol.Optional<string?> OptionalNull(string? value) =>
        Protocol.Optional<string?>.FromValue(value);
}
