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
        string? hookEvent = null,
        IReadOnlyCollection<string>? requiredPlugins = null)
    {
        JsonObject? merged = additionalMeta is null ? null : (JsonObject)additionalMeta.DeepClone();

        if (context is not null)
        {
            merged ??= new JsonObject();
            var callContext = new JsonObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["threadId"] = context.ThreadId,
                ["turnId"] = context.TurnId,
                ["sessionId"] = context.ThreadId,
                ["callId"] = context.CallId,
                ["isSubagent"] = context.IsSubagent,
                ["parentThreadId"] = context.ParentThreadId
            };

            if (requiredPlugins is { Count: > 0 } && context.EnabledPlugins is not null)
            {
                var allowed = new HashSet<string>(requiredPlugins, StringComparer.OrdinalIgnoreCase);
                var matched = new JsonArray();
                foreach (var pluginId in context.EnabledPlugins
                             .Where(id => !string.IsNullOrWhiteSpace(id) && allowed.Contains(id))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase))
                {
                    matched.Add(pluginId);
                }
                callContext["enabledPlugins"] = matched;
            }

            merged[CallContextKey] = callContext;
        }

        if (!string.IsNullOrWhiteSpace(hookEvent))
        {
            merged ??= new JsonObject();
            merged[HookEventKey] = hookEvent;
        }

        return merged;
    }
}
