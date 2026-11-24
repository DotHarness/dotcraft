using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceTurnAdmissionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DynamicToolRebind_DoesNotBlockTurnInterruptWhileResourcesPrepare(bool ownsLease)
    {
        var timeout = TimeSpan.FromSeconds(10);
        var preparation = new AdmissionRefresher();
        await using var h = new AdmissionHarness(refresher: preparation);
        var thread = await h.CreateThreadAsync();
        preparation.Block = true;
        var drain = DrainAsync(h.Service.SubmitInputAsync(thread.Id, [new TextContent("first")]));
        var turn = Assert.Single(thread.Turns);

        var permissionChecked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var leaseUpdated = false;
        Task? rebind = null;
        try
        {
            await preparation.Entered.Task.WaitAsync(timeout);
            rebind = h.Service.UpdateThreadDynamicToolsAsync(thread.Id, [], () =>
            {
                permissionChecked.TrySetResult();
                return ownsLease;
            }, () => leaseUpdated = true);
            await permissionChecked.Task.WaitAsync(timeout);
            if (!ownsLease)
                await rebind.WaitAsync(timeout);

            await h.Service.CancelTurnAsync(thread.Id, turn.Id).WaitAsync(timeout);
            Assert.Contains(await drain.WaitAsync(timeout), item => item.EventType == SessionEventType.TurnCancelled);
            Assert.False(leaseUpdated);
        }
        finally
        {
            preparation.Release.TrySetResult();
            if (rebind is not null)
                await rebind.WaitAsync(timeout);
            await drain.WaitAsync(timeout);
        }

        Assert.Equal(ownsLease, leaseUpdated);
    }
}
