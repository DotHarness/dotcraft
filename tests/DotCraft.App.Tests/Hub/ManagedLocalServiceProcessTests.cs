using System.Diagnostics;
using DotCraft.Hub;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class ManagedLocalServiceProcessTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task Ready_DoesNotEndOutputConsumption(bool stderr, bool newline)
    {
        var payload = new string('x', 1024);
        var redirect = stderr ? " 1>&2" : string.Empty;
        var windowsWrite = newline ? $"echo {payload}" : $"<nul set /p \"={payload}\"";
        var unixFormat = newline ? "%s\\n" : "%s";
        var windowsTerminator = stderr && !newline ? "echo. 1>&2" : string.Empty;
        var unixTerminator = stderr && !newline ? "printf '\\n' 1>&2" : string.Empty;
        await using var fixture = ManagedLocalServiceProcessFixture.Start(
            $"""
            echo ordinary startup output
            echo {ManagedLocalServiceProcessFixture.ReadyRecord}
            set /p begin=
            for /l %%i in (1,1,8192) do {windowsWrite}{redirect}
            {windowsTerminator}
            echo complete>completed
            set /p stop=
            """,
            $"""
            printf '%s\n' 'ordinary startup output' '{ManagedLocalServiceProcessFixture.ReadyRecord}'
            read begin
            i=0
            while [ "$i" -lt 8192 ]; do
                printf '{unixFormat}' '{payload}'{redirect}
                i=$((i + 1))
            done
            {unixTerminator}
            printf complete > completed
            read stop
            """);
        using var readyCancellation = new CancellationTokenSource(TestTimeout);

        var ready = await fixture.Service.WaitForReadyAsync(readyCancellation.Token);
        Assert.Equal("oratorio", ready.ServiceId);
        readyCancellation.Cancel();
        await fixture.SendAsync("begin");
        await fixture.WaitForMarkerAsync("completed", TestTimeout);

        Assert.True(fixture.Service.IsRunning);
        if (stderr)
        {
            using var diagnosticsDeadline = new CancellationTokenSource(TestTimeout);
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
            while (fixture.Service.RecentStderr is null)
                await timer.WaitForNextTickAsync(diagnosticsDeadline.Token);
            var recent = Assert.IsType<string>(fixture.Service.RecentStderr);
            Assert.Equal(newline ? 20 : 1, recent.Split(Environment.NewLine).Length);
            Assert.True(recent.Length <= 20 * (4096 + Environment.NewLine.Length));
            if (!newline)
                Assert.Equal(4096, recent.Length);
        }
    }

    [Fact]
    public async Task CancelledReadinessWait_DoesNotCancelTheReader()
    {
        await using var fixture = ManagedLocalServiceProcessFixture.Start(
            $"set /p begin=\necho {ManagedLocalServiceProcessFixture.ReadyRecord}\nset /p stop=",
            $"read begin\nprintf '%s\\n' '{ManagedLocalServiceProcessFixture.ReadyRecord}'\nread stop");
        using var cancellation = new CancellationTokenSource();
        var cancelledWait = fixture.Service.WaitForReadyAsync(cancellation.Token);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledWait.WaitAsync(TestTimeout));
        await fixture.SendAsync("begin");
        var ready = await fixture.Service.WaitForReadyAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal("oratorio", ready.ServiceId);
        Assert.True(fixture.Service.IsRunning);
    }

    [Fact]
    public async Task ExitBeforeReadiness_FailsThePendingWait()
    {
        await using var fixture = ManagedLocalServiceProcessFixture.Start(
            "set /p begin=\nexit /b 7", "read begin\nexit 7");
        var ready = fixture.Service.WaitForReadyAsync(CancellationToken.None);

        await fixture.SendAsync("begin");

        await AssertReadinessFailsAsync(ready);
    }

    [Fact]
    public async Task InvalidReadinessFields_FailWithoutWaitingForProcessExit()
    {
        const string invalid = "{\"type\":\"dotcraft.managed-service.ready\",\"serviceId\":\"oratorio\"}";
        await using var fixture = ManagedLocalServiceProcessFixture.Start(
            $"set /p begin=\necho {invalid}\nset /p stop=",
            $"read begin\nprintf '%s\\n' '{invalid}'\nread stop");
        var ready = fixture.Service.WaitForReadyAsync(CancellationToken.None);

        await fixture.SendAsync("begin");

        await AssertReadinessFailsAsync(ready);
        Assert.True(fixture.Service.IsRunning);
    }

    [Fact]
    public async Task Dispose_EndsPendingReadinessAndStopsTheProcess()
    {
        await using var fixture = ManagedLocalServiceProcessFixture.Start("set /p stop=", "read stop");
        using var process = Process.GetProcessById(fixture.Service.ProcessId);
        var ready = fixture.Service.WaitForReadyAsync(CancellationToken.None);

        await fixture.Service.DisposeAsync().AsTask().WaitAsync(TestTimeout);

        await AssertReadinessFailsAsync(ready);
        Assert.True(process.HasExited);
    }

    [Fact]
    public async Task ExitWithInheritedOutputHandles_DoesNotWaitForPipeEof()
    {
        await using var fixture = ManagedLocalServiceProcessFixture.Start(
            "set /p begin=\nstart \"\" /b cmd.exe /d /q /c descendant.cmd\nexit /b 0",
            "read begin\n/bin/sh descendant.sh <&0 &\nprintf '%s' \"$!\" > descendant.pid\nexit 0",
            inheritedOutput: true);
        using var process = Process.GetProcessById(fixture.Service.ProcessId);
        var ready = fixture.Service.WaitForReadyAsync(CancellationToken.None);

        await fixture.SendAsync("begin");
        await fixture.WaitForMarkerAsync("descendant.started", TestTimeout);
        await process.WaitForExitAsync().WaitAsync(TestTimeout);
        fixture.CaptureDescendant();
        Assert.False(fixture.MarkerExists("descendant.exited"));

        await AssertReadinessFailsAsync(ready);
        await fixture.Service.DisposeAsync().AsTask().WaitAsync(TestTimeout);
    }

    private static async Task AssertReadinessFailsAsync(Task<ManagedServiceReady> ready)
    {
        var error = await Record.ExceptionAsync(async () => await ready.WaitAsync(TestTimeout));
        Assert.NotNull(error);
        Assert.IsNotType<TimeoutException>(error);
    }
}
