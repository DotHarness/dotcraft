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
        using var scope = UserCoordinationRuntimeScope.Set(new UserCoordinationRuntimeContext(
            (message, _) =>
            {
                messages.Add(message);
                return Task.CompletedTask;
            },
            (_, _) => Task.FromResult(new UserCoordinationSleepResult(1, "completed"))));
        var tools = new UserCoordinationTools();

        Assert.Contains("error", await tools.SendUserMessageAsync("  "));
        var result = await tools.SendUserMessageAsync(" Ask now ");

        Assert.Equal(["Ask now"], messages);
        Assert.True(JsonDocument.Parse(result).RootElement.GetProperty("accepted").GetBoolean());
    }

    [Fact]
    public async Task Sleep_ValidatesBoundsAndReturnsRuntimeResult()
    {
        var capturedDuration = 0;
        using var scope = UserCoordinationRuntimeScope.Set(new UserCoordinationRuntimeContext(
            (_, _) => Task.CompletedTask,
            (duration, _) =>
            {
                capturedDuration = duration;
                return Task.FromResult(new UserCoordinationSleepResult(3, "interrupted"));
            }));
        var tools = new UserCoordinationTools();

        Assert.Contains("error", await tools.Sleep(0));
        Assert.Contains("error", await tools.Sleep(43_200_001));
        var result = JsonDocument.Parse(await tools.Sleep(10)).RootElement;

        Assert.Equal(10, capturedDuration);
        Assert.Equal("interrupted", result.GetProperty("status").GetString());
        Assert.Equal(3, result.GetProperty("actualDurationMs").GetInt64());
    }

    [Fact]
    public void CurrentTime_ReturnsUtcNow()
    {
        var before = DateTimeOffset.UtcNow;
        var result = JsonDocument.Parse(new UserCoordinationTools().CurrentTime()).RootElement;
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(result.GetProperty("utc").GetDateTimeOffset(), before, after);
    }
}
