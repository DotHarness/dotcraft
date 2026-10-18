using Xunit;

namespace DotCraft.Tests.CLI;

public sealed class ExecInterruptProcessTests : ExecProcessTestBase
{
    [Fact]
    public async Task Interrupt_ThreadId_InterruptsTheRunningTurn()
    {
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions { ActiveTurnId = "turn_5" });

        var result = await RunExecAsync(server, ["interrupt", "thread_9"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("thread_9", Assert.Single(server.ParamsOf("thread/read"))["threadId"]!.GetValue<string>());
        var interrupt = Assert.Single(server.ParamsOf("turn/interrupt"));
        Assert.Equal("thread_9", interrupt["threadId"]!.GetValue<string>());
        Assert.Equal("turn_5", interrupt["turnId"]!.GetValue<string>());
        Assert.Equal(string.Empty, result.StandardOutput);
    }

    [Fact]
    public async Task Interrupt_NoThreadId_InterruptsTheOnlyRunningExecTask()
    {
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions
        {
            ListedThreadIds = ["thread_6", "thread_7"],
            RunningThreadIds = ["thread_7"],
            ActiveTurnId = "turn_3"
        });

        var result = await RunExecAsync(server, ["interrupt"]);

        Assert.Equal(0, result.ExitCode);
        var list = Assert.Single(server.ParamsOf("thread/list"));
        Assert.Equal("workspace", list["scope"]!.GetValue<string>());
        Assert.Equal("cli", list["channelName"]!.GetValue<string>());
        var interrupt = Assert.Single(server.ParamsOf("turn/interrupt"));
        Assert.Equal("thread_7", interrupt["threadId"]!.GetValue<string>());
        Assert.Equal("turn_3", interrupt["turnId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Interrupt_NoThreadIdWithSeveralRunning_ListsThemAndExitsOne()
    {
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions
        {
            ListedThreadIds = ["thread_6", "thread_7"],
            RunningThreadIds = ["thread_6", "thread_7"],
            ActiveTurnId = "turn_3"
        });

        var result = await RunExecAsync(server, ["interrupt"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("thread_6", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("thread_7", result.StandardError, StringComparison.Ordinal);
        Assert.Empty(server.ParamsOf("turn/interrupt"));
    }

    [Fact]
    public async Task Interrupt_NoThreadIdWithNothingRunning_ExitsZero()
    {
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions { ListedThreadIds = ["thread_6"] });

        var result = await RunExecAsync(server, ["interrupt"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(server.ParamsOf("turn/interrupt"));
    }

    [Fact]
    public async Task Interrupt_NothingRunning_ExitsZeroWithoutInterrupting()
    {
        await using var server = await ExecFakeAppServer.StartAsync();

        var result = await RunExecAsync(server, ["interrupt", "thread_9"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(server.ParamsOf("turn/interrupt"));
    }

    [Fact]
    public async Task Interrupt_UnknownThread_ExitsOne()
    {
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions { KnownThreadIds = [] });

        var result = await RunExecAsync(server, ["interrupt", "thread_missing"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("thread_missing", result.StandardError, StringComparison.Ordinal);
        Assert.Empty(server.ParamsOf("turn/interrupt"));
    }

    [Fact]
    public async Task Interrupt_OutsideWorkspace_ExitsOneWithoutContactingServer()
    {
        await using var server = await ExecFakeAppServer.StartAsync();

        var result = await RunExecAsync(server, ["interrupt"], workingDirectory: CreateDirectory("elsewhere"));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(server.Requests);
    }
}
