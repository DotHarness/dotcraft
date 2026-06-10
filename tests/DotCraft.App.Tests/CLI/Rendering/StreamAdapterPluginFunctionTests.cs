using System.Text.Json;
using DotCraft.CLI.Rendering;

namespace DotCraft.Tests.CLI.Rendering;

public sealed class StreamAdapterPluginFunctionTests
{
    [Fact]
    public async Task AdaptWireNotificationsAsync_MapsPluginFunctionCallLifecycleToToolEvents()
    {
        var events = new List<RenderEvent>();
        await foreach (var renderEvent in StreamAdapter.AdaptWireNotificationsAsync(ReadNotifications()))
        {
            events.Add(renderEvent);
        }

        Assert.Collection(
            events,
            started =>
            {
                Assert.Equal(RenderEventType.ToolCallStarted, started.Type);
                Assert.Equal("NodeReplJs", started.Title);
                Assert.Equal("plugin-call-1", started.CallId);
                Assert.Contains("\"code\"", started.AdditionalInfo);
            },
            completed =>
            {
                Assert.Equal(RenderEventType.ToolCallCompleted, completed.Type);
                Assert.Equal("NodeReplJs", completed.Title);
                Assert.Equal("plugin-call-1", completed.CallId);
                Assert.Equal($"2{Environment.NewLine}[image: image/png]", completed.AdditionalInfo);
            });
    }

    [Fact]
    public async Task AdaptWireNotificationsAsync_UsesToolExecutionCompletionAndSkipsDuplicateToolResult()
    {
        var events = new List<RenderEvent>();
        await foreach (var renderEvent in StreamAdapter.AdaptWireNotificationsAsync(ReadToolExecutionNotifications()))
        {
            events.Add(renderEvent);
        }

        Assert.Collection(
            events,
            started =>
            {
                Assert.Equal(RenderEventType.ToolCallStarted, started.Type);
                Assert.Equal("WaitAgent", started.Title);
                Assert.Equal("call-1", started.CallId);
            },
            completed =>
            {
                Assert.Equal(RenderEventType.ToolCallCompleted, completed.Type);
                Assert.Equal("WaitAgent", completed.Title);
                Assert.Equal("call-1", completed.CallId);
                Assert.Equal("preview done", completed.AdditionalInfo);
            });
    }

    [Fact]
    public async Task AdaptWireNotificationsAsync_MapsDynamicToolCallLifecycleToToolEvents()
    {
        var events = new List<RenderEvent>();
        await foreach (var renderEvent in StreamAdapter.AdaptWireNotificationsAsync(ReadDynamicToolNotifications()))
        {
            events.Add(renderEvent);
        }

        Assert.Collection(
            events,
            started =>
            {
                Assert.Equal(RenderEventType.ToolCallStarted, started.Type);
                Assert.Equal("ListBoardItems", started.Title);
                Assert.Equal("dynamic-call-1", started.CallId);
                Assert.Contains("\"status\"", started.AdditionalInfo);
            },
            completed =>
            {
                Assert.Equal(RenderEventType.ToolCallCompleted, completed.Type);
                Assert.Equal("ListBoardItems", completed.Title);
                Assert.Equal("dynamic-call-1", completed.CallId);
                Assert.Equal($"2 board items{Environment.NewLine}[image: image/png]", completed.AdditionalInfo);
            });
    }

    // M-v fallback contract: the non-Desktop channel renders the model-visible result
    // (contentItems / structuredResult) and never the UI-only fields (_meta / widgetState / ui).
    [Fact]
    public async Task AdaptWireNotificationsAsync_DynamicToolFallback_RendersContentAndExcludesUiOnlyFields()
    {
        var events = new List<RenderEvent>();
        await foreach (var renderEvent in StreamAdapter.AdaptWireNotificationsAsync(ReadDynamicToolFallbackNotifications()))
        {
            events.Add(renderEvent);
        }

        var completed = events.Single(e => e.Type == RenderEventType.ToolCallCompleted);
        Assert.Contains("Board: 2 items", completed.AdditionalInfo);
        Assert.DoesNotContain("ui-only-meta", completed.AdditionalInfo);
        Assert.DoesNotContain("secret-widget-state", completed.AdditionalInfo);
        Assert.DoesNotContain("board.html", completed.AdditionalInfo);
    }

    private static async IAsyncEnumerable<JsonDocument> ReadDynamicToolFallbackNotifications()
    {
        yield return JsonDocument.Parse(
            """
            {
              "jsonrpc": "2.0",
              "method": "item/started",
              "params": {
                "item": {
                  "id": "dynamic-2",
                  "type": "dynamicToolCall",
                  "payload": { "namespace": "oratorio", "toolName": "ListBoardItems", "callId": "dynamic-call-2", "arguments": {} }
                }
              }
            }
            """);

        await Task.Yield();

        yield return JsonDocument.Parse(
            """
            {
              "jsonrpc": "2.0",
              "method": "item/completed",
              "params": {
                "item": {
                  "id": "dynamic-2",
                  "type": "dynamicToolCall",
                  "payload": {
                    "namespace": "oratorio",
                    "toolName": "ListBoardItems",
                    "callId": "dynamic-call-2",
                    "contentItems": [ { "type": "text", "text": "Board: 2 items" } ],
                    "structuredResult": { "count": 2 },
                    "_meta": { "secret": "ui-only-meta" },
                    "widgetState": { "selectedTab": "secret-widget-state" },
                    "ui": { "resourceUri": "ui://oratorio/board.html" },
                    "success": true
                  }
                }
              }
            }
            """);
    }

    private static async IAsyncEnumerable<JsonDocument> ReadNotifications()
    {
        yield return JsonDocument.Parse(
            """
            {
              "jsonrpc": "2.0",
              "method": "item/started",
              "params": {
                "item": {
                  "id": "plugin-1",
                  "type": "pluginFunctionCall",
                  "payload": {
                    "pluginId": "browser",
                    "namespace": "node_repl",
                    "functionName": "NodeReplJs",
                    "callId": "plugin-call-1",
                    "arguments": { "code": "1 + 1" }
                  }
                }
              }
            }
            """);

        await Task.Yield();

        yield return JsonDocument.Parse(
            """
            {
              "jsonrpc": "2.0",
              "method": "item/completed",
              "params": {
                "item": {
                  "id": "plugin-1",
                  "type": "pluginFunctionCall",
                  "payload": {
                    "pluginId": "browser",
                    "namespace": "node_repl",
                    "functionName": "NodeReplJs",
                    "callId": "plugin-call-1",
                    "contentItems": [
                      { "type": "text", "text": "2" },
                      { "type": "image", "mediaType": "image/png", "dataBase64": "abc123" }
                    ],
                    "success": true
                  }
                }
              }
            }
            """);
    }

    private static async IAsyncEnumerable<JsonDocument> ReadToolExecutionNotifications()
    {
        yield return JsonDocument.Parse(
            """
            {
              "jsonrpc": "2.0",
              "method": "item/started",
              "params": {
                "item": {
                  "id": "tool-1",
                  "type": "toolCall",
                  "payload": {
                    "toolName": "WaitAgent",
                    "callId": "call-1",
                    "arguments": { "childThreadId": "thread_child" }
                  }
                }
              }
            }
            """);

        await Task.Yield();

        yield return JsonDocument.Parse(
            """
            {
              "jsonrpc": "2.0",
              "method": "item/completed",
              "params": {
                "item": {
                  "id": "exec-1",
                  "type": "toolExecution",
                  "payload": {
                    "toolName": "WaitAgent",
                    "callId": "call-1",
                    "status": "completed",
                    "success": true,
                    "resultPreview": "preview done"
                  }
                }
              }
            }
            """);

        yield return JsonDocument.Parse(
            """
            {
              "jsonrpc": "2.0",
              "method": "item/completed",
              "params": {
                "item": {
                  "id": "result-1",
                  "type": "toolResult",
                  "payload": {
                    "callId": "call-1",
                    "success": true,
                    "result": "full result"
                  }
                }
              }
            }
            """);
    }

    private static async IAsyncEnumerable<JsonDocument> ReadDynamicToolNotifications()
    {
        yield return JsonDocument.Parse(
            """
            {
              "jsonrpc": "2.0",
              "method": "item/started",
              "params": {
                "item": {
                  "id": "dynamic-1",
                  "type": "dynamicToolCall",
                  "payload": {
                    "namespace": "oratorio",
                    "toolName": "ListBoardItems",
                    "callId": "dynamic-call-1",
                    "arguments": { "status": "todo" }
                  }
                }
              }
            }
            """);

        await Task.Yield();

        yield return JsonDocument.Parse(
            """
            {
              "jsonrpc": "2.0",
              "method": "item/completed",
              "params": {
                "item": {
                  "id": "dynamic-1",
                  "type": "dynamicToolCall",
                  "payload": {
                    "namespace": "oratorio",
                    "toolName": "ListBoardItems",
                    "callId": "dynamic-call-1",
                    "contentItems": [
                      { "type": "text", "text": "2 board items" },
                      { "type": "image", "mediaType": "image/png", "dataBase64": "abc123" }
                    ],
                    "success": true
                  }
                }
              }
            }
            """);
    }
}
