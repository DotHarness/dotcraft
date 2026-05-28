using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerGoalTests : IDisposable
{
    private readonly AppServerTestHarness _h = new();

    public AppServerGoalTests()
    {
        _h.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task ThreadGoalGet_ReturnsNull_WhenNoGoalExists()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        await _h.ExecuteRequestAsync(_h.BuildRequest(
            AppServerMethods.ThreadGoalGet,
            new { threadId = thread.Id }));

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        Assert.True(doc.RootElement.GetProperty("result").GetProperty("goal").ValueKind == System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task ThreadGoalSet_CreatesGoal_AndEmitsUpdatedNotification()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        await _h.ExecuteRequestAsync(_h.BuildRequest(
            AppServerMethods.ThreadGoalSet,
            new { threadId = thread.Id, objective = "Finish AppServer goal M1", tokenBudget = 9000 }));

        var response = await _h.Transport.ReadNextSentAsync();
        var notification = await _h.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(response);
        var goal = response.RootElement.GetProperty("result").GetProperty("goal");
        Assert.Equal(thread.Id, goal.GetProperty("threadId").GetString());
        Assert.Equal("Finish AppServer goal M1", goal.GetProperty("objective").GetString());
        Assert.Equal("active", goal.GetProperty("status").GetString());
        Assert.Equal(9000, goal.GetProperty("tokenBudget").GetInt64());

        AppServerTestHarness.AssertIsNotification(notification, AppServerMethods.ThreadGoalUpdated);
        Assert.Equal(thread.Id, notification.RootElement.GetProperty("params").GetProperty("threadId").GetString());
    }

    [Fact]
    public async Task ThreadGoalClear_DeletesGoal_AndEmitsClearedNotification()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        await _h.Service.SetThreadGoalAsync(thread.Id, new ThreadGoalUpdate { Objective = "Temporary goal" });

        await _h.ExecuteRequestAsync(_h.BuildRequest(
            AppServerMethods.ThreadGoalClear,
            new { threadId = thread.Id }));

        var response = await _h.Transport.ReadNextSentAsync();
        var notification = await _h.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.True(response.RootElement.GetProperty("result").GetProperty("cleared").GetBoolean());
        AppServerTestHarness.AssertIsNotification(notification, AppServerMethods.ThreadGoalCleared);
        Assert.Equal(thread.Id, notification.RootElement.GetProperty("params").GetProperty("threadId").GetString());
        Assert.Null(await _h.Service.GetThreadGoalAsync(thread.Id));
    }
}
