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
            }));
        var tools = new UserCoordinationTools();

        Assert.Contains("error", await tools.SendUserMessageAsync("  ", invocation));
        var result = await tools.SendUserMessageAsync(" Ask now ", invocation);

        Assert.Equal(["Ask now"], messages);
        Assert.True(JsonDocument.Parse(result).RootElement.GetProperty("accepted").GetBoolean());
    }

    [Fact]
    public async Task ChildThread_CannotMessageTheUser()
    {
        var sent = false;
        var invocation = Invocation(new UserCoordinationRuntimeContext(
            (_, _) =>
            {
                sent = true;
                return Task.CompletedTask;
            }), topLevel: false);
        var tools = new UserCoordinationTools();

        var message = JsonDocument.Parse(await tools.SendUserMessageAsync("Ask now", invocation)).RootElement;

        Assert.True(message.TryGetProperty("error", out _));
        Assert.False(sent);
    }

    private static ToolInvocationContext Invocation(UserCoordinationRuntimeContext coordination, bool topLevel = true) =>
        TurnToolInvocation.For(new TurnToolServices { IsTopLevelThread = topLevel, UserCoordination = coordination });
}
