using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

internal interface IAcpThreadBoundParams
{
    string ThreadId { get; set; }
}

/// <summary>Parameters for <c>ext/acp/fs/readTextFile</c>.</summary>
public sealed class AcpFsReadTextFileParams : IAcpThreadBoundParams
{
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
    [JsonPropertyName("offset")] public int? Offset { get; set; }
    [JsonPropertyName("limit")] public int? Limit { get; set; }
    [JsonPropertyName("threadId")] public string ThreadId { get; set; } = string.Empty;
}

/// <summary>Result for <c>ext/acp/fs/readTextFile</c>.</summary>
public sealed class AcpFsReadTextFileResult
{
    [JsonPropertyName("content")] public string? Content { get; set; }
}

/// <summary>Parameters for <c>ext/acp/fs/writeTextFile</c>.</summary>
public sealed class AcpFsWriteTextFileParams : IAcpThreadBoundParams
{
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    [JsonPropertyName("threadId")] public string ThreadId { get; set; } = string.Empty;
}

/// <summary>Result for <c>ext/acp/fs/writeTextFile</c>.</summary>
public sealed class AcpFsWriteTextFileResult
{
    [JsonPropertyName("success")] public bool Success { get; set; }
}

/// <summary>Parameters for <c>ext/acp/terminal/create</c>.</summary>
public sealed class AcpTerminalCreateParams : IAcpThreadBoundParams
{
    [JsonPropertyName("command")] public string Command { get; set; } = string.Empty;
    [JsonPropertyName("cwd")] public string? Cwd { get; set; }
    [JsonPropertyName("env")] public Dictionary<string, string>? Env { get; set; }
    [JsonPropertyName("threadId")] public string ThreadId { get; set; } = string.Empty;
}

/// <summary>Result for <c>ext/acp/terminal/create</c>.</summary>
public sealed class AcpTerminalCreateResult
{
    [JsonPropertyName("terminalId")] public string? TerminalId { get; set; }
}

/// <summary>Parameters for terminal output requests.</summary>
public sealed class AcpTerminalGetOutputParams : IAcpThreadBoundParams
{
    [JsonPropertyName("terminalId")] public string TerminalId { get; set; } = string.Empty;
    [JsonPropertyName("threadId")] public string ThreadId { get; set; } = string.Empty;
}

/// <summary>Parameters for <c>ext/acp/terminal/waitForExit</c>.</summary>
public sealed class AcpTerminalWaitForExitParams : IAcpThreadBoundParams
{
    [JsonPropertyName("terminalId")] public string TerminalId { get; set; } = string.Empty;
    [JsonPropertyName("timeout")] public int? Timeout { get; set; }
    [JsonPropertyName("threadId")] public string ThreadId { get; set; } = string.Empty;
}

/// <summary>Parameters for <c>ext/acp/terminal/kill</c>.</summary>
public sealed class AcpTerminalKillParams : IAcpThreadBoundParams
{
    [JsonPropertyName("terminalId")] public string TerminalId { get; set; } = string.Empty;
    [JsonPropertyName("threadId")] public string ThreadId { get; set; } = string.Empty;
}

/// <summary>Parameters for <c>ext/acp/terminal/release</c>.</summary>
public sealed class AcpTerminalReleaseParams : IAcpThreadBoundParams
{
    [JsonPropertyName("terminalId")] public string TerminalId { get; set; } = string.Empty;
    [JsonPropertyName("threadId")] public string ThreadId { get; set; } = string.Empty;
}

/// <summary>Result for ACP terminal output and wait requests.</summary>
public sealed class AcpTerminalOutputResult
{
    [JsonPropertyName("output")] public string? Output { get; set; }
    [JsonPropertyName("exitCode")] public int? ExitCode { get; set; }
}

/// <summary>Parameters for <c>ext/nodeRepl/evaluate</c>.</summary>
public sealed class NodeReplEvaluateParams
{
    [JsonPropertyName("threadId")] public string ThreadId { get; set; } = string.Empty;
    [JsonPropertyName("turnId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TurnId { get; set; }
    [JsonPropertyName("evaluationId")] public string EvaluationId { get; set; } = string.Empty;
    [JsonPropertyName("browserSession")] public NodeReplBrowserSessionParams BrowserSession { get; set; } = new();
    [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;
    [JsonPropertyName("timeoutMs")] public int TimeoutMs { get; set; }
}

/// <summary>Browser-session routing nested in a Node REPL evaluation request.</summary>
public sealed class NodeReplBrowserSessionParams
{
    [JsonPropertyName("protocolVersion")] public int ProtocolVersion { get; set; }
    [JsonPropertyName("sessionId")] public string SessionId { get; set; } = string.Empty;
    [JsonPropertyName("threadId")] public string ThreadId { get; set; } = string.Empty;
    [JsonPropertyName("turnId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TurnId { get; set; }
    [JsonPropertyName("evaluationId")] public string EvaluationId { get; set; } = string.Empty;
}

/// <summary>Parameters for <c>ext/nodeRepl/cancel</c>.</summary>
public sealed class NodeReplCancelParams
{
    [JsonPropertyName("threadId")] public string ThreadId { get; set; } = string.Empty;
    [JsonPropertyName("evaluationId")] public string EvaluationId { get; set; } = string.Empty;
}
