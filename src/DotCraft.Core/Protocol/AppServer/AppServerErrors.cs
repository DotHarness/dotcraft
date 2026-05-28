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
    // ── Automation-specific codes (-32050 to -32059) ──

    public const int TaskNotFoundCode = -32051;
    public const int TaskInvalidStatusCode = -32052;
    public const int TaskAlreadyExistsCode = -32054;

    // ── Factory methods ──

    public static AppServerException ParseError(string? detail = null) =>
        new(ParseErrorCode, "Parse error", detail is null ? null : new { detail });

    public static AppServerException InvalidRequest(string detail) =>
        new(InvalidRequestCode, "Invalid request", new { detail });

    public static AppServerException MethodNotFound(string method) =>
        new(MethodNotFoundCode, $"Method not found: {method}");

    public static AppServerException InvalidParams(string detail) =>
        new(InvalidParamsCode, "Invalid params", new { detail });

    public static AppServerException InternalError(string detail) =>
        new(InternalErrorCode, "Internal error", new { detail });

    public static AppServerException ServerOverloaded() =>
        new(ServerOverloadedCode, "Server overloaded; retry later.");

    public static AppServerException NotInitialized() =>
        new(NotInitializedCode, "Not initialized");

    public static AppServerException AlreadyInitialized() =>
        new(AlreadyInitializedCode, "Already initialized");

    public static AppServerException ThreadNotFound(string threadId) =>
        new(ThreadNotFoundCode, $"Thread not found: {threadId}");

    public static AppServerException ThreadNotActive(string threadId) =>
        new(ThreadNotActiveCode, $"Thread is not active: {threadId}");

    public static AppServerException TurnInProgress(string threadId) =>
        new(TurnInProgressCode, $"A turn is already in progress on thread: {threadId}");

    public static AppServerException TurnNotFound(string turnId) =>
        new(TurnNotFoundCode, $"Turn not found: {turnId}");

    public static AppServerException TurnNotRunning(string turnId) =>
        new(TurnNotRunningCode, $"Turn is not running: {turnId}");

    public static AppServerException ApprovalTimeout() =>
        new(ApprovalTimeoutCode, "Approval request timed out");

    public static AppServerException ChannelRejected(string channelName) =>
        new(ChannelRejectedCode, $"Channel adapter rejected: '{channelName}' is not registered in server configuration");

    public static AppServerException CronJobNotFound(string jobId) =>
        new(CronJobNotFoundCode, $"Cron job not found: {jobId}");

    public static AppServerException SkillNotFound(string name) =>
        new(SkillNotFoundCode, $"Skill not found: {name}");

    public static AppServerException CommandNotFound(string command) =>
        new(CommandNotFoundCode, $"Command not found: {command}");

    public static AppServerException CommandPermissionDenied(string command) =>
        new(CommandPermissionDeniedCode, $"Permission denied for command: {command}");

    public static AppServerException CommandServiceUnavailable(string command) =>
        new(CommandServiceUnavailableCode, $"Service unavailable for command: {command}");

    public static AppServerException McpServerNotFound(string name) =>
        new(McpServerNotFoundCode, $"MCP server not found: {name}");

    public static AppServerException McpServerValidationFailed(string detail) =>
        new(McpServerValidationFailedCode, "MCP server validation failed", new { detail });

    public static AppServerException McpServerTestFailed(string detail) =>
        new(McpServerTestFailedCode, "MCP server test failed", new { detail });

    public static AppServerException McpServerNameConflict(string detail) =>
        new(McpServerNameConflictCode, "MCP server name conflict", new { detail });

    public static AppServerException McpServerReadOnly(string name) =>
        new(McpServerReadOnlyCode, $"MCP server is read-only: {name}");

    public static AppServerException ExternalChannelNotFound(string name) =>
        new(ExternalChannelNotFoundCode, $"External channel not found: {name}");

    public static AppServerException ExternalChannelValidationFailed(string detail) =>
        new(ExternalChannelValidationFailedCode, "External channel validation failed", new { detail });

    public static AppServerException ExternalChannelNameConflict(string detail) =>
        new(ExternalChannelNameConflictCode, "External channel name conflict", new { detail });

    public static AppServerException SubAgentProfileNotFound(string name) =>
        new(SubAgentProfileNotFoundCode, $"SubAgent profile not found: {name}");

    public static AppServerException SubAgentProfileValidationFailed(string detail) =>
        new(SubAgentProfileValidationFailedCode, "SubAgent profile validation failed", new { detail });

    public static AppServerException SubAgentProfileProtected(string detail) =>
        new(SubAgentProfileProtectedCode, "SubAgent profile is protected", new { detail });

    public static AppServerException TaskAlreadyExists(string taskId) =>
        new(TaskAlreadyExistsCode, $"Task already exists: {taskId}");

    public static AppServerException TaskNotFound(string taskId) =>
        new(TaskNotFoundCode, $"Task not found: {taskId}");

    public static AppServerException TaskInvalidStatus(string detail) =>
        new(TaskInvalidStatusCode, detail);

}
