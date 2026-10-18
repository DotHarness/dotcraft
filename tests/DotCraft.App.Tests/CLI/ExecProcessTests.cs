using System.Text.Json.Nodes;
using Xunit;
using static DotCraft.Tests.CLI.ExecWire;

namespace DotCraft.Tests.CLI;

public sealed class ExecProcessTests : ExecProcessTestBase
{
    [Fact]
    public async Task Exec_CompletedTurn_WritesLastAgentMessageToStdoutAndExitsZero()
    {
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions
        {
            TurnNotifications = threadId =>
            [
                Notification("item/agentMessage/delta", new JsonObject { ["threadId"] = threadId, ["delta"] = "Checking." }),
                Notification("turn/completed", new JsonObject
                {
                    ["turn"] = Turn(threadId, "completed",
                        AgentMessage("Checking."),
                        AgentMessage("final answer"),
                        AgentMessage("sent mid-turn", deliveryMode: "async"))
                })
            ]
        });

        var result = await RunExecAsync(server, ["hello"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("final answer", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task Exec_OutsideWorkspace_ExitsOneWithoutContactingServer()
    {
        await using var server = await ExecFakeAppServer.StartAsync();
        var elsewhere = CreateDirectory("elsewhere");

        var result = await RunExecAsync(server, ["hello"], workingDirectory: elsewhere);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("DotCraft workspace not found", result.StandardError, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(elsewhere, ".craft"), result.StandardError, StringComparison.Ordinal);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Exec_CdOption_RunsInThatWorkspaceFromAnotherDirectory()
    {
        await using var server = await ExecFakeAppServer.StartAsync();
        var elsewhere = CreateDirectory("elsewhere");

        var result = await RunExecAsync(server, ["-C", Workspace, "hello"], workingDirectory: elsewhere);

        Assert.Equal(0, result.ExitCode);
        var start = Assert.Single(server.ParamsOf("thread/start"));
        Assert.Equal(Workspace, start["identity"]!["workspacePath"]!.GetValue<string>());
        Assert.Null(start["cwd"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exec_PromptFromStdin_SendsStdinAsThePrompt(bool dash)
    {
        await using var server = await ExecFakeAppServer.StartAsync();

        var result = await RunExecAsync(server, dash ? ["-"] : [], stdin: "summarize the repo\n");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("summarize the repo", TurnText(server));
    }

    [Fact]
    public async Task Exec_PromptWithPipedStdin_AppendsStdinBlock()
    {
        await using var server = await ExecFakeAppServer.StartAsync();

        var result = await RunExecAsync(server, ["review", "this"], stdin: "+added line");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("review this\n\n<stdin>\n+added line\n</stdin>", TurnText(server));
    }

    [Fact]
    public async Task Exec_PromptWithEmptyPipedStdin_SendsPromptAlone()
    {
        await using var server = await ExecFakeAppServer.StartAsync();

        var result = await RunExecAsync(server, ["hello"], stdin: string.Empty);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("hello", TurnText(server));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exec_NoPromptAndEmptyStdin_ExitsTwoWithoutContactingServer(bool dash)
    {
        await using var server = await ExecFakeAppServer.StartAsync();

        var result = await RunExecAsync(server, dash ? ["-"] : [], stdin: "  \n");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("No prompt provided", result.StandardError, StringComparison.Ordinal);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Exec_Model_SetsThreadStartModel()
    {
        await using var server = await ExecFakeAppServer.StartAsync();

        var result = await RunExecAsync(server, ["-m", "gpt-test", "hello"]);

        Assert.Equal(0, result.ExitCode);
        var start = Assert.Single(server.ParamsOf("thread/start"));
        Assert.Equal("gpt-test", start["config"]!["model"]!.GetValue<string>());
        Assert.Null(start["config"]!["providerId"]);
    }

    [Fact]
    public async Task Exec_Ephemeral_StartsEphemeralThread()
    {
        await using var server = await ExecFakeAppServer.StartAsync();

        var result = await RunExecAsync(server, ["--ephemeral", "hello"]);

        Assert.Equal(0, result.ExitCode);
        var start = Assert.Single(server.ParamsOf("thread/start"));
        Assert.True(start["ephemeral"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Exec_Images_AttachLocalImagePartsResolvedAgainstCurrentDirectory()
    {
        await using var server = await ExecFakeAppServer.StartAsync();
        var images = CreateDirectory("images");
        foreach (var name in new[] { "a.png", "b.png", "c.png" })
            await File.WriteAllBytesAsync(Path.Combine(images, name), [0x89, 0x50, 0x4E, 0x47]);

        var result = await RunExecAsync(
            server,
            ["-C", Workspace, "-i", "a.png,b.png", "--image", "c.png", "compare these"],
            workingDirectory: images);

        Assert.Equal(0, result.ExitCode);
        var input = Assert.Single(server.ParamsOf("turn/start"))["input"]!.AsArray();
        Assert.Equal("text", input[0]!["type"]!.GetValue<string>());
        Assert.Equal("compare these", input[0]!["text"]!.GetValue<string>());
        Assert.Equal(
            new[] { "a.png", "b.png", "c.png" }.Select(name => Path.Combine(images, name)),
            input.Skip(1).Select(part =>
            {
                Assert.Equal("localImage", part!["type"]!.GetValue<string>());
                return part["path"]!.GetValue<string>();
            }));
    }

    [Fact]
    public async Task Exec_MissingImage_ExitsTwoWithoutContactingServer()
    {
        await using var server = await ExecFakeAppServer.StartAsync();

        var result = await RunExecAsync(server, ["-i", "missing.png", "hello"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("missing.png", result.StandardError, StringComparison.Ordinal);
        Assert.Empty(server.Requests);
    }

    [Theory]
    [InlineData("resume")]
    [InlineData("resume", "--ephemeral", "thread_9", "hello")]
    [InlineData("--unknown-option", "hello")]
    public async Task Exec_InvalidUsage_ExitsTwoWithoutContactingServer(params string[] args)
    {
        await using var server = await ExecFakeAppServer.StartAsync();

        var result = await RunExecAsync(server, args);

        Assert.Equal(2, result.ExitCode);
        Assert.NotEqual(string.Empty, result.StandardError);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Resume_ThreadId_StartsTurnOnThatThread()
    {
        await using var server = await ExecFakeAppServer.StartAsync();

        var result = await RunExecAsync(server, ["resume", "thread_9", "follow", "up"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(server.ParamsOf("thread/start"));
        var turn = Assert.Single(server.ParamsOf("turn/start"));
        Assert.Equal("thread_9", turn["threadId"]!.GetValue<string>());
        Assert.Equal("follow up", TurnText(server));
        Assert.Contains("[CLI] Thread: thread_9", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resume_Last_ContinuesMostRecentWorkspaceThread()
    {
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions { ListedThreadIds = ["thread_7"] });

        var result = await RunExecAsync(server, ["resume", "--last", "next", "step"]);

        Assert.Equal(0, result.ExitCode);
        var list = Assert.Single(server.ParamsOf("thread/list"));
        Assert.Equal("workspace", list["scope"]!.GetValue<string>());
        Assert.False(list["includeArchived"]!.GetValue<bool>());
        Assert.False(list["includeSubAgents"]!.GetValue<bool>());
        Assert.Equal(1, list["limit"]!.GetValue<int>());
        Assert.Equal("cli", list["identity"]!["channelName"]!.GetValue<string>());
        Assert.Equal("local", list["identity"]!["userId"]!.GetValue<string>());
        Assert.Equal(Workspace, list["identity"]!["workspacePath"]!.GetValue<string>());
        Assert.Equal("thread_7", Assert.Single(server.ParamsOf("turn/start"))["threadId"]!.GetValue<string>());
        Assert.Equal("next step", TurnText(server));
        Assert.Contains("[CLI] Thread: thread_7", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resume_LastWithNoThreads_ExitsOne()
    {
        await using var server = await ExecFakeAppServer.StartAsync();

        var result = await RunExecAsync(server, ["resume", "--last", "hello"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("No thread found in this workspace.", result.StandardError, StringComparison.Ordinal);
        Assert.Empty(server.ParamsOf("turn/start"));
        Assert.Empty(server.ParamsOf("thread/start"));
    }

    [Fact]
    public async Task Resume_TurnStartRejected_ExitsOneWithServerMessage()
    {
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions
        {
            TurnStartError = "Thread thread_9 already has a running turn."
        });

        var result = await RunExecAsync(server, ["resume", "thread_9", "hello"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        Assert.Contains("Thread thread_9 already has a running turn.", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resume_Model_ReplacesModelAndKeepsTheRestOfTheConfiguration()
    {
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions
        {
            ThreadConfiguration = new JsonObject
            {
                ["model"] = "old-model",
                ["providerId"] = "workspace-provider",
                ["reasoning"] = new JsonObject { ["effort"] = "high" },
                ["speed"] = "fast",
                ["approvalPolicy"] = "default",
                ["mcpServers"] = new JsonArray(new JsonObject { ["name"] = "docs", ["transport"] = "stdio" })
            }
        });

        var result = await RunExecAsync(server, ["resume", "thread_9", "-m", "new-model", "hello"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("thread_9", Assert.Single(server.ParamsOf("thread/read"))["threadId"]!.GetValue<string>());
        var update = Assert.Single(server.ParamsOf("thread/config/update"));
        Assert.Equal("thread_9", update["threadId"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["model"] = "new-model",
                ["providerId"] = "workspace-provider",
                ["approvalPolicy"] = "default",
                ["mcpServers"] = new JsonArray(new JsonObject { ["name"] = "docs", ["transport"] = "stdio" })
            },
            update["config"]));

        var methods = server.Requests.Select(r => r.Method).ToList();
        Assert.True(methods.IndexOf("thread/config/update") < methods.IndexOf("turn/start"));
    }

    [Fact]
    public async Task Exec_DeclinedApproval_InitializesWithoutApprovalSupportAndFollowsTheTurnOutcome()
    {
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions
        {
            TurnNotifications = threadId =>
            [
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
                Completed(threadId, "skipped the cleanup")
            ]
        });

        var result = await RunExecAsync(server, ["clean up"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("skipped the cleanup", result.StandardOutput.Trim());
        var capabilities = Assert.Single(server.ParamsOf("initialize"))["capabilities"]!;
        Assert.False(capabilities["approvalSupport"]!.GetValue<bool>());
        Assert.False(capabilities["requestUserInputSupport"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Exec_AutoApprove_StartsThreadWithAutoApprovePolicy()
    {
        await using var server = await ExecFakeAppServer.StartAsync();

        var result = await RunExecAsync(server, ["--auto-approve", "hello"]);

        Assert.Equal(0, result.ExitCode);
        var config = Assert.Single(server.ParamsOf("thread/start"))["config"]!;
        Assert.True(JsonNode.DeepEquals(new JsonObject { ["approvalPolicy"] = "autoApprove" }, config));
    }

    [Fact]
    public async Task Resume_AutoApprove_UpdatesOnlyTheApprovalPolicy()
    {
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions
        {
            ThreadConfiguration = new JsonObject
            {
                ["model"] = "kept-model",
                ["providerId"] = "workspace-provider",
                ["reasoning"] = new JsonObject { ["effort"] = "high" },
                ["approvalPolicy"] = "default"
            }
        });

        var result = await RunExecAsync(server, ["resume", "thread_9", "--auto-approve", "hello"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Single(server.ParamsOf("thread/read"));
        var update = Assert.Single(server.ParamsOf("thread/config/update"));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["model"] = "kept-model",
                ["providerId"] = "workspace-provider",
                ["reasoning"] = new JsonObject { ["effort"] = "high" },
                ["approvalPolicy"] = "autoApprove"
            },
            update["config"]));
    }

    [Fact]
    public async Task Resume_AutoApproveWithModel_AppliesBothInOneUpdate()
    {
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions
        {
            ThreadConfiguration = new JsonObject
            {
                ["model"] = "old-model",
                ["providerId"] = "workspace-provider",
                ["speed"] = "fast",
                ["approvalPolicy"] = "prompt"
            }
        });

        var result = await RunExecAsync(server, ["resume", "thread_9", "--auto-approve", "-m", "new-model", "hello"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Single(server.ParamsOf("thread/read"));
        var update = Assert.Single(server.ParamsOf("thread/config/update"));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["model"] = "new-model",
                ["providerId"] = "workspace-provider",
                ["approvalPolicy"] = "autoApprove"
            },
            update["config"]));
    }
}
