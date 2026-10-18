using System.Text.Json.Nodes;
using Xunit;
using static DotCraft.Tests.CLI.ExecWire;

namespace DotCraft.Tests.CLI;

public sealed class ExecJsonProcessTests : ExecProcessTestBase
{
    private const string ImageBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public async Task Exec_Json_EmitsEventsAndItemsForTheTurn()
    {
        var savedPath = Path.Combine(Workspace, ".craft", "generated_images", "thread_1", "call_img.png");
        var lateCommand = Item("commandExecution", Command("sleep 1", "completed", "", 0), id: "item_late");
        var finalMessage = AgentMessage("final answer");
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions
        {
            TurnNotifications = threadId =>
            [
                Notification("turn/started", new JsonObject { ["turn"] = Turn(threadId, "running") }),
                ItemEvent("item/started", threadId, AgentMessage(string.Empty)),
                ItemEvent("item/completed", threadId, Item("reasoningContent", new JsonObject { ["text"] = "thinking" })),
                ItemEvent("item/started", threadId, Item("commandExecution", Command("ls", "inProgress", ""), id: "item_cmd")),
                ItemEvent("item/completed", threadId, Item("commandExecution", Command("ls", "completed", "a.txt\n", 0), id: "item_cmd")),
                Plan(threadId, ("step one", "in_progress")),
                ItemEvent("item/completed", threadId, Item("approvalRequest", new JsonObject
                {
                    ["approvalType"] = "shell",
                    ["operation"] = "rm -rf build",
                    ["target"] = Workspace,
                    ["requestId"] = "req_1"
                })),
                ItemEvent("item/completed", threadId, Item("approvalResponse", new JsonObject
                {
                    ["requestId"] = "req_1",
                    ["approved"] = false,
                    ["decision"] = "decline"
                })),
                ItemEvent("item/started", threadId, Item("commandExecution", Command("rm -rf build", "inProgress", ""), id: "item_rm")),
                ItemEvent("item/completed", threadId, Item("commandExecution", Command("rm -rf build", "failed", "Error: rejected"), id: "item_rm")),
                ItemEvent("item/completed", threadId, Item("toolResult", new JsonObject
                {
                    ["callId"] = "call_write",
                    ["toolName"] = "WriteFile",
                    ["success"] = true,
                    ["structuredContent"] = new JsonObject
                    {
                        ["kind"] = "fileChange",
                        ["writeState"] = "applied",
                        ["changes"] = new JsonArray(
                            new JsonObject { ["path"] = "a.txt", ["kind"] = "add", ["diff"] = "+a" },
                            new JsonObject { ["path"] = "b.txt", ["kind"] = "update", ["diff"] = "+b" })
                    }
                })),
                Plan(threadId, ("step one", "completed"), ("step two", "pending")),
                ItemEvent("item/started", threadId, Item("mcpToolCall", Mcp("inProgress"), id: "item_mcp")),
                ItemEvent("item/completed", threadId, Item("mcpToolCall", Mcp("completed"), id: "item_mcp")),
                ItemEvent("item/started", threadId, Item("webSearch", Search("inProgress", ""), id: "item_web")),
                ItemEvent("item/completed", threadId, Item("webSearch", Search("completed", "dotnet 10"), id: "item_web")),
                ItemEvent("item/started", threadId, Item("imageGeneration", Image("inProgress"), id: "item_img")),
                ItemEvent("item/completed", threadId, Item("imageGeneration", Image("completed", savedPath), id: "item_img")),
                ItemEvent("item/started", threadId, Item("commandExecution", Command("sleep 1", "inProgress", ""), id: "item_late")),
                ItemEvent("item/completed", threadId, finalMessage),
                Notification("turn/completed", new JsonObject
                {
                    ["turn"] = WithUsage(Turn(threadId, "completed", lateCommand, finalMessage))
                })
            ]
        });

        var result = await RunExecAsync(server, ["--json", "do the work"]);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(ImageBase64, result.StandardOutput, StringComparison.Ordinal);
        var events = ParseLines(result.StandardOutput);
        Assert.Equal(
            new[]
            {
                "thread.started", "turn.started",
                "item.completed:reasoning",
                "item.started:command_execution", "item.completed:command_execution",
                "item.started:todo_list",
                "item.started:command_execution", "item.completed:command_execution",
                "item.completed:file_change",
                "item.updated:todo_list",
                "item.started:mcp_tool_call", "item.completed:mcp_tool_call",
                "item.started:web_search", "item.completed:web_search",
                "item.started:image_generation", "item.completed:image_generation",
                "item.started:command_execution",
                "item.completed:agent_message",
                "item.completed:todo_list",
                "item.completed:command_execution",
                "turn.completed"
            },
            events.Select(Describe));

        Assert.Equal("thread_1", events[0]["thread_id"]!.GetValue<string>());
        AssertItem(events[2], new JsonObject { ["id"] = "item_reasoningContent", ["type"] = "reasoning", ["text"] = "thinking" });
        AssertItem(events[4], new JsonObject
        {
            ["id"] = "item_cmd",
            ["type"] = "command_execution",
            ["command"] = "ls",
            ["aggregated_output"] = "a.txt\n",
            ["exit_code"] = 0,
            ["status"] = "completed"
        });
        Assert.Equal("in_progress", events[3]["item"]!["status"]!.GetValue<string>());
        Assert.Equal("failed", events[7]["item"]!["status"]!.GetValue<string>());
        AssertItem(events[8], new JsonObject
        {
            ["id"] = "item_toolResult",
            ["type"] = "file_change",
            ["changes"] = new JsonArray(
                new JsonObject { ["path"] = "a.txt", ["kind"] = "add" },
                new JsonObject { ["path"] = "b.txt", ["kind"] = "update" }),
            ["status"] = "completed"
        });

        var todoId = events[5]["item"]!["id"]!.GetValue<string>();
        Assert.True(JsonNode.DeepEquals(Todos(("step one", false)), events[5]["item"]!["items"]));
        Assert.True(JsonNode.DeepEquals(Todos(("step one", true), ("step two", false)), events[9]["item"]!["items"]));
        Assert.True(JsonNode.DeepEquals(Todos(("step one", true), ("step two", false)), events[18]["item"]!["items"]));
        Assert.Equal(todoId, events[9]["item"]!["id"]!.GetValue<string>());
        Assert.Equal(todoId, events[18]["item"]!["id"]!.GetValue<string>());

        var mcp = events[11]["item"]!;
        Assert.Equal("docs", mcp["server"]!.GetValue<string>());
        Assert.Equal("search", mcp["tool"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(new JsonObject { ["q"] = "x" }, mcp["arguments"]));
        Assert.Equal("completed", mcp["status"]!.GetValue<string>());
        Assert.Null(mcp["error"]);
        Assert.Equal("dotnet 10", events[13]["item"]!["query"]!.GetValue<string>());
        AssertItem(events[15], new JsonObject
        {
            ["id"] = "item_img",
            ["type"] = "image_generation",
            ["status"] = "completed",
            ["saved_path"] = savedPath,
            ["revised_prompt"] = "a cat",
            ["error"] = null
        });
        AssertItem(events[17], new JsonObject { ["id"] = "item_agentMessage", ["type"] = "agent_message", ["text"] = "final answer" });
        Assert.Equal("item_late", events[19]["item"]!["id"]!.GetValue<string>());
        Assert.Equal("completed", events[19]["item"]!["status"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["input_tokens"] = 100,
                ["cached_input_tokens"] = 40,
                ["cache_write_input_tokens"] = 20,
                ["output_tokens"] = 30,
                ["reasoning_output_tokens"] = 10
            },
            events[20]["usage"]));
    }

    [Fact]
    public async Task Exec_JsonFailedTurn_EmitsTurnFailedAndExitsOne()
    {
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions
        {
            TurnNotifications = threadId =>
            [
                Notification("turn/started", new JsonObject { ["turn"] = Turn(threadId, "running") }),
                Notification("turn/failed", new JsonObject
                {
                    ["turn"] = Turn(threadId, "failed", AgentMessage("partial")),
                    ["error"] = "provider exploded"
                })
            ]
        });

        var result = await RunExecAsync(server, ["--json", "hello"]);

        Assert.Equal(1, result.ExitCode);
        var events = ParseLines(result.StandardOutput);
        Assert.Equal(new[] { "thread.started", "turn.started", "turn.failed" }, events.Select(Describe));
        Assert.Equal("provider exploded", events[2]["error"]!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Exec_JsonCancelledTurn_CompletesOpenItemsWithoutATurnEvent()
    {
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions
        {
            TurnNotifications = threadId =>
            [
                Notification("turn/started", new JsonObject { ["turn"] = Turn(threadId, "running") }),
                Plan(threadId, ("step one", "in_progress")),
                Notification("turn/cancelled", new JsonObject
                {
                    ["turn"] = Turn(threadId, "cancelled"),
                    ["reason"] = "interrupted"
                })
            ]
        });

        var result = await RunExecAsync(server, ["--json", "hello"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(
            new[] { "thread.started", "turn.started", "item.started:todo_list", "item.completed:todo_list" },
            ParseLines(result.StandardOutput).Select(Describe));
    }

    [Fact]
    public async Task Exec_JsonRejectedTurnStart_EmitsErrorEvent()
    {
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions
        {
            TurnStartError = "Thread thread_9 already has a running turn."
        });

        var result = await RunExecAsync(server, ["resume", "thread_9", "--json", "hello"]);

        Assert.Equal(1, result.ExitCode);
        var events = ParseLines(result.StandardOutput);
        Assert.Equal(new[] { "thread.started", "error" }, events.Select(Describe));
        Assert.Equal("Thread thread_9 already has a running turn.", events[1]["message"]!.GetValue<string>());
    }

    private static List<JsonObject> ParseLines(string stdout) =>
        stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .ToList();

    private static string Describe(JsonObject evt)
    {
        var type = evt["type"]!.GetValue<string>();
        return evt["item"] is { } item ? $"{type}:{item["type"]!.GetValue<string>()}" : type;
    }

    private static void AssertItem(JsonObject evt, JsonObject expected) =>
        Assert.True(JsonNode.DeepEquals(expected, evt["item"]), evt["item"]!.ToJsonString());

    private static JsonObject Command(string command, string status, string output, int? exitCode = null)
    {
        var payload = new JsonObject
        {
            ["callId"] = "call_cmd",
            ["command"] = command,
            ["workingDirectory"] = ".",
            ["source"] = "host",
            ["status"] = status,
            ["aggregatedOutput"] = output
        };
        if (exitCode is { } code)
            payload["exitCode"] = code;
        return payload;
    }

    private static JsonObject Mcp(string status) => new()
    {
        ["toolName"] = "docs_search",
        ["providerFlatName"] = "mcp__docs__search",
        ["server"] = "docs",
        ["origin"] = "workspace",
        ["sourceToolId"] = "search",
        ["callId"] = "call_mcp",
        ["arguments"] = new JsonObject { ["q"] = "x" },
        ["status"] = status,
        ["content"] = status == "completed" ? new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "found" }) : null
    };

    private static JsonObject Search(string status, string query) => new()
    {
        ["callId"] = "call_web",
        ["status"] = status,
        ["query"] = query,
        ["action"] = null,
        ["results"] = null
    };

    private static JsonObject Image(string status, string? savedPath = null)
    {
        var payload = new JsonObject
        {
            ["callId"] = "call_img",
            ["status"] = status,
            ["revisedPrompt"] = "a cat",
            ["mediaType"] = "image/png"
        };
        if (savedPath != null)
        {
            payload["result"] = ImageBase64;
            payload["savedPath"] = savedPath;
            payload["saveStatus"] = "saved";
        }
        return payload;
    }

    private static JsonObject Plan(string threadId, params (string Content, string Status)[] todos) =>
        Notification("plan/updated", new JsonObject
        {
            ["threadId"] = threadId,
            ["title"] = string.Empty,
            ["overview"] = string.Empty,
            ["content"] = string.Empty,
            ["todos"] = new JsonArray(todos.Select(JsonNode? (todo) => new JsonObject
            {
                ["id"] = todo.Content.Replace(' ', '-'),
                ["content"] = todo.Content,
                ["priority"] = "medium",
                ["status"] = todo.Status
            }).ToArray())
        });

    private static JsonArray Todos(params (string Text, bool Completed)[] todos) =>
        new(todos.Select(JsonNode? (todo) => new JsonObject { ["text"] = todo.Text, ["completed"] = todo.Completed }).ToArray());

    private static JsonObject WithUsage(JsonObject turn)
    {
        turn["tokenUsage"] = new JsonObject
        {
            ["inputTokens"] = 100,
            ["cachedInputTokens"] = 40,
            ["cacheWriteInputTokens"] = 20,
            ["outputTokens"] = 30,
            ["reasoningOutputTokens"] = 10,
            ["totalTokens"] = 130
        };
        return turn;
    }
}
