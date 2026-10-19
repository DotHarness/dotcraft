using DotCraft.Agents;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceInterruptionTests
{
    [Fact]
    public async Task CallerCancellationAfterInitialHistoryFlush_PreservesInputOnce()
    {
        using var cts = new CancellationTokenSource();
        var writer = new CancelAfterHistoryFlushWriter(cts);
        await using var store = new ThreadStore(root, null, null, writer);
        var client = new InterruptibleClient { Block = false };
        await using var factory = Factory();
        var service = Service(factory, client, store);
        var thread = await service.CreateThreadAsync(Identity());
        await Drain(service.SubmitInputAsync(thread.Id, [new TextContent("previous")]));

        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ThreadRuntimeSignalForBroadcast = (_, signal, _) =>
        {
            if (signal == SessionThreadRuntimeSignal.TurnCancelled) cancelled.TrySetResult();
        };
        writer.CancelAfterNextHistoryFlush = true;
        var run = Drain(service.SubmitInputAsync(thread.Id, [new TextContent("cancelled initial input")], ct: cts.Token));
        try { await run.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var history = await store.LoadModelHistoryAsync(thread.Id);
        Assert.Single(history, message => message.Role == ChatRole.User && message.Text.Contains("cancelled initial input"));
        Assert.DoesNotContain(history, IsMarker);
        await Drain(service.SubmitInputAsync(thread.Id, [new TextContent("next")]));
        Assert.Single(client.Messages, message => message.Role == ChatRole.User && message.Text.Contains("cancelled initial input"));
        Assert.DoesNotContain(client.Messages, IsMarker);

        await using var reloadedStore = new ThreadStore(root);
        service = Service(factory, client, reloadedStore);
        await service.GetThreadAsync(thread.Id);
        await Drain(service.SubmitInputAsync(thread.Id, [new TextContent("after reload")]));
        Assert.Single(client.Messages, message => message.Role == ChatRole.User && message.Text.Contains("cancelled initial input"));
        Assert.DoesNotContain(client.Messages, IsMarker);
    }

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

    private sealed class CancelAfterHistoryFlushWriter(CancellationTokenSource cancellation) : IRolloutWriter
    {
        private readonly OrderedRolloutWriter _inner = new();
        private bool _cancelAfterFlush;
        public bool CancelAfterNextHistoryFlush { get; set; }

        public async Task AddBatchAsync(
            string threadId, string path, IReadOnlyList<ThreadRolloutRecord> records, CancellationToken ct = default)
        {
            await _inner.AddBatchAsync(threadId, path, records, ct);
            if (CancelAfterNextHistoryFlush && records.Any(record => record.Kind == RolloutKinds.ModelHistoryMessagesAppended))
            {
                CancelAfterNextHistoryFlush = false;
                _cancelAfterFlush = true;
            }
        }

        public async Task<RolloutWriteReceipt> FlushAsync(string threadId, CancellationToken ct = default)
        {
            var receipt = await _inner.FlushAsync(threadId, ct);
            if (_cancelAfterFlush)
            {
                _cancelAfterFlush = false;
                cancellation.Cancel();
            }
            return receipt;
        }

        public Task CloseAsync(string threadId, CancellationToken ct = default) => _inner.CloseAsync(threadId, ct);

        public Task ShutdownAllAsync(CancellationToken ct = default) => _inner.ShutdownAllAsync(ct);
    }
}
