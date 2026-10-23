using DotCraft.Agents;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using ToolExecutionPayload = DotCraft.Sessions.ToolExecutionPayload;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class ModeToolPolicyTests
{
    [Fact]
    public async Task AgentMode_DeniesCreatePlanWithRecoverableMessage()
    {
        var result = await RunToolCallAsync(AgentMode.Agent, "CreatePlan", []);

        Assert.Contains("MODE_POLICY_DENIED", result, StringComparison.Ordinal);
        Assert.Contains("Tool: CreatePlan", result, StringComparison.Ordinal);
        Assert.Contains("CurrentMode: Agent", result, StringComparison.Ordinal);
        Assert.Contains("use TodoWrite to track", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CreatePlan")]
    [InlineData(ImageGenerationToolSource.ToolName)]
    [InlineData("exec")]
    public async Task PlanMode_AllowsPlanningTools(string toolName)
    {
        Assert.Equal(ToolOutput, await RunToolCallAsync(AgentMode.Plan, toolName, []));
    }

    [Theory]
    [InlineData("WriteFile")]
    [InlineData("Transfer")]
    [InlineData(GoalToolNames.GetGoal)]
    [InlineData(GoalToolNames.CreateGoal)]
    public async Task PlanMode_DeniesMutatingToolsWithRecoverableMessage(string toolName)
    {
        var result = await RunToolCallAsync(AgentMode.Plan, toolName, new() { ["path"] = "a.txt", ["content"] = "hello" });

        Assert.Contains("MODE_POLICY_DENIED", result, StringComparison.Ordinal);
        Assert.Contains($"Tool: {toolName}", result, StringComparison.Ordinal);
        Assert.Contains("NextAllowedActions:", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("y", false)]
    public async Task PlanMode_AllowsWriteStdinOnlyToPoll(string input, bool allowed)
    {
        var result = await RunToolCallAsync(AgentMode.Plan, "WriteStdin", new() { ["sessionId"] = "term_1", ["input"] = input });

        Assert.Equal(allowed, result == ToolOutput);
    }

    [Fact]
    public async Task StreamingClient_AppliesToolCallPolicyBeforeToolResolution()
    {
        var inner = new ToolCallChatClient("WriteFile", new Dictionary<string, object?>
        {
            ["path"] = "a.txt",
            ["content"] = "hello"
        });
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            ToolCallPolicy = call => string.Equals(call.Name, "WriteFile", StringComparison.Ordinal)
                ? ModeToolPolicyDecision.DenyRecoverable(
                    """
PROFILE_TOOL_POLICY_DENIED
Tool: WriteFile
Reason: denied by test policy
""")
                : ModeToolPolicyDecision.Allow
        };

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        var result = Assert.Single(inner.Calls[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Contains("PROFILE_TOOL_POLICY_DENIED", result.Result?.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("not found", result.Result?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("git status")]
    [InlineData("git --no-pager diff")]
    [InlineData("git diff --stat; git log -1")]
    public async Task PlanMode_AllowsReadOnlyShellCommands(string command)
    {
        Assert.Equal(ToolOutput, await RunToolCallAsync(AgentMode.Plan, "Exec", new() { ["command"] = command }));
    }

    [Theory]
    [InlineData("git push origin main", "git push")]
    [InlineData("git diff --stat && rm -rf build", "'rm'")]
    [InlineData("git -C ../other status", "'-C'")]
    [InlineData("git diff --ext-diff", "'--ext-diff'")]
    [InlineData("git status\nrm -rf build", "'rm'")]
    [InlineData("find . -delete", "'-delete'")]
    [InlineData("rg --pre sh pattern", "'--pre'")]
    [InlineData("sed -n -i s/a/b/ README.md", "sed")]
    public async Task PlanMode_DeniesMutatingShellCommand(string command, string expectedReasonFragment)
    {
        var result = await RunToolCallAsync(AgentMode.Plan, "Exec", new() { ["command"] = command });

        Assert.Contains("MODE_POLICY_DENIED", result, StringComparison.Ordinal);
        Assert.Contains(expectedReasonFragment, result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamingClient_DeniedPlanModeExecCompletesPendingCommandExecution()
    {
        const string callId = "call-1";
        const string command = "git push origin main";
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
            ThreadId = turn.ThreadId,
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

    private const string ToolOutput = "tool output";

    private static async Task<string?> RunToolCallAsync(AgentMode mode, string toolName, Dictionary<string, object?> arguments)
    {
        var modeManager = new AgentModeManager();
        modeManager.SwitchMode(mode);
        var inner = new ToolCallChatClient(toolName, arguments);
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [AIFunctionFactory.Create(() => ToolOutput, name: toolName)],
            ModeToolPolicy = new ModeToolPolicy(modeManager).Evaluate
        };

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        return Assert.Single(inner.Calls[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>()).Result?.ToString();
    }
}
