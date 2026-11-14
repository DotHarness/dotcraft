using System.Text.Json;

namespace DotCraft.CodexGateway.Workspace;

internal sealed partial class WorkspaceProjector(WorkspaceRoutes routes, string workspace)
{
    public static string Binding(string provider, string model) =>
        "dotcraft/" + Uri.EscapeDataString(provider) + "/" + Uri.EscapeDataString(model);

    public Dictionary<string, object?> Thread(JsonElement source, object[]? turns = null)
    {
        var id = source.Required("id");
        var configuration = source.Get("configuration");
        var runtime = source.Get("runtime");
        var model = configuration.Text("model");
        var provider = configuration.Text("providerId") ?? "";
        return new()
        {
            ["id"] = routes.External("thread", id),
            ["sessionId"] = routes.External("thread", source.Text("sessionId") ?? id),
            ["forkedFromId"] = source.Text("forkedFromId") is { } fork ? routes.External("thread", fork) : null,
            ["parentThreadId"] = source.Text("parentThreadId") is { } parent ? routes.External("thread", parent) : null,
            ["attachedToThreadId"] = null, ["environments"] = null, ["extra"] = null,
            ["preview"] = source.Text("displayName") ?? "", ["ephemeral"] = source.Flag("ephemeral"),
            ["section"] = null, ["sectionEnteredAt"] = null, ["projectId"] = null,
            ["historyMode"] = "paginated", ["modelProvider"] = provider,
            ["model"] = model is null ? null : Binding(provider, model),
            ["reasoningEffort"] = configuration.Get("reasoning").Get("enabled").ValueKind == JsonValueKind.False ? "none"
                : configuration.Get("reasoning").Text("effort") is { } effort ? CodexWorkspaceSession.CodexEffort(effort) : null,
            ["createdAt"] = source.Seconds("createdAt") ?? 0,
            ["updatedAt"] = source.Seconds("lastActiveAt") ?? 0,
            ["recencyAt"] = source.Seconds("lastActiveAt"),
            ["status"] = RuntimeStatus(runtime),
            ["path"] = null, ["cwd"] = source.Text("cwd") ?? workspace,
            ["cliVersion"] = "0.166.0", ["originator"] = source.Text("originChannel"),
            ["source"] = "appServer", ["canAcceptDirectInput"] = null,
            ["threadSource"] = null, ["agentNickname"] = null, ["agentRole"] = null,
            ["gitInfo"] = source.Get("gitInfo").Value(), ["name"] = source.Text("displayName"),
            ["turns"] = turns ?? []
        };
    }

    public static object RuntimeStatus(JsonElement runtime)
    {
        if (runtime.ValueKind != JsonValueKind.Object) return Status(null);
        if (runtime.Flag("waitingOnApproval")) return Status("waitingApproval");
        if (runtime.Flag("waitingOnInput")) return Status("waitingInput");
        return Status(runtime.Flag("running") || runtime.Flag("busy") ? "running" : "idle");
    }

    public static object Status(string? state) => state switch
    {
        "running" => new { type = "active", activeFlags = Array.Empty<string>() },
        "waitingApproval" => new { type = "active", activeFlags = new[] { "waitingOnApproval" } },
        "waitingInput" => new { type = "active", activeFlags = new[] { "waitingOnUserInput" } },
        "idle" => new { type = "idle" },
        _ => new { type = "notLoaded" }
    };

    public object Turn(string thread, JsonElement source, object[]? items = null, string? view = null)
    {
        var id = source.Required("id");
        return new
        {
            id = routes.External("turn", thread, id),
            rootTurnId = routes.External("turn", thread, id),
            items = items ?? ProjectItems(thread, source, view ?? source.Text("itemsView")),
            itemsView = view ?? source.Text("itemsView") ?? (source.Get("items").ValueKind == JsonValueKind.Array ? "full" : "notLoaded"),
            status = TurnStatus(source.Text("status")),
            error = TurnError(source),
            startedAt = source.Seconds("startedAt"), completedAt = source.Seconds("completedAt"),
            durationMs = source.Milliseconds("completedAt") - source.Milliseconds("startedAt")
        };
    }

    internal static string TurnStatus(string? status) => status switch
    {
        "completed" => "completed", "cancelled" => "interrupted", "failed" => "failed", _ => "inProgress"
    };

    internal static object? TurnError(JsonElement source) => source.Text("error") is { } error
        ? new { message = error, codexErrorInfo = (object?)null, additionalDetails = source.Text("providerError") } : null;

    public Dictionary<string, object?>? Item(string thread, string turn, JsonElement source)
    {
        var p = source.Get("payload");
        var type = source.Required("type");
        if (type == "toolCall" && string.IsNullOrEmpty(p.Text("callId"))) return null;
        var itemId = ItemId(thread, turn, source);
        if (OmitInternalSuccess(source, itemId)) return null;
        // Retained Item fields must not keep the entire source history-page document alive.
        source = source.Clone();
        p = source.Get("payload");
        var result = new Dictionary<string, object?>
        {
            ["id"] = itemId,
            ["startedAtMs"] = source.Milliseconds("createdAt"),
            ["completedAtMs"] = source.Milliseconds("completedAt")
        };
        switch (type)
        {
            case "userMessage":
                if (p.Text("deliveryMode") == "subagentMailbox" ||
                    p.Text("triggerKind") is "subagentMailbox" or "terminal" or "subagent" or "workflow")
                    return null;
                result["type"] = "userMessage";
                result["clientId"] = p.Text("clientUserMessageId");
                result["content"] = p.Get("nativeInputParts").ValueKind == JsonValueKind.Array
                    ? p.Get("nativeInputParts").Rows().Select(InputPart).ToArray()
                    : new[] { TextPart(p.Text("text") ?? "") };
                break;
            case "agentMessage":
                result["type"] = "agentMessage"; result["text"] = p.Text("text") ?? "";
                result["phase"] = p.Text("phase");
                result["memoryCitation"] = null;
                result["delivery"] = p.Text("deliveryMode") == "async" ? "async" : null;
                break;
            case "reasoningContent":
                result["type"] = "reasoning"; result["summary"] = Array.Empty<string>();
                result["content"] = new[] { p.Text("text") ?? "" };
                break;
            case "commandExecution":
                result["type"] = type; result["command"] = p.Text("command") ?? "";
                result["cwd"] = p.Text("workingDirectory") ?? workspace;
                result["processId"] = p.Text("sessionId");
                result["source"] = "agent"; result["commandActions"] = CommandActions(p.Text("command") ?? "");
                result["status"] = p.Text("status") switch
                {
                    "completed" => "completed", "declined" => "declined",
                    "failed" or "cancelled" or "killed" or "lost" => "failed", _ => "inProgress"
                };
                result["aggregatedOutput"] = p.Text("aggregatedOutput");
                result["exitCode"] = p.Get("exitCode").Value(); result["durationMs"] = p.Get("durationMs").Value();
                break;
            case "toolCall":
                ToolInvocation(result, p);
                break;
            case "toolResult":
                ToolOutput(result, p);
                break;
            case "mcpToolCall":
                McpCall(result, p);
                break;
            case "dynamicToolCall":
                DynamicCall(result, p);
                break;
            case "approvalRequest":
                return null;
            case "webSearch":
                WebSearch(result, p);
                break;
            case "imageGeneration":
                ImageGeneration(result, p);
                break;
            case "error":
                return null;
            default:
                return null;
        }
        var merged = MergeOperation(thread, turn, source, result);
        if (!IsVisible(source, merged)) return null;
        RememberVisibleSource(source, merged);
        return merged;
    }

    private static object TextPart(string text) => new { type = "text", text, text_elements = Array.Empty<object>() };

    private static object InputPart(JsonElement part) => part.Text("type") switch
    {
        "text" => TextPart(part.Text("text") ?? ""),
        "image" when part.Text("url") is { } url => new { type = "image", url },
        "localImage" when part.Text("path") is { } path => new { type = "localImage", path },
        "skill" when part.Text("path") is { } path => new { type = "skill", name = part.Text("name") ?? Path.GetFileName(path), path },
        "file" when part.Text("path") is { } path => new { type = "mention", name = part.Text("name") ?? Path.GetFileName(path), path },
        _ => TextPart("[DotCraft " + part.Required("type") + " input] " +
            (part.Text("text") ?? part.Text("displayPath") ?? part.Text("path") ?? part.Text("name") ?? ""))
    };

}
