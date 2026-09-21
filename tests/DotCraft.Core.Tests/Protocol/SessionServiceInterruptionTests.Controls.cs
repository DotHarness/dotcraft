using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Tools;
using DotCraft.Tests.Security.ShellCommands;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceInterruptionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingInteractiveWait_PersistsInterruption(bool approval)
    {
        var client = new InterruptibleClient();
        client.BeforeResponse = async () =>
        {
            if (approval)
                await new SessionScopedApprovalService(new AutoApproveApprovalService())
                    .RequestShellApprovalAsync(ShellApprovalRequests.For("test", root));
            else
                await RequestUserInputRuntimeScope.Current!.RequestAsync(
                    [new RequestUserInputQuestion { Id = "choice", Header = "Choice", Question = "Choose" }]);
        };
        await using var factory = Factory(client: client);
        var store = new ThreadStore(root);
        var service = Service(factory, client, store);
        var thread = await service.CreateThreadAsync(Identity(), new ThreadConfiguration { ApprovalPolicy = ApprovalPolicy.Prompt });
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ThreadRuntimeSignalForBroadcast = (_, _, turn) =>
        {
            if (turn?.Status is TurnStatus.WaitingApproval or TurnStatus.WaitingInput) waiting.TrySetResult();
        };
        var run = Drain(service.SubmitInputAsync(thread.Id, [new TextContent("ask")]));
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (approval)
        {
            var turn = thread.Turns[^1];
            var request = Assert.Single(turn.Items.Select(item => item.Payload).OfType<ApprovalRequestPayload>());
            await service.ResolveApprovalAsync(thread.Id, turn.Id, request.RequestId, SessionApprovalDecision.CancelTurn);
        }
        else
            await service.CancelTurnAsync(thread.Id, thread.Turns[^1].Id);
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(TurnStatus.Cancelled, thread.Turns[^1].Status);
        Assert.Single(await store.LoadModelHistoryAsync(thread.Id), IsMarker);
    }

    [Fact]
    public async Task ConsecutiveCancelledTurns_HaveDistinctMarkersAndRollbackRemovesOnlyDiscardedTurn()
    {
        var client = new InterruptibleClient();
        await using var factory = Factory();
        var store = new ThreadStore(root);
        var service = Service(factory, client, store);
        var thread = await service.CreateThreadAsync(Identity());
        for (var i = 0; i < 2; i++)
        {
            client.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var run = Drain(service.SubmitInputAsync(thread.Id, [new TextContent($"request {i}")]));
            await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await service.CancelTurnAsync(thread.Id, thread.Turns[^1].Id);
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
        var history = await store.LoadModelHistoryAsync(thread.Id);
        Assert.Equal(thread.Turns.Select(turn => turn.Id), history.Where(IsMarker).Select(TurnInterruption.GetTurnId));
        await service.RollbackThreadAsync(thread.Id, 1);
        client.Block = false;
        await Drain(service.SubmitInputAsync(thread.Id, [new TextContent("replacement")]));
        Assert.Single(client.Messages, IsMarker);
    }

    [Fact]
    public async Task CompletedTurnItemCut_DoesNotGenerateInterruption()
    {
        var client = new InterruptibleClient { Block = false };
        await using var factory = Factory();
        var store = new ThreadStore(root);
        var service = Service(factory, client, store);
        var thread = await service.CreateThreadAsync(Identity());
        await Drain(service.SubmitInputAsync(thread.Id, [new TextContent("complete")]));
        var turn = thread.Turns[^1];
        var fork = await service.ForkThreadAsync(thread.Id, new ThreadForkOptions
        {
            ForkPoint = new ThreadForkPoint { TurnId = turn.Id, ItemId = turn.Input!.Id, Position = "after" }
        });
        Assert.DoesNotContain(await store.LoadModelHistoryAsync(fork.Id), IsMarker);
    }
}
