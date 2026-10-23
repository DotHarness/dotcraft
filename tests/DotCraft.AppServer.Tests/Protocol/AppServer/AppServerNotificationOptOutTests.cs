using Xunit;
using Methods = DotCraft.Protocol.AppServer.AppServerMethodNames;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerNotificationOptOutTests
{
    [Theory]
    [InlineData(true, new[] { Methods.TurnCompleted },
        new[] { Methods.TurnStarted, Methods.ItemStarted, Methods.AgentMessageDelta, Methods.ItemCompleted })]
    [InlineData(true, new[] { Methods.AgentMessageDelta, Methods.ReasoningDelta },
        new[] { Methods.TurnStarted, Methods.ItemStarted, Methods.ItemCompleted, Methods.TurnCompleted })]
    [InlineData(false, new[] { Methods.ItemStarted },
        new[] { Methods.TurnStarted, Methods.ItemCompleted, Methods.TurnCompleted })]
    public async Task OptOutAndStreamingSupport_FilterTurnNotifications(
        bool streamingSupport,
        string[] optOutMethods,
        string[] expectedMethods)
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(streamingSupport: streamingSupport, optOutMethods: [.. optOutMethods]);
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        await harness.ExecuteRequestAsync(harness.BuildRequest(Methods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        }));

        var all = await harness.Transport.WaitAndDrainAsync(expectedMethods.Length + 1, TimeSpan.FromSeconds(10));
        AppServerTestHarness.AssertIsSuccessResponse(all[0]);
        Assert.Equal(
            expectedMethods,
            all.Skip(1).Select(d => d.RootElement.GetProperty("method").GetString()));
    }
}
