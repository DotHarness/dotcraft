using System.Text.Json.Nodes;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

/// <summary>Selects the Items a Turn summary keeps, as defined by the thread history loading spec.</summary>
internal static class TurnSummarySelector
{
    public const int MaxSummaryTools = 32;

    public static IEnumerable<SessionWireItem> Select(
        IReadOnlyList<SessionWireItem> items,
        IReadOnlyList<Contract.ThreadSummaryTool> summaryTools)
    {
        var keptCalls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            switch (item.Payload)
            {
                case ToolResultPayload result when IsFileChange(result.StructuredContent):
                    keptCalls.Add(result.CallId);
                    break;
                case ToolCallPayload call when Named(summaryTools, call.Namespace, call.ToolName):
                    keptCalls.Add(call.CallId);
                    break;
            }
        }

        return items.Where(item => item.Type switch
        {
            ItemType.UserMessage or ItemType.AgentMessage or ItemType.Error or ItemType.SystemNotice or ItemType.WebSearch => true,
            ItemType.ImageGeneration => item.Payload is ImageGenerationPayload { Status: not "inProgress" },
            ItemType.McpToolCall => item.Status == ItemStatus.Completed
                && item.Payload is McpToolCallPayload { McpAppResourceUri: { } uri }
                && uri.StartsWith("ui://", StringComparison.Ordinal),
            ItemType.DynamicToolCall => item.Payload is DynamicToolCallPayload dynamic
                && Named(summaryTools, dynamic.Namespace, dynamic.ToolName),
            ItemType.ToolCall => item.Payload is ToolCallPayload call && keptCalls.Contains(call.CallId),
            ItemType.ToolResult => item.Payload is ToolResultPayload result && keptCalls.Contains(result.CallId),
            _ => false
        });
    }

    private static bool Named(IReadOnlyList<Contract.ThreadSummaryTool> tools, string? ns, string name) =>
        tools.Any(tool => string.Equals(tool.Name, name, StringComparison.Ordinal)
            && string.Equals(tool.Namespace ?? "", ns ?? "", StringComparison.Ordinal));

    private static bool IsFileChange(JsonNode? structuredContent) =>
        structuredContent is JsonObject content
        && content["kind"] is JsonValue kind
        && kind.TryGetValue<string>(out var value)
        && value == "fileChange";
}
