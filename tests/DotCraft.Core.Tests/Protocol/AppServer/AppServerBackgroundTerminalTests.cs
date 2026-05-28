using DotCraft.Configuration;
using DotCraft.Protocol.AppServer;
using DotCraft.Tools.BackgroundTerminals;

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

    [Fact]
    public async Task TerminalList_ReturnsSessions()
    {
        using var harness = new AppServerTestHarness(backgroundTerminalService: Terminals);
        await harness.InitializeAsync();
        var started = await Terminals.StartAsync(new BackgroundTerminalStartRequest
        {
            ThreadId = "thread_wire",
            Command = OperatingSystem.IsWindows() ? "Write-Output 'wire'" : "echo 'wire'",
            WorkingDirectory = _tempDir,
            TimeoutSeconds = 5
        });

        var msg = harness.BuildRequest(AppServerMethods.TerminalList, new { threadId = "thread_wire" });
        await harness.ExecuteRequestAsync(msg);

        var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var terminals = response.RootElement.GetProperty("result").GetProperty("terminals");
        Assert.Contains(terminals.EnumerateArray(), t => t.GetProperty("sessionId").GetString() == started.SessionId);
    }

    private BackgroundTerminalService Terminals => _terminals ?? throw new InvalidOperationException("Not initialized.");
}
