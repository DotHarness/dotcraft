using System.Collections.Concurrent;
using System.Text.Json;

namespace DotCraft.CodexGateway.Workspace;

internal sealed partial class WorkspaceProjector
{
    private readonly ConcurrentDictionary<string, string> _sourceItemKeys = new();

    private static bool IsExec(JsonElement p) =>
        p.Get("source").Text("kind") == "CoreNative" && p.Get("source").Text("sourceId") == "core-native" &&
        p.Get("source").Text("sourceToolId") == "Exec";

    private static bool IsFileMutation(JsonElement p) =>
        p.Get("source").Text("kind") == "CoreNative" && p.Get("source").Text("sourceId") == "core-native" &&
        p.Get("source").Text("sourceToolId") is "WriteFile" or "EditFile";

    internal string ItemId(string thread, string turn, JsonElement source)
    {
        var p = source.Get("payload");
        var key = source.Required("id");
        if (p.Text("callId") is { Length: > 0 } call)
            key = source.Text("type") == "commandExecution" || IsExec(p) ? "command:" + call : "call:" + call;
        else if (source.Text("type") == "approvalRequest" && p.Text("requestId") is { } request)
            key = "approval:" + request;
        _sourceItemKeys[thread + "\n" + turn + "\n" + source.Required("id")] = key;
        return routes.External("item", thread, turn, key);
    }

    internal string DeltaItemId(string thread, string turn, string sourceId) =>
        routes.External("item", thread, turn, _sourceItemKeys.GetValueOrDefault(thread + "\n" + turn + "\n" + sourceId) ?? sourceId);

    internal static string LifecycleStatus(JsonElement p) => p.Text("status") switch
    {
        "completed" => "completed", "failed" or "declined" or "cancelled" => "failed", _ => "inProgress"
    };

    private static void FileChange(Dictionary<string, object?> result, JsonElement p)
    {
        var content = p.Get("structuredContent");
        result["type"] = "fileChange";
        result["changes"] = content.Get("changes").Rows()
            .Where(change => change.Text("diff") is not null)
            .Select(change => new
            {
                path = change.Required("path"),
                kind = change.Text("kind") == "add"
                    ? (object)new { type = "add" }
                    : new { type = "update", move_path = (string?)null },
                diff = change.Required("diff")
            }).ToArray();
        result["status"] = content.Text("writeState") == "applied" ? "completed" : "failed";
    }

    private static void McpCall(Dictionary<string, object?> result, JsonElement p)
    {
        result["type"] = "mcpToolCall";
        result["server"] = p.Required("server");
        result["tool"] = p.Required("toolName");
        result["status"] = LifecycleStatus(p);
        result["arguments"] = p.Get("arguments").Value() ?? new { };
        result["appContext"] = null;
        result["mcpAppUi"] = null;
        result["pluginId"] = p.Get("source").Text("pluginId");
        result["readOnlyHint"] = null;
        result["result"] = p.Text("status") is "completed" or "failed" ? new
        {
            content = SafeMcpContent(p), structuredContent = (object?)null, _meta = (object?)null
        } : null;
        result["error"] = p.Text("errorMessage") is { } error ? new { message = error } : null;
        result["durationMs"] = p.Get("durationMs").Value();
    }

    private static object[] SafeMcpContent(JsonElement p)
    {
        var normalized = p.Get("modelContentItems");
        if (normalized.ValueKind == JsonValueKind.Array)
            return normalized.Rows().Select(item => item.Text("type") switch
            {
                "text" => (object)new { type = "text", text = item.Text("text") ?? "" },
                "image" when item.Text("dataBase64") is { } data => new { type = "image", data, mimeType = item.Text("mediaType") },
                _ => new { type = "text", text = "[DotCraft unsupported media output]" }
            }).ToArray();
        return p.Get("content").Rows().Where(item => item.Text("type") == "text")
            .Select(item => (object)new { type = "text", text = item.Text("text") ?? "" }).ToArray();
    }

    private static void DynamicCall(Dictionary<string, object?> result, JsonElement p)
    {
        result["type"] = "dynamicToolCall";
        result["namespace"] = p.Text("namespace");
        result["tool"] = p.Required("toolName");
        result["arguments"] = p.Get("arguments").Value() ?? new { };
        result["status"] = LifecycleStatus(p);
        result["contentItems"] = p.Get("contentItems").ValueKind == JsonValueKind.Array
            ? OutputContent(p.Get("contentItems"), null) : null;
        result["success"] = p.Get("success").Value();
        result["durationMs"] = p.Get("durationMs").Value();
    }
}
