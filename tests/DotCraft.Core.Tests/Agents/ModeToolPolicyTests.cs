using DotCraft.Agents;
using DotCraft.Protocol;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Agents;

public sealed class ModeToolPolicyTests
{
    [Fact]
    public async Task StreamingClient_DeniesAgentModeCreatePlanWithRecoverableMessage()
    {
        var modeManager = new AgentModeManager();
        var inner = new ToolCallChatClient("CreatePlan", new Dictionary<string, object?>());
        var tool = AIFunctionFactory.Create(() => "plan saved", name: "CreatePlan");
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool],
            ModeToolPolicy = new ModeToolPolicy(modeManager).Evaluate
        };

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        var result = Assert.Single(inner.Calls[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Contains("MODE_POLICY_DENIED", result.Result?.ToString(), StringComparison.Ordinal);
        Assert.Contains("Tool: CreatePlan", result.Result?.ToString(), StringComparison.Ordinal);
        Assert.Contains("CurrentMode: Agent", result.Result?.ToString(), StringComparison.Ordinal);
        Assert.Contains("TodoWrite/UpdateTodos", result.Result?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamingClient_AllowsPlanModeCreatePlan()
    {
        var modeManager = new AgentModeManager();
        modeManager.SwitchMode(AgentMode.Plan);
        var inner = new ToolCallChatClient("CreatePlan", new Dictionary<string, object?>());
        var tool = AIFunctionFactory.Create(() => "plan saved", name: "CreatePlan");
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool],
            ModeToolPolicy = new ModeToolPolicy(modeManager).Evaluate
        };

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        var result = Assert.Single(inner.Calls[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Equal("plan saved", result.Result?.ToString());
    }

    [Fact]
    public async Task StreamingClient_DeniesPlanModeFileWriteWithRecoverableMessage()
    {
        var modeManager = new AgentModeManager();
        modeManager.SwitchMode(AgentMode.Plan);
        var inner = new ToolCallChatClient("WriteFile", new Dictionary<string, object?>
        {
            ["path"] = "a.txt",
            ["content"] = "hello"
        });
        var tool = AIFunctionFactory.Create(() => "wrote", name: "WriteFile");
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool],
            ModeToolPolicy = new ModeToolPolicy(modeManager).Evaluate
        };

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        var result = Assert.Single(inner.Calls[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Contains("MODE_POLICY_DENIED", result.Result?.ToString(), StringComparison.Ordinal);
        Assert.Contains("Tool: WriteFile", result.Result?.ToString(), StringComparison.Ordinal);
        Assert.Contains("NextAllowedActions:", result.Result?.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("git status")]
    [InlineData("Get-Content README.md")]
    [InlineData("rg ModeToolPolicy")]
    public async Task StreamingClient_AllowsPlanModeReadOnlyShellCommands(string command)
    {
        var modeManager = new AgentModeManager();
        modeManager.SwitchMode(AgentMode.Plan);
        var inner = new ToolCallChatClient("Exec", new Dictionary<string, object?> { ["command"] = command });
        var tool = AIFunctionFactory.Create(Exec, name: "Exec");
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool],
            ModeToolPolicy = new ModeToolPolicy(modeManager).Evaluate
        };

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        var result = Assert.Single(inner.Calls[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Equal("shell output", result.Result?.ToString());
    }

    [Fact]
    public async Task StreamingClient_DeniesPlanModeMutatingShellCommand()
    {
        var modeManager = new AgentModeManager();
        modeManager.SwitchMode(AgentMode.Plan);
        var inner = new ToolCallChatClient("Exec", new Dictionary<string, object?> { ["command"] = "dotnet test > out.txt" });
        var tool = AIFunctionFactory.Create(Exec, name: "Exec");
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool],
            ModeToolPolicy = new ModeToolPolicy(modeManager).Evaluate
        };

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        var result = Assert.Single(inner.Calls[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Contains("MODE_POLICY_DENIED", result.Result?.ToString(), StringComparison.Ordinal);
        Assert.Contains("read-only shell commands", result.Result?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamingClient_DeniedPlanModeExecCompletesPendingCommandExecution()
    {
        const string callId = "call-1";
        const string command = "Get-Content README.md | Select-String DotCraft";
        var workingDirectory = Directory.GetCurrentDirectory();
        var modeManager = new AgentModeManager();
        modeManager.SwitchMode(AgentMode.Plan);
        var inner = new ToolCallChatClient("Exec", new Dictionary<string, object?>
        {
            ["command"] = command
        });
        var invoked = false;
        var tool = AIFunctionFactory.Create((string _) =>
        {
            invoked = true;
            return "shell output";
        }, name: "Exec");
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool],
            ModeToolPolicy = new ModeToolPolicy(modeManager).Evaluate
        };

        var turn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = "thread_test",
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
        var commandCompleted = new List<SessionItem>();
        var toolCompleted = new List<SessionItem>();
        var commandItem = CreatePendingCommandExecution(turn, "item_command", callId, command, workingDirectory);
        var toolItem = CreatePendingToolExecution(turn, "item_tool", callId, "Exec");
        var commandContext = new CommandExecutionRuntimeContext
        {
            ThreadId = turn.ThreadId,
            TurnId = turn.Id,
            Turn = turn,
            NextItemSequence = () => turn.Items.Count + 1,
            EmitItemStarted = _ => { },
            EmitItemDelta = (_, _) => { },
            EmitItemCompleted = commandCompleted.Add,
            SupportsCommandExecutionStreaming = true
        };
        commandContext.RegisterPending(new PendingCommandExecutionRegistration
        {
            CallId = callId,
            Command = command,
            WorkingDirectory = workingDirectory,
            Source = "host",
            Item = commandItem
        });
        commandContext.RegisterPendingShellExecution(new PendingShellExecutionRegistration
        {
            CallId = callId,
            Command = command,
            WorkingDirectory = workingDirectory,
            Source = "host"
        });
        var toolContext = new ToolExecutionRuntimeContext
        {
            TurnId = turn.Id,
            Turn = turn,
            NextItemSequence = () => turn.Items.Count + 1,
            EmitItemStarted = _ => { },
            EmitItemCompleted = toolCompleted.Add,
            SupportsToolExecutionLifecycle = true
        };
        toolContext.RegisterPending(new PendingToolExecutionRegistration
        {
            CallId = callId,
            ToolName = "Exec",
            Item = toolItem
        });

        using var commandScope = CommandExecutionRuntimeScope.Set(commandContext);
        using var toolScope = ToolExecutionRuntimeScope.Set(toolContext);

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        Assert.False(invoked);
        var result = Assert.Single(inner.Calls[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Contains("MODE_POLICY_DENIED", result.Result?.ToString(), StringComparison.Ordinal);

        Assert.Same(commandItem, Assert.Single(commandCompleted));
        Assert.Equal(ItemStatus.Completed, commandItem.Status);
        Assert.NotNull(commandItem.CompletedAt);
        var commandPayload = Assert.IsType<CommandExecutionPayload>(commandItem.Payload);
        Assert.Equal(callId, commandPayload.CallId);
        Assert.Equal(command, commandPayload.Command);
        Assert.Equal(workingDirectory, commandPayload.WorkingDirectory);
        Assert.Equal("host", commandPayload.Source);
        Assert.Equal("failed", commandPayload.Status);
        Assert.Null(commandPayload.ExitCode);
        Assert.NotNull(commandPayload.DurationMs);
        Assert.Contains("MODE_POLICY_DENIED", commandPayload.AggregatedOutput, StringComparison.Ordinal);

        Assert.Same(toolItem, Assert.Single(toolCompleted));
        Assert.Equal(ItemStatus.Completed, toolItem.Status);
        var toolPayload = Assert.IsType<ToolExecutionPayload>(toolItem.Payload);
        Assert.Equal("failed", toolPayload.Status);
        Assert.False(toolPayload.Success);
        Assert.Contains("MODE_POLICY_DENIED", toolPayload.ErrorMessage, StringComparison.Ordinal);

        Assert.Null(commandContext.TryClaimPending(command, workingDirectory));
        Assert.Null(commandContext.TryClaimPendingShellExecution(command, workingDirectory));
    }

    [Theory]
    [InlineData(GoalToolNames.GetGoal)]
    [InlineData(GoalToolNames.CreateGoal)]
    [InlineData(GoalToolNames.UpdateGoal)]
    public async Task StreamingClient_DeniesPlanModeGoalTools(string toolName)
    {
        var modeManager = new AgentModeManager();
        modeManager.SwitchMode(AgentMode.Plan);
        var inner = new ToolCallChatClient(toolName, new Dictionary<string, object?>());
        var tool = AIFunctionFactory.Create(() => "goal changed", name: toolName);
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool],
            ModeToolPolicy = new ModeToolPolicy(modeManager).Evaluate
        };

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        var result = Assert.Single(inner.Calls[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Contains("MODE_POLICY_DENIED", result.Result?.ToString(), StringComparison.Ordinal);
        Assert.Contains($"Tool: {toolName}", result.Result?.ToString(), StringComparison.Ordinal);
    }

    private sealed class ToolCallChatClient(string toolName, IDictionary<string, object?> arguments) : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(chatMessages.ToList());
            if (Calls.Count == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent("call-1", toolName, new Dictionary<string, object?>(arguments))
                ]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static SessionItem CreatePendingCommandExecution(
        SessionTurn turn,
        string itemId,
        string callId,
        string command,
        string workingDirectory)
    {
        var item = new SessionItem
        {
            Id = itemId,
            TurnId = turn.Id,
            Type = ItemType.CommandExecution,
            Status = ItemStatus.Started,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new CommandExecutionPayload
            {
                CallId = callId,
                Command = command,
                WorkingDirectory = workingDirectory,
                Source = "host",
                Status = "inProgress",
                AggregatedOutput = string.Empty
            }
        };
        turn.Items.Add(item);
        return item;
    }

    private static SessionItem CreatePendingToolExecution(
        SessionTurn turn,
        string itemId,
        string callId,
        string toolName)
    {
        var item = new SessionItem
        {
            Id = itemId,
            TurnId = turn.Id,
            Type = ItemType.ToolExecution,
            Status = ItemStatus.Started,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new ToolExecutionPayload
            {
                CallId = callId,
                ToolName = toolName,
                Status = "inProgress"
            }
        };
        turn.Items.Add(item);
        return item;
    }

    private static string Exec(string command) => "shell output";
}
