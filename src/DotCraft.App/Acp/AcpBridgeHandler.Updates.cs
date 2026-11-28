using System.Text.Json;
using DotCraft.Agents;
using DotCraft.CLI;
using DotCraft.Memory;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using DotHarness.Acp.Protocol;
using DotHarness.Acp.Rpc;
using DotHarness.Acp.V1;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.Acp;

public sealed partial class AcpBridgeHandler
{
    private async Task ReplayItemAsAcpUpdateAsync(string sessionId, SessionWireItem item, CancellationToken ct)
    {
        var text = ExtractTextFromPayload(item.Payload);
        if (text == null)
            return;

        SessionUpdate? update = item.Type switch
        {
            ItemType.UserMessage => SessionUpdate.FromUserMessage(text),
            ItemType.AgentMessage => SessionUpdate.FromAgentMessage(text),
            ItemType.ReasoningContent => SessionUpdate.FromAgentThought(text),
            _ => null
        };

        if (update != null)
            await SendSessionUpdateAsync(sessionId, update, ct).ConfigureAwait(false);
    }

    internal static string? ExtractTextFromPayload(object? payload)
    {
        if (payload is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Object &&
                el.TryGetProperty("text", out var textEl) &&
                textEl.ValueKind == JsonValueKind.String)
                return textEl.GetString();
            return null;
        }

        if (payload is UserMessagePayload userMsg)
            return userMsg.Text;
        if (payload is AgentMessagePayload agentMsg)
            return agentMsg.Text;
        if (payload is ReasoningContentPayload reasoning)
            return reasoning.Text;
        return null;
    }

    internal static bool IsTodoProgressTool(string? toolName) =>
        string.Equals(toolName, "TodoWrite", StringComparison.OrdinalIgnoreCase);

    internal static ToolCallContent[]? BuildToolResultContent(
        string? toolName,
        object? resultObj,
        bool success)
    {
        if (success && IsTodoProgressTool(toolName))
            return null;

        var preview = ImageContentSanitizingChatClient.DescribeResult(resultObj);
        if (preview.Length > 500) preview = preview[..500] + "...";
        return [ToolCallContent.FromText(preview)];
    }

    private async Task MapWireNotificationToAcpAsync(
        string sessionId,
        string method,
        JsonElement @params,
        CancellationToken ct)
    {
        switch (method)
        {
            case Contract.AppServerMethodNames.AgentMessageDelta:
            {
                var delta = @params.TryGetProperty("delta", out var d) ? d.GetString() : null;
                if (!string.IsNullOrEmpty(delta))
                    await SendSessionUpdateAsync(sessionId, SessionUpdate.FromAgentMessage(delta), ct).ConfigureAwait(false);
                break;
            }
            case Contract.AppServerMethodNames.ReasoningDelta:
            {
                var delta = @params.TryGetProperty("delta", out var d) ? d.GetString() : null;
                if (!string.IsNullOrEmpty(delta))
                    await SendSessionUpdateAsync(sessionId, SessionUpdate.FromAgentThought(delta), ct).ConfigureAwait(false);
                break;
            }
            case Contract.AppServerMethodNames.ItemStarted:
            {
                if (!@params.TryGetProperty("item", out var item)) break;
                var type = item.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type is not ("toolCall" or "dynamicToolCall")) break;
                if (!item.TryGetProperty("payload", out var payload)) break;

                var toolName = payload.TryGetProperty("toolName", out var tn) ? tn.GetString() ?? "" : "";
                var callId = payload.TryGetProperty("callId", out var ci) ? ci.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(callId))
                    _activeToolNames[ToolCallKey(sessionId, callId)] = toolName;

                string? argsStr = null;
                JsonElement argsElement = default;
                if (payload.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object)
                {
                    argsElement = args;
                    argsStr = JsonSerializer.Serialize(args, JsonOptions);
                }

                var declaredKind = ResolveRuntimeToolKind(payload, toolName);
                var kind = AcpToolKindMapper.GetKind(toolName, declaredKind);
                var filePaths = AcpToolKindMapper.ExtractFilePaths(toolName, argsElement);
                var toolCall = new ToolCall
                {
                    ToolCallId = new(callId),
                    Title = toolName,
                    Kind = kind,
                    Status = ToolCallStatus.InProgress,
                    Content = Optional<ToolCallContent[]>.OmitIfNull(
                        string.IsNullOrEmpty(argsStr) ? null : new[] { ToolCallContent.FromText(argsStr) }),
                    Locations = Optional<ToolCallLocation[]>.OmitIfNull(
                        filePaths is { Count: > 0 } ? filePaths.Select(fp => new ToolCallLocation { Path = fp }).ToArray() : null)
                };

                await SendSessionUpdateAsync(sessionId, toolCall, ct).ConfigureAwait(false);
                break;
            }
            case Contract.AppServerMethodNames.ItemCompleted:
            {
                if (!@params.TryGetProperty("item", out var item)) break;
                var type = item.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type is not ("toolResult" or "dynamicToolCall")) break;
                if (!item.TryGetProperty("payload", out var payload)) break;

                var callId = payload.TryGetProperty("callId", out var ci) ? ci.GetString() ?? "" : "";
                object? resultObj = null;
                if (type == "dynamicToolCall")
                {
                    resultObj = ExtractDynamicToolResult(payload);
                }
                else if (payload.TryGetProperty("result", out var r))
                {
                    resultObj = r.ValueKind == JsonValueKind.String
                        ? r.GetString()
                        : JsonSerializer.Deserialize<object>(r.GetRawText(), JsonOptions);
                }

                var success = !payload.TryGetProperty("success", out var su) || su.GetBoolean();
                var toolName = payload.TryGetProperty("toolName", out var tn) ? tn.GetString() : null;
                if (!string.IsNullOrEmpty(callId)
                    && _activeToolNames.TryRemove(ToolCallKey(sessionId, callId), out var trackedToolName)
                    && string.IsNullOrEmpty(toolName))
                {
                    toolName = trackedToolName;
                }

                var content = BuildToolResultContent(toolName, resultObj, success);
                var toolCallUpdate = new ToolCallUpdate
                {
                    ToolCallId = new(callId),
                    Status = success ? ToolCallStatus.Completed : ToolCallStatus.Failed,
                    Content = Optional<ToolCallContent[]?>.OmitIfNull(content)
                };

                await SendSessionUpdateAsync(sessionId, toolCallUpdate, ct).ConfigureAwait(false);
                break;
            }
            case Contract.AppServerMethodNames.TurnCompleted:
                break;
            case Contract.AppServerMethodNames.PlanUpdated:
            {
                if (!@params.TryGetProperty("todos", out var todosEl) || todosEl.ValueKind != JsonValueKind.Array)
                    break;
                var entries = new List<PlanEntry>();
                foreach (var todo in todosEl.EnumerateArray())
                {
                    var content = todo.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                    var priority = todo.TryGetProperty("priority", out var pr) ? pr.GetString() : "medium";
                    var status = todo.TryGetProperty("status", out var st) ? st.GetString() : "pending";
                    entries.Add(new PlanEntry
                    {
                        Content = content,
                        Priority = priority switch
                        {
                            "high" => PlanEntryPriority.High,
                            "low" => PlanEntryPriority.Low,
                            _ => PlanEntryPriority.Medium
                        },
                        Status = status switch
                        {
                            "inProgress" => PlanEntryStatus.InProgress,
                            "completed" or "cancelled" => PlanEntryStatus.Completed,
                            _ => PlanEntryStatus.Pending
                        }
                    });
                }

                await SendSessionUpdateAsync(sessionId, SessionUpdate.FromPlan([.. entries]), ct).ConfigureAwait(false);
                break;
            }
        }
    }

    private string? ResolveRuntimeToolKind(JsonElement payload, string toolName)
    {
        var @namespace = TryGetStringProperty(payload, "namespace");
        return _runtimeToolDescriptors.TryGetValue(AcpRuntimeTools.RuntimeToolKey(@namespace, toolName), out var descriptor)
            ? descriptor.Kind
            : null;
    }

    private static object? ExtractDynamicToolResult(JsonElement payload)
    {
        if (payload.TryGetProperty("structuredContent", out var structuredContent)
            && structuredContent.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            return JsonSerializer.Deserialize<object>(structuredContent.GetRawText(), JsonOptions);
        }

        if (payload.TryGetProperty("contentItems", out var contentItems)
            && contentItems.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            return JsonSerializer.Deserialize<object>(contentItems.GetRawText(), JsonOptions);
        }

        if (payload.TryGetProperty("errorMessage", out var error)
            && error.ValueKind == JsonValueKind.String)
        {
            return error.GetString();
        }

        return null;
    }

    private async Task SendPlanUpdateAsync(string sessionId, StructuredPlan plan, CancellationToken ct)
    {
        var entries = plan.Todos.Select(t => new PlanEntry
        {
            Content = t.Content,
            Priority = t.Priority switch
            {
                PlanTodoPriority.High => PlanEntryPriority.High,
                PlanTodoPriority.Low => PlanEntryPriority.Low,
                _ => PlanEntryPriority.Medium
            },
            Status = t.Status switch
            {
                PlanTodoStatus.InProgress => PlanEntryStatus.InProgress,
                PlanTodoStatus.Completed or PlanTodoStatus.Cancelled => PlanEntryStatus.Completed,
                _ => PlanEntryStatus.Pending
            }
        }).ToArray();

        await SendSessionUpdateAsync(sessionId, SessionUpdate.FromPlan(entries), ct).ConfigureAwait(false);
    }

    private Task SendModeUpdateAsync(string sessionId, AgentMode mode, CancellationToken ct) =>
        SendSessionUpdateAsync(sessionId, SessionUpdate.FromCurrentMode(new(mode.ToString().ToLowerInvariant())), ct);

    private Task SendConfigOptionsUpdateAsync(
        string sessionId,
        SessionConfigOption[] configOptions,
        CancellationToken ct) =>
        SendSessionUpdateAsync(sessionId, SessionUpdate.FromConfigOptions(configOptions), ct);

    private void ScheduleSlashCommandsBroadcast(string sessionId)
    {
        var commands = commandRegistry?.ListCommands()
            .Where(static command => command.Category == "workflow")
            .Select(c => new AvailableCommand
            {
                Name = c.Name.TrimStart('/'),
                Description = c.Description ?? ""
            })
            .ToArray()
            ?? [];

        if (commands.Length == 0)
            return;

        AcpRequestContext.ScheduleAfterReply(ct =>
            SendSessionUpdateAsync(sessionId, SessionUpdate.FromAvailableCommands(commands), ct));
    }

    private Task SendSessionUpdateAsync(string sessionId, SessionUpdate update, CancellationToken ct) =>
        Client.SessionUpdateAsync(new SessionNotification
        {
            SessionId = new(sessionId),
            Update = update
        }, ct);
}
