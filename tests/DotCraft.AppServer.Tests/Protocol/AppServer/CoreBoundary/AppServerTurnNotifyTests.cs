using DotCraft.Protocol.AppServer;
using Xunit;
using Domain = DotCraft.Sessions;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerTurnNotifyTests
{
    [Theory]
    [InlineData("completed", true)]
    [InlineData("failed", false)]
    [InlineData("stopped", true)]
    [InlineData("waitingForInput", false)]
    public async Task Notify_PassesReportToTheThreadAndReturnsWhetherItWasQueued(string status, bool queued)
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        Domain.ThreadTaskReport? delivered = null;
        harness.Service.ThreadNotificationHandler = (target, report) =>
        {
            Assert.Equal(thread.Id, target);
            delivered = report;
            return queued;
        };

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethodNames.TurnNotify, new
        {
            threadId = thread.Id,
            notificationId = "notice-1",
            label = "Report",
            refId = "thread_child",
            status,
            summary = "The chat ended.",
            project = "Docs",
            result = "The final response."
        }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.Equal(queued, response.RootElement.GetProperty("result").GetProperty("queued").GetBoolean());
        Assert.Equal(new Domain.ThreadTaskReport(
            "notice-1", "thread_child", "Report", status, "The chat ended.", "Docs", "The final response."), delivered);
    }

    [Theory]
    [InlineData("status", "running", 0)]
    [InlineData("status", "WaitingForInput", 0)]
    [InlineData("status", 0, 0)]
    [InlineData("notificationId", "", 0)]
    [InlineData("summary", null, 0)]
    [InlineData("result", null, 2001)]
    public async Task Notify_RejectsInvalidParameters(string field, object? invalid, int resultLength)
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var parameters = new Dictionary<string, object?>
        {
            ["threadId"] = thread.Id,
            ["notificationId"] = "notice-1",
            ["label"] = "Report",
            ["refId"] = "thread_child",
            ["status"] = "completed",
            ["summary"] = "The chat ended."
        };
        if (resultLength > 0)
            parameters[field] = new string('x', resultLength);
        else if (invalid == null)
            parameters.Remove(field);
        else
            parameters[field] = invalid;

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethodNames.TurnNotify, parameters));
        using var response = await harness.Transport.ReadNextSentAsync();
        Assert.Equal(-32602, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Notify_RejectsUnknownThread()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethodNames.TurnNotify, new
        {
            threadId = "thread_missing",
            notificationId = "notice-1",
            label = "Report",
            refId = "thread_child",
            status = "completed",
            summary = "The chat ended."
        }));

        using var response = await harness.Transport.ReadNextSentAsync();
        Assert.Equal(-32010, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }
}
