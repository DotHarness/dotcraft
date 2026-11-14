using System.Collections.Concurrent;
using System.Text.Json;

namespace DotCraft.CodexGateway.Workspace;

internal sealed partial class WorkspaceProjector
{
    private sealed class Operation
    {
        public readonly object Gate = new();
        public Dictionary<string, object?>? Item;
        public int Rank;
        public string? InvocationId;
        public string? FirstSourceId;
        public JsonElement Source;
        public JsonElement Invocation;
        public string? VisibleSourceId;
    }

    private readonly ConcurrentDictionary<string, Operation> _operations = new();

    private static int OperationRank(JsonElement source) => source.Text("type") switch
    {
        "toolCall" => 0, "toolResult" => 1, _ => 2
    };

    private Dictionary<string, object?> MergeOperation(
        string thread, string turn, JsonElement source, Dictionary<string, object?> item)
    {
        if (source.Get("payload").Text("callId") is null) return item;
        var operation = _operations.GetOrAdd((string)item["id"]!, _ => new());
        lock (operation.Gate)
        {
            operation.FirstSourceId ??= source.Required("id");
            if (source.Text("type") == "toolCall")
            {
                operation.InvocationId = source.Required("id");
                operation.Invocation = source;
            }
            if (operation.Item is not { } previous)
            {
                operation.Item = item;
                operation.Rank = OperationRank(source);
                operation.Source = source;
                return new(item);
            }
            var rank = OperationRank(source);
            if (rank >= operation.Rank)
            {
                if (previous.GetValueOrDefault("arguments") is { } arguments
                    && (source.Text("type") == "toolResult" || item.GetValueOrDefault("arguments") is null))
                    item["arguments"] = arguments;
                if (previous.GetValueOrDefault("startedAtMs") is { } start) item["startedAtMs"] = start;
                if (previous.GetValueOrDefault("status") is "completed" or "failed" && item.GetValueOrDefault("status") is "inProgress")
                    return new(previous);
                operation.Item = item;
                operation.Rank = rank;
                operation.Source = source;
            }
            else if (item.TryGetValue("arguments", out var arguments))
            {
                previous["arguments"] = arguments;
                if (source.Text("type") == "toolCall") previous["startedAtMs"] = item["startedAtMs"];
            }
            return new(operation.Item);
        }
    }

    internal Dictionary<string, object?> LatestOperation(Dictionary<string, object?> item)
    {
        if (!_operations.TryGetValue((string)item["id"]!, out var operation)) return item;
        lock (operation.Gate) return operation.Item is null ? item : new(operation.Item);
    }

    internal JsonElement OutputSource(JsonElement source, Dictionary<string, object?> item)
    {
        if (!_operations.TryGetValue((string)item["id"]!, out var operation)) return source;
        lock (operation.Gate) return operation.Source;
    }

    internal bool IsInvocation(string thread, string turn, JsonElement source)
    {
        var call = source.Get("payload").Text("callId");
        if (call is null) return true;
        var operation = _operations.GetValueOrDefault(ItemId(thread, turn, source));
        if (operation is null) return true;
        lock (operation.Gate)
            return (operation.VisibleSourceId ?? operation.InvocationId ?? operation.FirstSourceId) == source.Required("id");
    }

    private void ToolInvocation(Dictionary<string, object?> result, JsonElement p)
    {
        if (IsExec(p))
        {
            result["type"] = "commandExecution";
            result["command"] = p.Get("arguments").Text("command") ?? "";
            result["cwd"] = p.Get("arguments").Text("workingDir") ?? workspace;
            result["processId"] = null;
            result["source"] = "agent";
            result["commandActions"] = CommandActions(p.Get("arguments").Text("command") ?? "");
            result["status"] = "inProgress";
            result["aggregatedOutput"] = null;
            result["exitCode"] = null;
            result["durationMs"] = null;
        }
        else if (IsFileMutation(p))
        {
            result["type"] = "fileChange";
            result["changes"] = Array.Empty<object>();
            result["status"] = "inProgress";
        }
        else
        {
            result["type"] = "dynamicToolCall";
            result["namespace"] = p.Text("namespace");
            result["tool"] = p.Required("toolName");
            result["arguments"] = p.Get("arguments").Value() ?? new { };
            result["status"] = "inProgress";
            result["contentItems"] = null;
            result["success"] = null;
            result["durationMs"] = null;
        }
    }

    private void ToolOutput(Dictionary<string, object?> result, JsonElement p)
    {
        if (p.Get("structuredContent").Text("kind") == "fileChange")
        {
            FileChange(result, p);
            return;
        }
        ToolInvocation(result, p);
        var failed = p.Get("success").ValueKind == JsonValueKind.False
            || p.Text("errorCode") is not null || p.Text("errorMessage") is not null
            || p.Get("source").Text("kind") == "CoreNative" && p.Get("source").Text("sourceId") == "code-mode"
                && p.Get("structuredContent").Text("status") == "failed";
        result["status"] = failed ? "failed" : "completed";
        if (IsExec(p))
        {
            result["aggregatedOutput"] = p.Text("result");
            result["durationMs"] = p.Get("durationMs").Value();
        }
        else if (!IsFileMutation(p))
        {
            result["contentItems"] = OutputContent(p.Get("contentItems"), p.Text("result"));
            result["success"] = failed ? false : p.Get("success").Value();
            result["durationMs"] = p.Get("durationMs").Value();
        }
    }

    private static object[] OutputContent(JsonElement content, string? fallback) => content.ValueKind == JsonValueKind.Array
        ? content.Rows().Select(item => item.Text("type") switch
        {
            "image" when item.Text("url") is { } url => (object)new { type = "inputImage", imageUrl = url },
            "image" when item.Text("dataBase64") is { } data => new
            {
                type = "inputImage", imageUrl = "data:" + (item.Text("mediaType") ?? "image/png") + ";base64," + data
            },
            _ => new { type = "inputText", text = item.Text("text") ?? "[DotCraft unsupported media output]" }
        }).ToArray()
        : fallback is null ? [] : [new { type = "inputText", text = fallback }];

    private static void WebSearch(Dictionary<string, object?> result, JsonElement p)
    {
        if (p.Text("errorMessage") is null && p.Get("results").Rows().Any()
            && p.Get("action").Text("type") is "search" or "openPage" or "findInPage")
        {
            result["type"] = "webSearch";
            result["query"] = p.Text("query") ?? "";
            result["action"] = p.Get("action").Value();
            result["results"] = p.Get("results").Value();
            return;
        }
        result["type"] = "dynamicToolCall";
        result["namespace"] = null;
        result["tool"] = "webSearch";
        result["arguments"] = p.Get("arguments").Value() ?? new { };
        result["status"] = LifecycleStatus(p);
        result["contentItems"] = OutputContent(default, p.Text("output") ?? p.Text("errorMessage"));
        result["success"] = p.Text("status") == "completed" ? true : p.Text("status") == "failed" ? false : (bool?)null;
        result["durationMs"] = null;
    }

    private static void ImageGeneration(Dictionary<string, object?> result, JsonElement p)
    {
        if (p.Text("errorMessage") is { } error)
        {
            result["type"] = "dynamicToolCall";
            result["namespace"] = null;
            result["tool"] = "imageGeneration";
            result["arguments"] = new { revisedPrompt = p.Text("revisedPrompt") };
            result["status"] = "failed";
            result["contentItems"] = OutputContent(default, error);
            result["success"] = false;
            result["durationMs"] = null;
            return;
        }
        result["type"] = "imageGeneration";
        result["status"] = p.Text("status") ?? "inProgress";
        result["revisedPrompt"] = p.Text("revisedPrompt");
        result["result"] = p.Text("result") ?? "";
        result["failure"] = null;
        result["generationId"] = p.Text("generationId");
    }
}
