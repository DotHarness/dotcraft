using DotCraft.Protocol.AppServer;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerTurnModeTests
{
    [Fact]
    public async Task TurnStart_CarriesExecutionModeIntoAdmission()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            mode = "agent",
            input = new[] { new { type = "text", text = "Implement the plan." } }
        }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.Equal("agent", harness.Service.LastSubmittedTurnSettings?.Mode);
    }
}
