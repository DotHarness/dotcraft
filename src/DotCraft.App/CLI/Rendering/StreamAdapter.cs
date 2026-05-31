using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Text;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Tools;

namespace DotCraft.CLI.Rendering;

/// <summary>
/// Adapter to convert JSON-RPC streaming output into RenderEvent stream
/// </summary>
public static class StreamAdapter
{
    /// <summary>
    /// Adapts an <see cref="IAsyncEnumerable{JsonDocument}"/> of JSON-RPC notifications
    /// produced by <see cref="DotCraft.Protocol.AppServer.AppServerWireClient.ReadTurnNotificationsAsync"/>
    /// into the same <see cref="RenderEvent"/> stream consumed by terminal clients.
    /// 
    /// Approval flow: <c>item/approval/request</c> server requests are handled out-of-band by
    /// <c>AppServerWireClient.ServerRequestHandler</c> before reaching this adapter. The adapter
    /// observes only the resulting <c>item/started (approvalRequest)</c> and <c>item/approval/resolved</c>
    /// notifications for spinner control.
    /// </summary>
    public static async IAsyncEnumerable<RenderEvent> AdaptWireNotificationsAsync(
        IAsyncEnumerable<JsonDocument> notifications,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var callIdMap = new Dictionary<string, (string? Icon, string? Name, string? ArgsJson, string? FormattedDisplay)>();
        var completedFromToolExecution = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var doc in notifications.WithCancellation(ct))
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("method", out var methodEl)) continue;
            var method = methodEl.GetString() ?? string.Empty;

            var hasParams = root.TryGetProperty("params", out var @params);

            switch (method)
            {
                // ---------------------------------------------------------
                // Agent text and reasoning streaming (spec Section 6.3)
                // ---------------------------------------------------------
                case AppServerMethods.ItemAgentMessageDelta:
                {
                    if (!hasParams) break;
                    var delta = @params.TryGetProperty("delta", out var d) ? d.GetString() : null;
                    if (!string.IsNullOrEmpty(delta))
                        yield return RenderEvent.Response(delta);
                    break;
                }

                case AppServerMethods.ItemReasoningDelta:
                {
                    if (!hasParams) break;
                    var delta = @params.TryGetProperty("delta", out var d) ? d.GetString() : null;
                    if (!string.IsNullOrEmpty(delta))
                        yield return RenderEvent.Thinking("💭", "Thinking", delta);
                    break;
                }

                // ---------------------------------------------------------
                // Item lifecycle (spec Section 6.3)
                // ---------------------------------------------------------
                case AppServerMethods.ItemStarted:
                {
                    if (!hasParams || !@params.TryGetProperty("item", out var item)) break;
                    var type = item.TryGetProperty("type", out var t) ? t.GetString() : null;

                    if (type is "toolCall" or "pluginFunctionCall" or "dynamicToolCall")
                    {
                        var payload = item.TryGetProperty("payload", out var p) ? p : default;
                        var toolName = GetInvocationName(type, payload);
                        var callId = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("callId", out var ci)
                            ? ci.GetString() : null;
                        var icon = ToolRegistry.GetToolIcon(toolName ?? string.Empty);
                        string? argsJson = null, formattedDisplay = null;
                        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("arguments", out var args)
                            && args.ValueKind == JsonValueKind.Object)
                        {
                            argsJson = args.GetRawText();
                            var argsNode = JsonNode.Parse(argsJson) as JsonObject;
                            formattedDisplay = ToolRegistry.FormatToolCall(toolName ?? string.Empty, argsNode);
                        }
                        if (!string.IsNullOrEmpty(callId))
                            callIdMap[callId] = (icon, toolName, argsJson, formattedDisplay);
                        yield return RenderEvent.ToolStarted(icon, toolName, string.Empty, argsJson, formattedDisplay, callId: callId);
                    }
                    else if (type == "approvalRequest")
                    {
                        // Signal the renderer to pause the spinner while approval is handled
                        // out-of-band by AppServerWireClient.ServerRequestHandler.
                        yield return RenderEvent.ApprovalRequest();
                    }
                    break;
                }

                case AppServerMethods.ItemCompleted:
                {
                    if (!hasParams || !@params.TryGetProperty("item", out var item)) break;
                    var type = item.TryGetProperty("type", out var t) ? t.GetString() : null;

                    if (type == "toolExecution")
                    {
                        var payload = item.TryGetProperty("payload", out var p) ? p : default;
                        var callId = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("callId", out var ci)
                            ? ci.GetString() : null;
                        var result = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("resultPreview", out var r)
                            ? r.GetString() : null;
                        string? icon = null, name = null, argsJson = null, formattedDisplay = null;
                        if (!string.IsNullOrEmpty(callId) && callIdMap.TryGetValue(callId!, out var info))
                        {
                            (icon, name, argsJson, formattedDisplay) = info;
                            completedFromToolExecution.Add(callId!);
                        }
                        yield return RenderEvent.ToolCompleted(icon, name, argsJson ?? string.Empty, result, formattedDisplay, callId: callId);
                    }
                    else if (type == "toolResult")
                    {
                        var payload = item.TryGetProperty("payload", out var p) ? p : default;
                        var callId = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("callId", out var ci)
                            ? ci.GetString() : null;
                        if (!string.IsNullOrEmpty(callId) && completedFromToolExecution.Remove(callId!))
                            break;
                        var result = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("result", out var r)
                            ? r.GetString() : null;
                        string? icon = null, name = null, argsJson = null, formattedDisplay = null;
                        if (!string.IsNullOrEmpty(callId) && callIdMap.TryGetValue(callId!, out var info))
                        {
                            (icon, name, argsJson, formattedDisplay) = info;
                            callIdMap.Remove(callId!);
                        }
                        yield return RenderEvent.ToolCompleted(icon, name, argsJson ?? string.Empty, result, formattedDisplay, callId: callId);
                    }
                    else if (type is "pluginFunctionCall" or "dynamicToolCall")
                    {
                        var payload = item.TryGetProperty("payload", out var p) ? p : default;
                        var callId = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("callId", out var ci)
                            ? ci.GetString() : null;
                        var result = FormatStructuredInvocationResult(payload);
                        string? icon = null, name = null, argsJson = null, formattedDisplay = null;
                        if (!string.IsNullOrEmpty(callId) && callIdMap.TryGetValue(callId!, out var info))
                        {
                            (icon, name, argsJson, formattedDisplay) = info;
                            callIdMap.Remove(callId!);
                        }
                        else
                        {
                            name = GetInvocationName(type, payload);
                            icon = ToolRegistry.GetToolIcon(name ?? string.Empty);
                        }
                        yield return RenderEvent.ToolCompleted(icon, name, argsJson ?? string.Empty, result, formattedDisplay, callId: callId);
                    }
                    break;
                }

                // ---------------------------------------------------------
                // Approval flow (spec Section 7)
                // ---------------------------------------------------------
                case AppServerMethods.ItemApprovalResolved:
                {
                    yield return RenderEvent.ApprovalComplete();
                    break;
                }

                // ---------------------------------------------------------
                // Turn lifecycle (spec Section 6.2)
                // ---------------------------------------------------------
                case AppServerMethods.TurnCompleted:
                {
                    if (hasParams && @params.TryGetProperty("turn", out var turn)
                        && turn.TryGetProperty("tokenUsage", out var tu)
                        && tu.ValueKind == JsonValueKind.Object)
                    {
                        var inputTokens = tu.TryGetProperty("inputTokens", out var it) ? it.GetInt64() : 0L;
                        var outputTokens = tu.TryGetProperty("outputTokens", out var ot) ? ot.GetInt64() : 0L;
                        var totalTokens = tu.TryGetProperty("totalTokens", out var tt) ? tt.GetInt64() : inputTokens + outputTokens;
                        if (inputTokens > 0 || outputTokens > 0)
                            yield return RenderEvent.TokenUsage(inputTokens, outputTokens, totalTokens);
                    }
                    yield return RenderEvent.Completed(string.Empty);
                    break;
                }

                case AppServerMethods.TurnFailed:
                {
                    var errMsg = hasParams && @params.TryGetProperty("error", out var e)
                        ? e.GetString() : null;
                    yield return RenderEvent.ErrorEvent(errMsg ?? "Turn failed");
                    yield return RenderEvent.Completed(string.Empty);
                    break;
                }

                case AppServerMethods.TurnCancelled:
                {
                    yield return RenderEvent.Completed("cancelled");
                    break;
                }

                // ---------------------------------------------------------
                // Usage delta (spec Section 6.6)
                // ---------------------------------------------------------
                case AppServerMethods.ItemUsageDelta:
                {
                    if (!hasParams) break;
                    var inputTokens = @params.TryGetProperty("inputTokens", out var it) ? it.GetInt64() : 0L;
                    var outputTokens = @params.TryGetProperty("outputTokens", out var ot) ? ot.GetInt64() : 0L;
                    if (inputTokens > 0 || outputTokens > 0)
                        yield return RenderEvent.UsageDeltaEvent(inputTokens, outputTokens);
                    break;
                }

                // ---------------------------------------------------------
                // SubAgent progress (spec Section 6.5)
                // ---------------------------------------------------------
                case AppServerMethods.SubAgentProgress:
                {
                    if (!hasParams || !@params.TryGetProperty("entries", out var entriesEl)
                        || entriesEl.ValueKind != JsonValueKind.Array)
                        break;

                    var entries = new List<SubAgentProgressEntry>();
                    foreach (var item in entriesEl.EnumerateArray())
                    {
                        var label = item.TryGetProperty("label", out var l) ? l.GetString() : null;
                        if (string.IsNullOrEmpty(label)) continue;

                        entries.Add(new SubAgentProgressEntry
                        {
                            Label = label!,
                            CurrentTool = item.TryGetProperty("currentTool", out var ct2) && ct2.ValueKind == JsonValueKind.String
                                ? ct2.GetString() : null,
                            CurrentToolDisplay = item.TryGetProperty("currentToolDisplay", out var ctd) && ctd.ValueKind == JsonValueKind.String
                                ? ctd.GetString() : null,
                            InputTokens = item.TryGetProperty("inputTokens", out var it) ? it.GetInt64() : 0L,
                            OutputTokens = item.TryGetProperty("outputTokens", out var ot) ? ot.GetInt64() : 0L,
                            IsCompleted = item.TryGetProperty("isCompleted", out var ic) && ic.GetBoolean()
                        });
                    }

                    if (entries.Count > 0)
                        yield return RenderEvent.SubAgentProgressUpdate(entries);
                    break;
                }

                // ---------------------------------------------------------
                // System events (spec Section 6.7)
                // ---------------------------------------------------------
                case AppServerMethods.SystemEvent:
                {
                    if (!hasParams) break;
                    var kind = @params.TryGetProperty("kind", out var k) ? k.GetString() : null;
                    if (string.IsNullOrEmpty(kind)) break;
                    var message = @params.TryGetProperty("fallbackText", out var ft) && ft.ValueKind == JsonValueKind.String
                        ? ft.GetString()
                        : @params.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                        ? m.GetString() : null;
                    var payload = new SystemEventPayload { Kind = kind!, Message = message };
                    foreach (var re in MapSystemEvent(payload))
                        yield return re;
                    break;
                }

                // ---------------------------------------------------------
                // Plan/todo progress (spec Section 6.8)
                // ---------------------------------------------------------
                case AppServerMethods.PlanUpdated:
                {
                    if (!hasParams) break;
                    var title = @params.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var overview = @params.TryGetProperty("overview", out var o) ? o.GetString() ?? "" : "";
                    var todos = new List<PlanTodoData>();
                    if (@params.TryGetProperty("todos", out var todosEl) && todosEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in todosEl.EnumerateArray())
                        {
                            todos.Add(new PlanTodoData
                            {
                                Id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "",
                                Content = item.TryGetProperty("content", out var cEl) ? cEl.GetString() ?? "" : "",
                                Priority = item.TryGetProperty("priority", out var pEl) ? pEl.GetString() ?? "medium" : "medium",
                                Status = item.TryGetProperty("status", out var sEl) ? sEl.GetString() ?? "pending" : "pending"
                            });
                        }
                    }
                    yield return RenderEvent.PlanUpdateEvent(new PlanUpdateData
                    {
                        Title = title,
                        Overview = overview,
                        Todos = todos
                    });
                    break;
                }

                // All other notifications (thread/*, turn/started, item/started for non-tool, etc.) are ignored.
            }
        }
    }

    /// <summary>
    /// Maps a <see cref="SystemEventPayload"/> to zero or more <see cref="RenderEvent"/>s.
    /// </summary>
    private static IEnumerable<RenderEvent> MapSystemEvent(SystemEventPayload sysEvt)
    {
        switch (sysEvt.Kind)
        {
            case "compacting":
                yield return RenderEvent.SystemInfoEvent(sysEvt.Message ?? FallbackText.ContextLimitReached);
                break;
            case "compacted":
                yield return RenderEvent.SystemInfoEvent(sysEvt.Message ?? FallbackText.ContextCompacted);
                break;
            case "compactSkipped":
                yield return RenderEvent.SystemInfoEvent(sysEvt.Message ?? FallbackText.ContextCompactSkipped);
                break;
            case "consolidating":
                yield return RenderEvent.SystemStatusEvent(
                    sysEvt.Message ?? FallbackText.MemoryConsolidating,
                    FallbackText.MemoryConsolidated);
                break;
            case "consolidated":
                // The completion event dismisses the SystemStatus spinner in the renderer.
                yield return RenderEvent.SystemInfoEvent(sysEvt.Message ?? FallbackText.MemoryConsolidated);
                break;
            case "consolidationSkipped":
                yield return RenderEvent.SystemInfoEvent(string.Empty);
                break;
            case "consolidationFailed":
                yield return RenderEvent.SystemInfoEvent(sysEvt.Message ?? FallbackText.MemoryConsolidationFailed);
                break;
        }
    }

    private static string? GetInvocationName(string? type, JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return null;
        var propertyName = type == "pluginFunctionCall" ? "functionName" : "toolName";
        if (payload.TryGetProperty(propertyName, out var name) && name.ValueKind == JsonValueKind.String)
            return name.GetString();
        if (payload.TryGetProperty("toolName", out var toolName) && toolName.ValueKind == JsonValueKind.String)
            return toolName.GetString();
        if (payload.TryGetProperty("functionName", out var functionName) && functionName.ValueKind == JsonValueKind.String)
            return functionName.GetString();
        return null;
    }

    private static string? FormatStructuredInvocationResult(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return null;

        if (payload.TryGetProperty("contentItems", out var contentItems)
            && contentItems.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var item in contentItems.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var type = item.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
                    ? typeEl.GetString()
                    : "text";
                if (type == "text")
                {
                    if (item.TryGetProperty("text", out var textEl)
                        && textEl.ValueKind == JsonValueKind.String
                        && !string.IsNullOrEmpty(textEl.GetString()))
                    {
                        parts.Add(textEl.GetString()!);
                    }
                }
                else if (type == "image")
                {
                    var mediaType = item.TryGetProperty("mediaType", out var mediaTypeEl)
                        && mediaTypeEl.ValueKind == JsonValueKind.String
                            ? mediaTypeEl.GetString()
                            : "image";
                    parts.Add($"[image: {mediaType}]");
                }
            }
            if (parts.Count > 0)
                return string.Join(Environment.NewLine, parts);
        }

        if (payload.TryGetProperty("structuredResult", out var structured)
            && structured.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            return JsonSerializer.Serialize(structured, new JsonSerializerOptions { WriteIndented = true });
        }

        return payload.TryGetProperty("errorMessage", out var error)
            && error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
    }
}
