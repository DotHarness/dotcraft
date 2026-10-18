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
    public async Task Interrupt_Last_InterruptsTheMostRecentThread()
    {
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions
        {
            ListedThreadIds = ["thread_7"],
            ActiveTurnId = "turn_3"
        });

        var result = await RunExecAsync(server, ["interrupt", "--last"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("workspace", Assert.Single(server.ParamsOf("thread/list"))["scope"]!.GetValue<string>());
        var interrupt = Assert.Single(server.ParamsOf("turn/interrupt"));
        Assert.Equal("thread_7", interrupt["threadId"]!.GetValue<string>());
        Assert.Equal("turn_3", interrupt["turnId"]!.GetValue<string>());
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

        var result = await RunExecAsync(server, ["interrupt", "--last"], workingDirectory: CreateDirectory("elsewhere"));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(server.Requests);
    }
}
