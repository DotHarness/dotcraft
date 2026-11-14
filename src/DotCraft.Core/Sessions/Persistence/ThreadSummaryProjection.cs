using System.Text.Json.Nodes;

namespace DotCraft.Sessions;

[Flags]
internal enum ThreadSummaryKind
{
    None = 0,
    Retained = 1,
    ToolCall = 2,
    ToolResult = 4,
    DynamicToolCall = 8,
    FileChange = 16,
    ToolExecution = 32
}

internal readonly record struct ThreadSummaryProjection(
    ThreadSummaryKind Kind,
    string? CallId = null,
    string? Namespace = null,
    string? ToolName = null)
{
    public static ThreadSummaryProjection From(SessionItem item) => item.Type switch
    {
        ItemType.UserMessage or ItemType.AgentMessage or ItemType.Error or ItemType.SystemNotice or ItemType.WebSearch =>
            new(ThreadSummaryKind.Retained, item.AsWebSearch?.CallId),
        ItemType.ImageGeneration when item.AsImageGeneration is { } image =>
            new(image.Status != "inProgress" ? ThreadSummaryKind.Retained : ThreadSummaryKind.None, image.CallId),
        ItemType.McpToolCall when item.AsMcpToolCall is { } mcp =>
            new(item.Status == ItemStatus.Completed && mcp.McpAppResourceUri is { } uri
                && uri.StartsWith("ui://", StringComparison.Ordinal) ? ThreadSummaryKind.Retained : ThreadSummaryKind.None,
                mcp.CallId),
        ItemType.CommandExecution when item.AsCommandExecution is { } command =>
            new(ThreadSummaryKind.None, command.CallId),
        ItemType.ToolCall when item.AsToolCall is { } call =>
            new(ThreadSummaryKind.ToolCall, call.CallId, call.Namespace, call.ToolName),
        ItemType.DynamicToolCall when item.AsDynamicToolCall is { } call =>
            new(ThreadSummaryKind.DynamicToolCall, call.CallId, call.Namespace, call.ToolName),
        ItemType.ToolResult when item.AsToolResult is { } result =>
            new(ThreadSummaryKind.ToolResult | (IsFileChange(result.StructuredContent) ? ThreadSummaryKind.FileChange : 0),
                result.CallId),
        ItemType.ToolExecution => new(ThreadSummaryKind.ToolExecution),
        _ => new(ThreadSummaryKind.None)
    };

    private static bool IsFileChange(JsonNode? content) =>
        content is JsonObject obj
        && obj["kind"] is JsonValue kind
        && kind.TryGetValue<string>(out var value)
        && value == "fileChange";
}
