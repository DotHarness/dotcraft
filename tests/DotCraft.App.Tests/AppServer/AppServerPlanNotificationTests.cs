using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Memory;
using DotCraft.Protocol.AppServer;
using PlanTodo = DotCraft.Memory.PlanTodo;
using Xunit;

namespace DotCraft.App.Tests.AppServer;

public sealed class AppServerPlanNotificationTests
{
    [Fact]
    public void BuildPlanUpdatedNotification_IncludesThreadIdAndCompleteSnapshot()
    {
        var notification = AppServerHost.BuildPlanUpdatedNotification(
            "thread-plan-1",
            new StructuredPlan
            {
                Title = "Thread Plan",
                Overview = "Scoped to one thread",
                Content = "# Thread Plan\n\nBody",
                Todos =
                [
                    new PlanTodo
                    {
                        Id = "do-work",
                        Content = "Do the work",
                        Priority = PlanTodoPriority.High,
                        Status = PlanTodoStatus.InProgress
                    }
                ]
            });

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(notification));
        var root = doc.RootElement;
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(DotCraft.Protocol.AppServer.AppServerMethodNames.PlanUpdated, root.GetProperty("method").GetString());

        var @params = root.GetProperty("params");
        Assert.Equal("thread-plan-1", @params.GetProperty("threadId").GetString());
        Assert.Equal("Thread Plan", @params.GetProperty("title").GetString());
        Assert.Equal("Scoped to one thread", @params.GetProperty("overview").GetString());
        Assert.Equal("# Thread Plan\n\nBody", @params.GetProperty("content").GetString());

        var todo = Assert.Single(@params.GetProperty("todos").EnumerateArray());
        Assert.Equal("do-work", todo.GetProperty("id").GetString());
        Assert.Equal("high", todo.GetProperty("priority").GetString());
        Assert.Equal("in_progress", todo.GetProperty("status").GetString());
    }
}
