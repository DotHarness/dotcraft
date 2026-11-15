using DotCraft.Configuration;
using DotCraft.Tools.BackgroundTerminals;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerBackgroundTerminalTests : IAsyncLifetime
{
    private readonly string _tempDir = Path.Combine(
        Directory.GetCurrentDirectory(),
        "TestArtifacts",
        "AppServerBackgroundTerminals_" + Guid.NewGuid().ToString("N"));

    private BackgroundTerminalService? _terminals;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_tempDir);
        _terminals = new BackgroundTerminalService(_tempDir, new AppConfig.ShellBackgroundConfig());
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_terminals != null)
            await _terminals.DisposeAsync();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task Initialize_WhenTerminalServiceProvided_AdvertisesCapability()
    {
        using var harness = new AppServerTestHarness(backgroundTerminalService: Terminals);

        var init = await harness.InitializeAsync();

        var caps = init.RootElement.GetProperty("result").GetProperty("capabilities");
        Assert.True(caps.GetProperty("backgroundTerminals").GetBoolean());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalList_ReturnsBackgroundStateWithoutThreadSubscription(bool background)
    {
        using var harness = new AppServerTestHarness(backgroundTerminalService: Terminals);
        await harness.InitializeAsync();
        var started = await Terminals.StartAsync(new BackgroundTerminalStartRequest
        {
            ThreadId = "thread_wire",
            Command = background
                ? OperatingSystem.IsWindows() ? "Start-Sleep -Seconds 30" : "sleep 30"
                : OperatingSystem.IsWindows() ? "Write-Output 'wire'" : "echo 'wire'",
            WorkingDirectory = _tempDir,
            RunInBackground = background,
            YieldTimeMs = background ? 100 : 5000,
            TimeoutSeconds = 60
        });
        Assert.False(harness.Connection.HasSubscription("thread_wire"));
        var request = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TerminalList,
            new { threadId = "thread_wire" });
        await harness.ExecuteRequestAsync(request);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var terminal = Assert.Single(response.RootElement.GetProperty("result").GetProperty("terminals").EnumerateArray());
        Assert.Equal(started.SessionId, terminal.GetProperty("sessionId").GetString());
        Assert.Equal(background, terminal.GetProperty("isBackground").GetBoolean());
    }

    [Fact]
    public async Task TerminalStop_CompletedSession_ReturnsOriginalFinalSnapshotOnEveryStop()
    {
        using var harness = new AppServerTestHarness(backgroundTerminalService: Terminals);
        using var init = await harness.InitializeAsync();
        var completed = await Terminals.StartAsync(new BackgroundTerminalStartRequest
        {
            ThreadId = "thread_stop_completed",
            Command = OperatingSystem.IsWindows() ? "Write-Output 'finished'; exit 0" : "echo 'finished'; exit 0",
            WorkingDirectory = _tempDir,
            TimeoutSeconds = 10
        });
        Assert.Equal(BackgroundTerminalStatus.Completed, completed.Status);
        Assert.Equal(0, completed.ExitCode);
        Assert.NotNull(completed.CompletedAt);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var request = harness.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.TerminalStop,
                new { sessionId = completed.SessionId });
            await harness.ExecuteRequestAsync(request);

            using var response = await harness.Transport.ReadNextSentAsync();
            AppServerTestHarness.AssertIsSuccessResponse(response);
            var terminal = response.RootElement.GetProperty("result").GetProperty("terminal");
            Assert.Equal(completed.SessionId, terminal.GetProperty("sessionId").GetString());
            Assert.Equal(BackgroundTerminalStatus.Completed, terminal.GetProperty("status").GetString());
            Assert.Equal(0, terminal.GetProperty("exitCode").GetInt32());
            Assert.Equal(completed.CompletedAt, terminal.GetProperty("completedAt").GetDateTimeOffset());
            Assert.Equal(completed.Output, terminal.GetProperty("output").GetString());
        }
    }

    private BackgroundTerminalService Terminals => _terminals ?? throw new InvalidOperationException("Not initialized.");
}
