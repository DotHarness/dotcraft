using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Protocol.AppServer;

namespace DotCraft.CLI;

internal sealed class ExecJsonEvents(TextWriter output)
{
    private static readonly JsonSerializerOptions LineOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly HashSet<string> _startedItemIds = new(StringComparer.Ordinal);
    private string? _todoListId;
    private JsonArray? _todoItems;

    public void ThreadStarted(string threadId) =>
        Emit(new JsonObject { ["type"] = "thread.started", ["thread_id"] = threadId });

    public void Error(string message) =>
        Emit(new JsonObject { ["type"] = "error", ["message"] = message });

    public void Project(JsonElement notification, string turnId)
    {
        if (!notification.TryGetProperty("method", out var method)
            || !notification.TryGetProperty("params", out var @params))
            return;

        switch (method.GetString())
        {
            case AppServerMethodNames.TurnStarted:
                Emit(new JsonObject { ["type"] = "turn.started" });
                break;
            case AppServerMethodNames.ItemStarted:
                ItemStarted(@params);
                break;
            case AppServerMethodNames.ItemCompleted:
                ItemCompleted(@params);
                break;
            case AppServerMethodNames.PlanUpdated:
                PlanUpdated(@params, turnId);
                break;
            case AppServerMethodNames.TurnCompleted:
                TurnEnded(@params);
                Emit(new JsonObject { ["type"] = "turn.completed", ["usage"] = Usage(@params) });
                break;
            case AppServerMethodNames.TurnFailed:
                TurnEnded(@params);
                Emit(new JsonObject
                {
                    ["type"] = "turn.failed",
                    ["error"] = new JsonObject { ["message"] = OneShotNotification.ReadFailure(@params) }
                });
                break;
            case AppServerMethodNames.TurnCancelled:
                TurnEnded(@params);
                break;
        }
    }

    private void ItemStarted(JsonElement @params)
    {
        if (!@params.TryGetProperty("item", out var item)
            || ReadString(item, "type") is "agentMessage" or "reasoningContent"
            || MapItem(item) is not { } mapped)
            return;

        _startedItemIds.Add(mapped["id"]!.GetValue<string>());
        Emit(new JsonObject { ["type"] = "item.started", ["item"] = mapped });
    }

    private void ItemCompleted(JsonElement @params)
    {
        if (!@params.TryGetProperty("item", out var item))
            return;

        CompleteItem(item);
    }

    private void CompleteItem(JsonElement item)
    {
        if (MapItem(item) is not { } mapped)
            return;

        _startedItemIds.Remove(mapped["id"]!.GetValue<string>());
        Emit(new JsonObject { ["type"] = "item.completed", ["item"] = mapped });
    }

    private void PlanUpdated(JsonElement @params, string turnId)
    {
        var items = new JsonArray();
        if (@params.TryGetProperty("todos", out var todos) && todos.ValueKind == JsonValueKind.Array)
        {
            foreach (var todo in todos.EnumerateArray())
            {
                items.Add(new JsonObject
                {
                    ["text"] = ReadString(todo, "content") ?? string.Empty,
                    ["completed"] = ReadString(todo, "status") == "completed"
                });
            }
        }

        var eventType = _todoListId is null ? "item.started" : "item.updated";
        _todoListId ??= $"todo_{turnId}";
        _todoItems = items;
        Emit(new JsonObject { ["type"] = eventType, ["item"] = TodoList() });
    }

    private void TurnEnded(JsonElement @params)
    {
        if (_todoListId is not null)
        {
            Emit(new JsonObject { ["type"] = "item.completed", ["item"] = TodoList() });
            _todoListId = null;
        }

        if (!@params.TryGetProperty("turn", out var turn)
            || !turn.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in items.EnumerateArray())
        {
            if (ReadString(item, "id") is { } id && _startedItemIds.Contains(id))
                CompleteItem(item);
        }
    }

    private JsonObject TodoList() => new()
    {
        ["id"] = _todoListId,
        ["type"] = "todo_list",
        ["items"] = _todoItems!.DeepClone()
    };

    private static JsonObject Usage(JsonElement @params)
    {
        var usage = @params.TryGetProperty("turn", out var turn)
                    && turn.TryGetProperty("tokenUsage", out var tokenUsage)
                    && tokenUsage.ValueKind == JsonValueKind.Object
            ? tokenUsage
            : default;
        return new JsonObject
        {
            ["input_tokens"] = ReadLong(usage, "inputTokens"),
            ["cached_input_tokens"] = ReadLong(usage, "cachedInputTokens"),
            ["cache_write_input_tokens"] = ReadLong(usage, "cacheWriteInputTokens"),
            ["output_tokens"] = ReadLong(usage, "outputTokens"),
            ["reasoning_output_tokens"] = ReadLong(usage, "reasoningOutputTokens")
        };
    }

    private JsonObject? MapItem(JsonElement item)
    {
        if (ReadString(item, "id") is not { Length: > 0 } id
            || !item.TryGetProperty("payload", out var payload)
            || payload.ValueKind != JsonValueKind.Object)
            return null;

        var mapped = ReadString(item, "type") switch
        {
            "agentMessage" => new JsonObject
            {
                ["type"] = "agent_message",
                ["text"] = ReadString(payload, "text") ?? string.Empty
            },
            "reasoningContent" when ReadString(payload, "text") is { } text && !string.IsNullOrWhiteSpace(text) =>
                new JsonObject { ["type"] = "reasoning", ["text"] = text },
            "commandExecution" => new JsonObject
            {
                ["type"] = "command_execution",
                ["command"] = ReadString(payload, "command") ?? string.Empty,
                ["aggregated_output"] = ReadString(payload, "aggregatedOutput") ?? string.Empty,
                ["exit_code"] = payload.TryGetProperty("exitCode", out var exitCode) && exitCode.ValueKind == JsonValueKind.Number
                    ? (JsonNode)exitCode.GetInt32()
                    : null,
                ["status"] = CommandStatus(payload)
            },
            "toolResult" => FileChange(payload),
            "mcpToolCall" => McpToolCall(payload),
            "webSearch" => new JsonObject
            {
                ["type"] = "web_search",
                ["query"] = ReadString(payload, "query") ?? string.Empty
            },
            "imageGeneration" => new JsonObject
            {
                ["type"] = "image_generation",
                ["status"] = LifecycleStatus(payload),
                ["saved_path"] = ReadString(payload, "savedPath"),
                ["revised_prompt"] = ReadString(payload, "revisedPrompt"),
                ["error"] = OneShotNotification.ImageGenerationFailure(payload)
            },
            _ => null
        };
        if (mapped is null)
            return null;

        mapped.Insert(0, "id", id);
        return mapped;
    }

    private static string CommandStatus(JsonElement payload) => ReadString(payload, "status") switch
    {
        "inProgress" => "in_progress",
        "completed" => "completed",
        _ => "failed"
    };

    private static JsonObject? FileChange(JsonElement payload)
    {
        if (!payload.TryGetProperty("structuredContent", out var content)
            || ReadString(content, "kind") != "fileChange")
            return null;

        var changes = new JsonArray();
        if (content.TryGetProperty("changes", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var change in list.EnumerateArray())
            {
                changes.Add(new JsonObject
                {
                    ["path"] = ReadString(change, "path") ?? string.Empty,
                    ["kind"] = ReadString(change, "kind") switch
                    {
                        "add" => "add",
                        "delete" => "delete",
                        _ => "update"
                    }
                });
            }
        }

        var success = payload.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
        return new JsonObject
        {
            ["type"] = "file_change",
            ["changes"] = changes,
            ["status"] = success ? "completed" : "failed"
        };
    }

    private static JsonObject McpToolCall(JsonElement payload)
    {
        var status = LifecycleStatus(payload);
        return new JsonObject
        {
            ["type"] = "mcp_tool_call",
            ["server"] = ReadString(payload, "server") ?? string.Empty,
            ["tool"] = ReadString(payload, "sourceToolId") ?? ReadString(payload, "toolName") ?? string.Empty,
            ["arguments"] = Clone(payload, "arguments") ?? new JsonObject(),
            ["result"] = status == "completed"
                ? new JsonObject
                {
                    ["content"] = Clone(payload, "content") ?? new JsonArray(),
                    ["structured_content"] = Clone(payload, "structuredContent")
                }
                : null,
            ["error"] = status == "failed"
                ? new JsonObject { ["message"] = ReadString(payload, "errorMessage") ?? "MCP tool call failed." }
                : null,
            ["status"] = status
        };
    }

    private static string LifecycleStatus(JsonElement payload) => ReadString(payload, "status") switch
    {
        "completed" => "completed",
        "failed" => "failed",
        _ => "in_progress"
    };

    private static JsonNode? Clone(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? JsonNode.Parse(value.GetRawText())
            : null;

    private static long ReadLong(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number)
            ? number
            : 0;

    private static string? ReadString(JsonElement element, string name) => OneShotNotification.ReadString(element, name);

    private void Emit(JsonObject line) => output.WriteLine(line.ToJsonString(LineOptions));
}
