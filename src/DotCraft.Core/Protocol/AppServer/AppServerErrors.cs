using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// JSON-RPC 2.0 error object for outbound error responses.
/// </summary>
public sealed class AppServerError
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; init; }
}

public sealed class AppServerErrorData
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("messageKey")]
    public string MessageKey { get; init; } = string.Empty;

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Params { get; init; }

    [JsonPropertyName("fallbackText")]
    public string FallbackText { get; init; } = string.Empty;

    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; init; }
}

/// <summary>
/// Exception thrown by AppServer request handlers to produce a JSON-RPC error response.
/// The handler catches this and serializes it as a JSON-RPC error.
/// </summary>
public sealed class AppServerException(int code, string message, object? errorData = null) : Exception(message)
{
    public int Code { get; } = code;

    public object? ErrorData { get; } = errorData;

    public AppServerError ToError() => new() { Code = Code, Message = Message, Data = ErrorData };
}

/// <summary>
/// Error code constants and factory methods from spec Section 8.2 and 8.3.
/// </summary>
public static class AppServerErrors
{
    // ── JSON-RPC standard codes (Section 8.2) ──

    public const int ParseErrorCode = -32700;
    public const int InvalidRequestCode = -32600;
    public const int MethodNotFoundCode = -32601;
    public const int InvalidParamsCode = -32602;
    public const int InternalErrorCode = -32603;

    // ── DotCraft-specific codes (Section 8.3) ──

    public const int ServerOverloadedCode = -32001;
    public const int NotInitializedCode = -32002;
    public const int AlreadyInitializedCode = -32003;
    public const int ThreadNotFoundCode = -32010;
    public const int ThreadNotActiveCode = -32011;
    public const int TurnInProgressCode = -32012;
    public const int TurnNotFoundCode = -32013;
    public const int TurnNotRunningCode = -32014;
    public const int ApprovalTimeoutCode = -32020;
    public const int ChannelRejectedCode = -32030;
    public const int CronJobNotFoundCode = -32031;

    public const int SkillNotFoundCode = -32040;

    public const int CommandNotFoundCode = -32060;
    public const int CommandPermissionDeniedCode = -32061;
    public const int CommandServiceUnavailableCode = -32062;
    public const int McpServerNotFoundCode = -32070;
    public const int McpServerValidationFailedCode = -32072;
    public const int McpServerTestFailedCode = -32073;
    public const int McpServerNameConflictCode = -32074;
    public const int McpServerReadOnlyCode = -32075;
    public const int ExternalChannelNotFoundCode = -32080;
    public const int ExternalChannelValidationFailedCode = -32081;
    public const int ExternalChannelNameConflictCode = -32082;
    public const int SubAgentProfileNotFoundCode = -32083;
    public const int SubAgentProfileValidationFailedCode = -32084;
    public const int SubAgentProfileProtectedCode = -32085;
    public const int AgentProfileNotFoundCode = -32086;
    public const int AgentProfileValidationFailedCode = -32087;
    public const int AgentProfileProtectedCode = -32088;
    public const int AgentProfileSourceUnavailableCode = -32089;
    public const int WorktreeHandoffConflictCode = -32090;
    public const int AgentProfileConflictCode = -32091;
    // ── Automation-specific codes (-32050 to -32059) ──

    public const int TaskNotFoundCode = -32051;
    public const int TaskInvalidStatusCode = -32052;
    public const int TaskAlreadyExistsCode = -32054;

    // ── Factory methods ──

    private static AppServerException Create(
        int jsonRpcCode,
        string code,
        string messageKey,
        string fallbackText,
        object? parameters = null,
        string? detail = null) =>
        new(jsonRpcCode, fallbackText, new AppServerErrorData
        {
            Code = code,
            MessageKey = messageKey,
            Params = parameters,
            FallbackText = fallbackText,
            Detail = detail
        });

    public static AppServerException ParseError(string? detail = null) =>
        Create(ParseErrorCode, "ParseError", "errors.parse", "Parse error", detail: detail);

    public static AppServerException InvalidRequest(string detail) =>
        Create(InvalidRequestCode, "InvalidRequest", "errors.invalidRequest", "Invalid request", detail: detail);

    public static AppServerException MethodNotFound(string method) =>
        Create(MethodNotFoundCode, "MethodNotFound", "errors.methodNotFound", $"Method not found: {method}", new { method });

    public static AppServerException InvalidParams(string detail) =>
        Create(InvalidParamsCode, "InvalidParams", "errors.invalidParams", "Invalid params", detail: detail);

    public static AppServerException InternalError(string detail) =>
        Create(InternalErrorCode, "InternalError", "errors.internal", "Internal error", detail: detail);

    public static AppServerException ServerOverloaded() =>
        Create(ServerOverloadedCode, "ServerOverloaded", "errors.serverOverloaded", "Server overloaded; retry later.");

    public static AppServerException NotInitialized() =>
        Create(NotInitializedCode, "NotInitialized", "errors.notInitialized", "Not initialized");

    public static AppServerException AlreadyInitialized() =>
        Create(AlreadyInitializedCode, "AlreadyInitialized", "errors.alreadyInitialized", "Already initialized");

    public static AppServerException ThreadNotFound(string threadId) =>
        Create(ThreadNotFoundCode, "ThreadNotFound", "errors.threadNotFound", $"Thread not found: {threadId}", new { threadId });

    public static AppServerException ThreadNotActive(string threadId) =>
        Create(ThreadNotActiveCode, "ThreadNotActive", "errors.threadNotActive", $"Thread is not active: {threadId}", new { threadId });

    public static AppServerException TurnInProgress(string threadId) =>
        Create(TurnInProgressCode, "TurnInProgress", "errors.turnInProgress", $"A turn is already in progress on thread: {threadId}", new { threadId });

    public static AppServerException TurnNotFound(string turnId) =>
        Create(TurnNotFoundCode, "TurnNotFound", "errors.turnNotFound", $"Turn not found: {turnId}", new { turnId });

    public static AppServerException TurnNotRunning(string turnId) =>
        Create(TurnNotRunningCode, "TurnNotRunning", "errors.turnNotRunning", $"Turn is not running: {turnId}", new { turnId });

    public static AppServerException WorktreeHandoffConflict(IReadOnlyList<string> conflictPaths) =>
        Create(
            WorktreeHandoffConflictCode,
            "WorktreeHandoffConflict",
            "errors.worktreeHandoffConflict",
            "Local workspace has conflicting uncommitted changes.",
            new { conflictPaths });

    public static AppServerException ApprovalTimeout() =>
        Create(ApprovalTimeoutCode, "ApprovalTimeout", "errors.approvalTimeout", "Approval request timed out");

    public static AppServerException ChannelRejected(string channelName) =>
        Create(ChannelRejectedCode, "ChannelRejected", "errors.channelRejected", $"Channel adapter rejected: '{channelName}' is not registered in server configuration", new { channelName });

    public static AppServerException CronJobNotFound(string jobId) =>
        Create(CronJobNotFoundCode, "CronJobNotFound", "errors.cronJobNotFound", $"Cron job not found: {jobId}", new { jobId });

    public static AppServerException SkillNotFound(string name) =>
        Create(SkillNotFoundCode, "SkillNotFound", "errors.skillNotFound", $"Skill not found: {name}", new { name });

    public static AppServerException CommandNotFound(string command) =>
        Create(CommandNotFoundCode, "CommandNotFound", "errors.commandNotFound", $"Command not found: {command}", new { command });

    public static AppServerException CommandPermissionDenied(string command) =>
        Create(CommandPermissionDeniedCode, "CommandPermissionDenied", "errors.commandPermissionDenied", $"Permission denied for command: {command}", new { command });

    public static AppServerException CommandServiceUnavailable(string command) =>
        Create(CommandServiceUnavailableCode, "CommandServiceUnavailable", "errors.commandServiceUnavailable", $"Service unavailable for command: {command}", new { command });

    public static AppServerException McpServerNotFound(string name) =>
        Create(McpServerNotFoundCode, "McpServerNotFound", "errors.mcpServerNotFound", $"MCP server not found: {name}", new { name });

    public static AppServerException McpServerValidationFailed(string detail) =>
        Create(McpServerValidationFailedCode, "McpServerValidationFailed", "errors.mcpServerValidationFailed", "MCP server validation failed", detail: detail);

    public static AppServerException McpServerTestFailed(string detail) =>
        Create(McpServerTestFailedCode, "McpServerTestFailed", "errors.mcpServerTestFailed", "MCP server test failed", detail: detail);

    public static AppServerException McpServerNameConflict(string detail) =>
        Create(McpServerNameConflictCode, "McpServerNameConflict", "errors.mcpServerNameConflict", "MCP server name conflict", detail: detail);

    public static AppServerException McpServerReadOnly(string name) =>
        Create(McpServerReadOnlyCode, "McpServerReadOnly", "errors.mcpServerReadOnly", $"MCP server is read-only: {name}", new { name });

    public static AppServerException ExternalChannelNotFound(string name) =>
        Create(ExternalChannelNotFoundCode, "ExternalChannelNotFound", "errors.externalChannelNotFound", $"External channel not found: {name}", new { name });

    public static AppServerException ExternalChannelValidationFailed(string detail) =>
        Create(ExternalChannelValidationFailedCode, "ExternalChannelValidationFailed", "errors.externalChannelValidationFailed", "External channel validation failed", detail: detail);

    public static AppServerException ExternalChannelNameConflict(string detail) =>
        Create(ExternalChannelNameConflictCode, "ExternalChannelNameConflict", "errors.externalChannelNameConflict", "External channel name conflict", detail: detail);

    public static AppServerException SubAgentProfileNotFound(string name) =>
        Create(SubAgentProfileNotFoundCode, "SubAgentProfileNotFound", "errors.subAgentProfileNotFound", $"SubAgent profile not found: {name}", new { name });

    public static AppServerException SubAgentProfileValidationFailed(string detail) =>
        Create(SubAgentProfileValidationFailedCode, "SubAgentProfileValidationFailed", "errors.subAgentProfileValidationFailed", "SubAgent profile validation failed", detail: detail);

    public static AppServerException SubAgentProfileProtected(string detail) =>
        Create(SubAgentProfileProtectedCode, "SubAgentProfileProtected", "errors.subAgentProfileProtected", "SubAgent profile is protected", detail: detail);

    public static AppServerException AgentProfileNotFound(string detail) =>
        Create(AgentProfileNotFoundCode, "AgentProfileNotFound", "errors.agentProfileNotFound", "Agent profile not found", detail: detail);

    public static AppServerException AgentProfileValidationFailed(
        string detail,
        object? diagnostics = null) =>
        Create(
            AgentProfileValidationFailedCode,
            "AgentProfileValidationFailed",
            "errors.agentProfileValidationFailed",
            "Agent profile validation failed",
            diagnostics == null ? null : new { diagnostics },
            detail);

    public static AppServerException AgentProfileProtected(string detail) =>
        Create(AgentProfileProtectedCode, "AgentProfileProtected", "errors.agentProfileProtected", "Agent profile is protected", detail: detail);

    public static AppServerException AgentProfileSourceUnavailable(string detail) =>
        Create(AgentProfileSourceUnavailableCode, "AgentProfileSourceUnavailable", "errors.agentProfileSourceUnavailable", "Agent profile source is unavailable", detail: detail);

    public static AppServerException AgentProfileConflict(string detail) =>
        Create(AgentProfileConflictCode, "AgentProfileConflict", "errors.agentProfileConflict", "Agent profile conflict", detail: detail);

    public static AppServerException TaskAlreadyExists(string taskId) =>
        Create(TaskAlreadyExistsCode, "TaskAlreadyExists", "errors.taskAlreadyExists", $"Task already exists: {taskId}", new { taskId });

    public static AppServerException TaskNotFound(string taskId) =>
        Create(TaskNotFoundCode, "TaskNotFound", "errors.taskNotFound", $"Task not found: {taskId}", new { taskId });

    public static AppServerException TaskInvalidStatus(string detail) =>
        Create(TaskInvalidStatusCode, "TaskInvalidStatus", "errors.taskInvalidStatus", detail, detail: detail);

}
