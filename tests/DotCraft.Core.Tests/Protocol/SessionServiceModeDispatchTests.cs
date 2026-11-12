using System.Runtime.CompilerServices;
using DotCraft.Agents;
using DotCraft.Sessions;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceModeDispatchTests : SessionServiceRuntimeSignalTestBase
{
    [Theory]
    [InlineData("agent", "plan", false)]
    [InlineData("plan", "agent", false)]
    [InlineData("plan", "agent", true)]
    public async Task CommittedMode_AuthorizesWritesThroughCachedAgent(
        string initialMode, string nextMode, bool submitWithMode)
    {
        var client = new WritingChatClient(_tempDir);
        var policies = new ThreadToolDispatchPolicyRegistry();
        var dispatcher = new ToolDispatcher(policyEvaluator: policies);
        await using var factory = CreateAgentFactory(client, [new FileWritingToolSource()], toolDispatcher: dispatcher);
        await using var store = new ThreadStore(_tempDir);
        var service = new SessionService(factory, factory.CreateDefaultAgent(),
            new SessionPersistenceService(store), new SessionGate(), toolDispatchPolicyRegistry: policies);
        var thread = await service.CreateThreadAsync(MakeIdentity(), new ThreadConfiguration { Mode = initialMode });
        await service.RefreshThreadAgentAsync(thread.Id);

        if (!submitWithMode)
            await service.SetThreadModeAsync(thread.Id, nextMode);
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("Write the file.")],
            inputSnapshot: submitWithMode ? new SessionInputSnapshot { Mode = nextMode } : null))
            .WaitAsync(TimeSpan.FromSeconds(10));

        AssertWriteOutcome(client, 1, nextMode == "agent");
    }

    [Fact]
    public async Task ModeChangeDuringTurn_PreservesActiveWriteAuthorityAndRestrictsNextTurn()
    {
        var client = new WritingChatClient(_tempDir, pauseFirstCall: true);
        var policies = new ThreadToolDispatchPolicyRegistry();
        var dispatcher = new ToolDispatcher(policyEvaluator: policies);
        await using var factory = CreateAgentFactory(client, [new FileWritingToolSource()], toolDispatcher: dispatcher);
        await using var store = new ThreadStore(_tempDir);
        var service = new SessionService(factory, factory.CreateDefaultAgent(),
            new SessionPersistenceService(store), new SessionGate(), toolDispatchPolicyRegistry: policies);
        var thread = await service.CreateThreadAsync(MakeIdentity(), new ThreadConfiguration { Mode = "agent" });
        await service.RefreshThreadAgentAsync(thread.Id);

        var activeTurn = DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("Write the first file.")]));
        try
        {
            await client.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await service.SetThreadModeAsync(thread.Id, "plan");
        }
        finally
        {
            client.ReleaseFirstCall.TrySetResult();
        }
        await activeTurn.WaitAsync(TimeSpan.FromSeconds(10));
        AssertWriteOutcome(client, 1, allowed: true);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("Write the second file.")]))
            .WaitAsync(TimeSpan.FromSeconds(10));
        AssertWriteOutcome(client, 2, allowed: false);
    }

    private static void AssertWriteOutcome(WritingChatClient client, int turnNumber, bool allowed)
    {
        var result = client.Results[turnNumber - 1];
        if (allowed)
        {
            Assert.Null(StreamingFunctionInvokingChatClient.GetToolResultErrorCode(result));
            Assert.Equal("written", File.ReadAllText(client.FilePath(turnNumber)));
        }
        else
        {
            Assert.Equal(ToolErrorCodes.Unauthorized,
                StreamingFunctionInvokingChatClient.GetToolResultErrorCode(result));
            Assert.False(File.Exists(client.FilePath(turnNumber)));
        }
    }

    private sealed class FileWritingToolSource : AIFunctionToolSource
    {
        public override string SourceId => "mode-dispatch-write";

        protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
        {
            yield return AIFunctionFactory.Create(
                async (string path, CancellationToken cancellationToken) =>
                {
                    await File.WriteAllTextAsync(path, "written", cancellationToken);
                    return "written";
                },
                name: "WriteFile");
        }
    }

    private sealed class WritingChatClient(string directory, bool pauseFirstCall = false) : IChatClient
    {
        private int _turnNumber;
        private string? _pendingCallId;
        public List<FunctionResultContent> Results { get; } = [];
        public TaskCompletionSource FirstCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstCall { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string FilePath(int turnNumber) => Path.Combine(directory, $"write-{turnNumber}.txt");

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "done")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_pendingCallId is null)
            {
                _turnNumber++;
                if (_turnNumber == 1 && pauseFirstCall)
                {
                    FirstCallStarted.TrySetResult();
                    await ReleaseFirstCall.Task.WaitAsync(cancellationToken);
                }
                _pendingCallId = $"write-call-{_turnNumber}";
                yield return new ChatResponseUpdate(ChatRole.Assistant,
                    [new FunctionCallContent(_pendingCallId, "WriteFile",
                        new Dictionary<string, object?> { ["path"] = FilePath(_turnNumber) })]);
            }
            else
            {
                Results.Add(Assert.Single(chatMessages.SelectMany(message => message.Contents)
                    .OfType<FunctionResultContent>(), result => result.CallId == _pendingCallId));
                _pendingCallId = null;
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("done")]);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
