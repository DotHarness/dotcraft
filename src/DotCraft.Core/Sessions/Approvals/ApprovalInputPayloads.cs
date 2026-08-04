namespace DotCraft.Sessions;

/// <summary>
/// Payload for ApprovalRequest items.
/// </summary>
public sealed record ApprovalRequestPayload
{
    /// <summary>
    /// "file" or "shell"
    /// </summary>
    public string ApprovalType { get; init; } = string.Empty;

    /// <summary>
    /// For file: "read", "write", "edit", "list". For shell: the command.
    /// </summary>
    public string Operation { get; init; } = string.Empty;

    /// <summary>
    /// For file: the path. For shell: the working directory.
    /// </summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>
    /// Unique ID for correlating with ApprovalResponse.
    /// </summary>
    public string RequestId { get; init; } = string.Empty;

    /// <summary>
    /// Session-scoped cache key for repeated approvals of the same class of operation.
    /// </summary>
    public string ScopeKey { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable explanation of why this approval is needed, shown to the user in approval UIs.
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// UTC instant after which the request can no longer be approved.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// Payload for ApprovalResponse items.
/// </summary>
public sealed record ApprovalResponsePayload
{
    /// <summary>
    /// Matches the ApprovalRequest.RequestId.
    /// </summary>
    public string RequestId { get; init; } = string.Empty;

    public bool Approved { get; init; }

    /// <summary>
    /// Rich decision captured for the request.
    /// </summary>
    public SessionApprovalDecision Decision { get; init; } = SessionApprovalDecision.Reject;
}

/// <summary>
/// A single selectable answer option for a model-initiated user input request.
/// </summary>
public sealed class RequestUserInputQuestionOption
{
    public string Label { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// A short question that the agent asks the user while a turn is paused.
/// </summary>
public sealed class RequestUserInputQuestion
{
    public string Id { get; set; } = string.Empty;

    public string Header { get; set; } = string.Empty;

    public string Question { get; set; } = string.Empty;

    public bool IsOther { get; set; } = true;

    public bool IsSecret { get; set; }

    public List<RequestUserInputQuestionOption> Options { get; set; } = [];
}

/// <summary>
/// Payload for UserInputRequest items.
/// </summary>
public sealed record UserInputRequestPayload
{
    public string RequestId { get; init; } = string.Empty;

    public IReadOnlyList<RequestUserInputQuestion> Questions { get; init; } = [];
}

/// <summary>
/// A response for one question in a model-initiated user input request.
/// </summary>
public sealed class RequestUserInputAnswer
{
    public List<string> Answers { get; set; } = [];
}

/// <summary>
/// Response object returned by clients and by the RequestUserInput tool.
/// </summary>
public sealed class RequestUserInputResponse
{
    public Dictionary<string, RequestUserInputAnswer> Answers { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Payload for UserInputResponse items.
/// </summary>
public sealed record UserInputResponsePayload
{
    public string RequestId { get; init; } = string.Empty;

    public RequestUserInputResponse Response { get; init; } = new();
}
