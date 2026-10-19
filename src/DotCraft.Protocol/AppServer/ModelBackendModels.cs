using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed class ProviderBackendListResult
{
    [JsonPropertyName("backends")]
    public IReadOnlyList<ProviderBackendInfo> Backends { get; init; } = [];
}

public sealed class ProviderBackendInfo
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("displayName")] public string DisplayName { get; init; } = string.Empty;
    [JsonPropertyName("defaultProtocol")] public string DefaultProtocol { get; init; } = string.Empty;
    [JsonPropertyName("protocols")] public IReadOnlyList<string> Protocols { get; init; } = [];
    [JsonPropertyName("authenticationMethods")] public IReadOnlyList<ProviderAuthenticationMethodInfo> AuthenticationMethods { get; init; } = [];
}

public sealed class ProviderAuthenticationMethodInfo
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("displayName")] public string DisplayName { get; init; } = string.Empty;
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
}

public sealed class ProviderAuthInstanceParams
{
    [JsonPropertyName("providerId")] public string ProviderId { get; init; } = string.Empty;
}

public sealed class ProviderAuthLoginParams
{
    [JsonPropertyName("providerId")] public string ProviderId { get; init; } = string.Empty;
    [JsonPropertyName("method")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Method { get; init; }
}

public sealed class ProviderAuthOperationParams
{
    [JsonPropertyName("loginId")] public string LoginId { get; init; } = string.Empty;
}

public sealed class ProviderAuthInputParams
{
    [JsonPropertyName("loginId")] public string LoginId { get; init; } = string.Empty;
    [JsonPropertyName("promptId")] public string PromptId { get; init; } = string.Empty;
    [JsonPropertyName("value")] public string Value { get; init; } = string.Empty;
}

public sealed class ProviderAuthStatusResult
{
    [JsonPropertyName("isAuthenticated")] public bool IsAuthenticated { get; init; }
    [JsonPropertyName("accountId")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AccountId { get; init; }
    [JsonPropertyName("displayName")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DisplayName { get; init; }
    [JsonPropertyName("planType")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PlanType { get; init; }
    [JsonPropertyName("email")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Email { get; init; }
    [JsonPropertyName("failureCode")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FailureCode { get; init; }
    [JsonPropertyName("failureMessage")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FailureMessage { get; init; }
}

public sealed class ProviderAuthEvent
{
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonPropertyName("message")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Message { get; init; }
    [JsonPropertyName("url")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Url { get; init; }
    [JsonPropertyName("userCode")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? UserCode { get; init; }
}

public sealed class ProviderAuthPrompt
{
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;
    [JsonPropertyName("choices")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<ProviderAuthChoice>? Choices { get; init; }
}

public sealed class ProviderAuthChoice
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("label")] public string Label { get; init; } = string.Empty;
}

public sealed class ProviderAuthLoginState
{
    [JsonPropertyName("loginId")] public string LoginId { get; init; } = string.Empty;
    [JsonPropertyName("providerId")] public string ProviderId { get; init; } = string.Empty;
    [JsonPropertyName("revision")] [JsonSafeInteger] public long Revision { get; init; }
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonPropertyName("event")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ProviderAuthEvent? Event { get; init; }
    [JsonPropertyName("promptId")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PromptId { get; init; }
    [JsonPropertyName("prompt")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ProviderAuthPrompt? Prompt { get; init; }
    [JsonPropertyName("authentication")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ProviderAuthStatusResult? Authentication { get; init; }
    [JsonPropertyName("errorCode")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ErrorCode { get; init; }
    [JsonPropertyName("errorMessage")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ErrorMessage { get; init; }
}

public sealed class ProviderAuthUsageResult
{
    [JsonPropertyName("available")] public bool Available { get; init; }
    [JsonPropertyName("snapshot")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public JsonElement? Snapshot { get; init; }
}

public sealed partial class ProviderInfo
{
    [JsonPropertyName("backendId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BackendId { get; init; }
}
