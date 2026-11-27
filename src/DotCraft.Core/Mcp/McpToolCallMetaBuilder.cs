using System.Text.Json.Nodes;
using DotCraft.Tools;

namespace DotCraft.Mcp;

public static class McpToolCallMetaBuilder
{
    public const string CallContextKey = "dotcraft/callContext";
    public const string HookEventKey = "dotcraft/hookEvent";
    public const int ProtocolVersion = 1;

    public static JsonObject? Build(
        ToolInvocationContext? context,
        JsonObject? additionalMeta = null,
        string? hookEvent = null)
    {
        JsonObject? merged = additionalMeta is null ? null : (JsonObject)additionalMeta.DeepClone();

        if (context is not null)
        {
            merged ??= new JsonObject();
            merged[CallContextKey] = new JsonObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["threadId"] = context.ThreadId,
                ["turnId"] = context.TurnId,
                ["sessionId"] = context.ThreadId,
                ["callId"] = context.CallId,
                ["isSubagent"] = context.IsSubagent,
                ["parentThreadId"] = context.ParentThreadId
            };
        }

        if (!string.IsNullOrWhiteSpace(hookEvent))
        {
            merged ??= new JsonObject();
            merged[HookEventKey] = hookEvent;
        }

        return merged;
    }
}
