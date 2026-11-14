using System.Collections.Concurrent;
using System.Text.Json;

namespace DotCraft.CodexGateway.Workspace;

public sealed partial class CodexWorkspaceSession
{
    private readonly ConcurrentDictionary<string, byte> _visibleItemIds = new();

    private void RecordItemStart(string id) => _visibleItemIds.TryAdd(id, 0);

    private async Task ForwardNotificationsAsync(CancellationToken ct)
    {
        await foreach (var notification in _client.ReadNotificationsAsync(ct))
        {
            var p = notification.Params;
            if (_interactive is not null) await _interactive.ObserveAsync(notification.Method, p, ct);
            var thread = p.Text("threadId") ?? p.Get("turn").Text("threadId") ?? p.Get("thread").Text("id");
            if (thread is null) continue;
            var method = notification.Method;
            if (method == "thread/started") _attached.TryAdd(thread, 0);
            if (_attachmentReady.TryGetValue(thread, out var ready)) await ready.Task.WaitAsync(ct);
            if (_client.Wire.State != DotCraft.Sdk.Wire.WireConnectionState.Ready) continue;
            object? projected = null;
            var threadId = _routes.External("thread", thread);
            switch (method)
            {
                case "thread/started":
                    projected = new { thread = _projector.Thread(p.Get("thread")) };
                    break;
                case "thread/renamed":
                    method = "thread/name/updated";
                    projected = new { threadId, threadName = p.Text("displayName") };
                    break;
                case "thread/archived":
                case "thread/unarchived":
                    projected = new { threadId };
                    break;
                case "thread/status/changed":
                    projected = new { threadId, status = WorkspaceProjector.RuntimeStatus(p.Get("runtime")) };
                    break;
                case "turn/started":
                case "turn/completed":
                case "turn/cancelled":
                case "turn/failed":
                    if (!_attached.ContainsKey(thread)) continue;
                    method = method == "turn/started" ? method : "turn/completed";
                    var sourceTurn = p.Get("turn");
                    projected = new { threadId, turn = _projector.Turn(thread, sourceTurn) };
                    break;
                case "item/started":
                case "item/completed":
                {
                    if (!_attached.ContainsKey(thread)) continue;
                    var turn = p.Required("turnId");
                    var source = p.Get("item");
                    if (source.Text("type") == "error")
                    {
                        if (method == "item/completed")
                        {
                            method = "error";
                            projected = new
                            {
                                threadId, turnId = _routes.External("turn", thread, turn), willRetry = false,
                                error = new
                                {
                                    message = source.Get("payload").Required("message"),
                                    codexErrorInfo = source.Get("payload").Text("code") ?? "other",
                                    additionalDetails = (string?)null
                                }
                            };
                        }
                        break;
                    }
                    if (method == "item/started" && source.Text("type") == "toolResult") continue;
                    var item = _projector.Item(thread, turn, source);
                    if (item is not null && method == "item/completed" && source.Text("type") == "toolCall")
                    {
                        if (!_visibleItemIds.TryAdd((string)item["id"]!, 0)) continue;
                        method = "item/started";
                    }
                    if (item is not null)
                    {
                        var itemId = (string)item["id"]!;
                        var externalTurn = _routes.External("turn", thread, turn);
                        if (method == "item/started") RecordItemStart(itemId);
                        else if (_visibleItemIds.TryAdd(itemId, 0))
                            await _send(WorkspaceJson.Element(new
                            {
                                method = "item/started", @params = new { threadId, turnId = externalTurn, item }
                            }), ct);
                        projected = new { threadId, turnId = externalTurn, item };
                    }
                    break;
                }
                case "item/agentMessage/delta":
                case "item/reasoning/delta":
                case "item/commandExecution/outputDelta":
                {
                    if (!_attached.ContainsKey(thread)) continue;
                    var turn = p.Required("turnId");
                    var values = new Dictionary<string, object?>
                    {
                        ["threadId"] = threadId, ["turnId"] = _routes.External("turn", thread, turn),
                        ["itemId"] = _projector.DeltaItemId(thread, turn, p.Required("itemId")),
                        ["delta"] = p.Required("delta")
                    };
                    if (method == "item/reasoning/delta")
                    {
                        method = "item/reasoning/textDelta";
                        values["contentIndex"] = 0;
                    }
                    projected = values;
                    break;
                }
                case "turn/diff/updated":
                    if (_attached.ContainsKey(thread) && p.Text("turnId") is { } diffTurn)
                        projected = new { threadId, turnId = _routes.External("turn", thread, diffTurn), diff = p.Text("diff") ?? "" };
                    break;
            }
            if (projected is not null)
                await _send(WorkspaceJson.Element(new { method, @params = projected }), ct);
        }
    }
}
