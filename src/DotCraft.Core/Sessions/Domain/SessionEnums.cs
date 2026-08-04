using System.Text.Json.Serialization;

namespace DotCraft.Sessions;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ThreadStatus
{
    Active,
    Paused,
    Archived
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TurnStatus
{
    Running,
    Completed,
    WaitingApproval,
    WaitingInput,
    Failed,
    Cancelled
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ItemType
{
    UserMessage,
    AgentMessage,
    ReasoningContent,
    CommandExecution,
    ToolExecution,
    ImageGeneration,
    ToolCall,
    McpToolCall,
    DynamicToolCall,
    ToolResult,
    ApprovalRequest,
    ApprovalResponse,
    UserInputRequest,
    UserInputResponse,
    Error,
    SystemNotice
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ItemStatus
{
    Started,
    Streaming,
    Completed
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SessionEventType
{
    ThreadCreated,
    ThreadResumed,
    ThreadStatusChanged,
    ThreadQueueUpdated,
    TurnStarted,
    TurnCompleted,
    TurnFailed,
    TurnCancelled,
    ItemStarted,
    ItemDelta,
    ItemCompleted,
    ApprovalRequested,
    ApprovalResolved,
    UserInputRequested,
    UserInputResolved,
    SubAgentProgress,
    UsageDelta,
    SystemEvent
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HistoryMode
{
    Server,
    Client
}
