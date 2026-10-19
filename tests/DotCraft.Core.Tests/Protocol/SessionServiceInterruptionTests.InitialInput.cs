using DotCraft.Agents;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceInterruptionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CancelDuringContextPreparation_PreservesInitialInput(bool ephemeral, bool callerCancellation)
    {
        var client = new InterruptibleClient { Block = false };
        var contextProvider = new BlockingContextProvider();
        var agent = new ChatClientAgent(new StreamingFunctionInvokingChatClient(client), contextProvider: contextProvider);
        await using var factory = Factory();
        var store = new ThreadStore(root);
        var service = new SessionService(factory, agent, new SessionPersistenceService(store), new SessionGate());
        var thread = await service.CreateThreadAsync(Identity());
        thread.Ephemeral = ephemeral;
        await Drain(service.SubmitInputAsync(thread.Id, [new TextContent("previous")]));

        contextProvider.Block = true;
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ThreadRuntimeSignalForBroadcast = (_, signal, _) =>
        {
            if (signal == SessionThreadRuntimeSignal.TurnCancelled) cancelled.TrySetResult();
        };
        using var cts = new CancellationTokenSource();
        var run = Drain(service.SubmitInputAsync(thread.Id, [new TextContent("early")], ct: cts.Token));
        await contextProvider.Preparing.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (callerCancellation) cts.Cancel();
        else await service.CancelTurnAsync(thread.Id, thread.Turns[^1].Id);
        try { await run.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (OperationCanceledException) when (callerCancellation) { }
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));

        contextProvider.Block = false;
        if (!ephemeral)
        {
            var history = await store.LoadModelHistoryAsync(thread.Id);
            Assert.Single(history, message => message.Role == ChatRole.User && message.Text.Contains("early"));
            Assert.Equal(callerCancellation ? 0 : 1, history.Count(IsMarker));
            service = Service(factory, client, new ThreadStore(root));
            await service.GetThreadAsync(thread.Id);
        }
        await Drain(service.SubmitInputAsync(thread.Id, [new TextContent("next")]));
        Assert.Single(client.Messages, message => message.Role == ChatRole.User && !IsMarker(message) && message.Text.Contains("previous"));
        var input = Assert.Single(client.Messages, message => message.Role == ChatRole.User && message.Text.Contains("early"));
        Assert.Single(client.Messages, message => message.Role == ChatRole.Assistant && message.Text == "partial");
        Assert.Equal(callerCancellation ? 0 : 1, client.Messages.Count(IsMarker));
        if (!callerCancellation)
            Assert.True(client.Messages.IndexOf(input) < client.Messages.FindIndex(IsMarker));
        Assert.True(client.Messages.IndexOf(input) < client.Messages.FindLastIndex(message => message.Text.Contains("next")));
    }

    private sealed class BlockingContextProvider : AIContextProvider
    {
        public bool Block { get; set; }
        public TaskCompletionSource Preparing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async ValueTask<AIContext> ProvideAIContextAsync(
            InvokingContext context, CancellationToken cancellationToken = default)
        {
            if (Block)
            {
                Preparing.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            return new AIContext();
        }
    }
}
