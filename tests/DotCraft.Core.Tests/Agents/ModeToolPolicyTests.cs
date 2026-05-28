using DotCraft.Agents;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Agents;

public sealed class ModeToolPolicyTests
{
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

    private static string Exec(string command) => "shell output";
}
