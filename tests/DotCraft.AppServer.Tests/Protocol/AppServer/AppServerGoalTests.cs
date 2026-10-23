using System.Text.Json;
using DotCraft.AppServer;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerGoalTests
{
    [Fact]
    public async Task ThreadGoal_GetSetAndClear_ReportStateAndEmitNotifications()
    {
        using var h = new AppServerTestHarness();
        await h.InitializeAsync();
        var thread = await h.Service.CreateThreadAsync(h.Identity);

        await h.ExecuteRequestAsync(h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadGoalGet, new { threadId = thread.Id }));
        using (var empty = await h.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(empty);
            Assert.Equal(JsonValueKind.Null, empty.RootElement.GetProperty("result").GetProperty("goal").ValueKind);
        }

        await h.ExecuteRequestAsync(h.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadGoalSet,
            new { threadId = thread.Id, objective = "Legacy mode", mode = "replaceExisting" }));
        using (var legacy = await h.Transport.ReadNextSentAsync())
            AppServerTestHarness.AssertIsErrorResponse(legacy, AppServerErrors.InvalidParamsCode);

        await h.ExecuteRequestAsync(h.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadGoalSet,
            new { threadId = thread.Id, objective = "Finish AppServer goal M1", tokenBudget = 9000 }));
        using (var response = await h.Transport.ReadNextSentAsync())
        using (var notification = await h.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(response);
            var goal = response.RootElement.GetProperty("result").GetProperty("goal");
            Assert.Equal(thread.Id, goal.GetProperty("threadId").GetString());
            Assert.Equal("Finish AppServer goal M1", goal.GetProperty("objective").GetString());
            Assert.Equal("active", goal.GetProperty("status").GetString());
            Assert.Equal(9000, goal.GetProperty("tokenBudget").GetInt64());
            Assert.False(goal.TryGetProperty("goalId", out _));
            Assert.Equal(0, goal.GetProperty("tokensUsed").GetInt64());
            Assert.Equal(JsonValueKind.Number, goal.GetProperty("createdAt").ValueKind);
            Assert.Equal(JsonValueKind.Number, goal.GetProperty("updatedAt").ValueKind);

            AppServerTestHarness.AssertIsNotification(notification, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadGoalUpdated);
            Assert.Equal(thread.Id, notification.RootElement.GetProperty("params").GetProperty("threadId").GetString());
            Assert.False(notification.RootElement.GetProperty("params").GetProperty("goal").TryGetProperty("goalId", out _));
        }

        await h.ExecuteRequestAsync(h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadGoalClear, new { threadId = thread.Id }));
        using (var response = await h.Transport.ReadNextSentAsync())
        using (var notification = await h.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(response);
            Assert.True(response.RootElement.GetProperty("result").GetProperty("cleared").GetBoolean());
            AppServerTestHarness.AssertIsNotification(notification, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadGoalCleared);
            Assert.Equal(thread.Id, notification.RootElement.GetProperty("params").GetProperty("threadId").GetString());
        }
        Assert.Null(await h.Service.GetThreadGoalAsync(thread.Id));
    }
}
