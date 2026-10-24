using System.Text.Json;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Core.Tests.Tools;

public sealed class UserCoordinationToolsTests
{
    [Fact]
    public async Task SendUserMessageAsync_ValidatesAndDispatches()
    {
        var messages = new List<string>();
        var invocation = Invocation(new UserCoordinationRuntimeContext(
            (message, _) =>
            {
                messages.Add(message);
                return Task.CompletedTask;
            },
            (_, _) => Task.FromResult(new UserCoordinationSleepResult(1, "completed"))));
        var tools = new UserCoordinationTools();

        Assert.Contains("error", await tools.SendUserMessageAsync("  ", invocation));
        var result = await tools.SendUserMessageAsync(" Ask now ", invocation);

        Assert.Equal(["Ask now"], messages);
        Assert.True(JsonDocument.Parse(result).RootElement.GetProperty("accepted").GetBoolean());
    }

    [Fact]
    public async Task ChildThread_CannotMessageTheUserButCanSleep()
    {
        var sent = false;
        var invocation = Invocation(new UserCoordinationRuntimeContext(
            (_, _) =>
            {
                sent = true;
                return Task.CompletedTask;
            },
            (_, _) => Task.FromResult(new UserCoordinationSleepResult(1, "completed"))), topLevel: false);
        var tools = new UserCoordinationTools();

        var message = JsonDocument.Parse(await tools.SendUserMessageAsync("Ask now", invocation)).RootElement;
        var sleep = JsonDocument.Parse(await tools.Sleep(1, invocation)).RootElement;

        Assert.True(message.TryGetProperty("error", out _));
        Assert.False(sent);
        Assert.Equal("completed", sleep.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Sleep_ValidatesBoundsAndReturnsRuntimeResult()
    {
        var capturedDuration = 0;
        var invocation = Invocation(new UserCoordinationRuntimeContext(
            (_, _) => Task.CompletedTask,
            (duration, _) =>
            {
                capturedDuration = duration;
                return Task.FromResult(new UserCoordinationSleepResult(3, "interrupted"));
            }));
        var tools = new UserCoordinationTools();

        Assert.Contains("error", await tools.Sleep(0, invocation));
        Assert.Contains("error", await tools.Sleep(43_200_001, invocation));
        var result = JsonDocument.Parse(await tools.Sleep(10, invocation)).RootElement;

        Assert.Equal(10, capturedDuration);
        Assert.Equal("interrupted", result.GetProperty("status").GetString());
        Assert.Equal(3, result.GetProperty("actualDurationMs").GetInt64());
    }

    private static ToolInvocationContext Invocation(UserCoordinationRuntimeContext coordination, bool topLevel = true) =>
        TurnToolInvocation.For(new TurnToolServices { IsTopLevelThread = topLevel, UserCoordination = coordination });
}
