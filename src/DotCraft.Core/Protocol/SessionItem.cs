using System.Text.Json.Serialization;

namespace DotCraft.Protocol;

/// <summary>
/// An Item is the atomic unit of input/output within a Turn.
/// Every piece of information exchanged between the user, agent, and tools
/// is represented as an Item with a typed payload and an explicit lifecycle.
/// </summary>
public sealed class SessionItem
{
    /// <summary>
    /// Unique within the Turn. Format: item_{3-digit-sequence} (e.g., item_001).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Reference to the parent Turn.
    /// </summary>
    public string TurnId { get; set; } = string.Empty;

    public ItemType Type { get; set; }

    public ItemStatus Status { get; set; }

    /// <summary>
    /// Type-specific payload. The concrete type corresponds to the ItemType value.
    /// Use the typed accessor methods to retrieve the payload with the correct type.
    /// </summary>
    public object? Payload { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    // Typed payload accessors

    [JsonIgnore]
    public UserMessagePayload? AsUserMessage =>
        Payload as UserMessagePayload;

    [JsonIgnore]
    public AgentMessagePayload? AsAgentMessage =>
        Payload as AgentMessagePayload;

    [JsonIgnore]
    public ReasoningContentPayload? AsReasoningContent =>
        Payload as ReasoningContentPayload;

    [JsonIgnore]
    public CommandExecutionPayload? AsCommandExecution =>
        Payload as CommandExecutionPayload;

    [JsonIgnore]
    public ToolExecutionPayload? AsToolExecution =>
        Payload as ToolExecutionPayload;

    [JsonIgnore]
    public ImageGenerationPayload? AsImageGeneration =>
        Payload as ImageGenerationPayload;

    [JsonIgnore]
    public ToolCallPayload? AsToolCall =>
        Payload as ToolCallPayload;

    [JsonIgnore]
    public PluginFunctionCallPayload? AsPluginFunctionCall =>
        Payload as PluginFunctionCallPayload;

    [JsonIgnore]
    public McpToolCallPayload? AsMcpToolCall =>
        Payload as McpToolCallPayload;

    [JsonIgnore]
    public DynamicToolCallPayload? AsDynamicToolCall =>
        Payload as DynamicToolCallPayload;

    [JsonIgnore]
    public ToolResultPayload? AsToolResult =>
        Payload as ToolResultPayload;

    [JsonIgnore]
    public ApprovalRequestPayload? AsApprovalRequest =>
        Payload as ApprovalRequestPayload;

    [JsonIgnore]
    public ApprovalResponsePayload? AsApprovalResponse =>
        Payload as ApprovalResponsePayload;

    [JsonIgnore]
    public UserInputRequestPayload? AsUserInputRequest =>
        Payload as UserInputRequestPayload;

    [JsonIgnore]
    public UserInputResponsePayload? AsUserInputResponse =>
        Payload as UserInputResponsePayload;

    [JsonIgnore]
    public ErrorPayload? AsError =>
        Payload as ErrorPayload;

    [JsonIgnore]
    public SystemNoticePayload? AsSystemNotice =>
        Payload as SystemNoticePayload;
}
