using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using DotCraft.Configuration;
using DotCraft.Tools.BackgroundTerminals;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class BackgroundTerminalWaitAndStallTests : IAsyncLifetime
{
    private readonly string _tempDir = Path.Combine(
        Directory.GetCurrentDirectory(),
        "TestArtifacts",
        "DotCraftTerminalWaits_" + Guid.NewGuid().ToString("N"));

    private readonly ConcurrentQueue<BackgroundTerminalEvent> _events = new();
    private BackgroundTerminalService? _service;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_tempDir);
        _service = new BackgroundTerminalService(_tempDir, new AppConfig.ShellBackgroundConfig
        {
            MaxYieldTimeMs = 30000,
            StallWatchdogSeconds = 1
        });
        _service.TerminalEvent += _events.Enqueue;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_service != null)
            await _service.DisposeAsync();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task StartAsync_ReturnsWhenOutputMatchesWaitFor()
    {
        var started = DateTimeOffset.UtcNow;

        var snapshot = await Service.StartAsync(Request(ReadyCommand()) with
        {
            RunInBackground = true,
            YieldTimeMs = 25000,
            WaitFor = Pattern("Listening on \\d+")
        });

        Assert.Equal(BackgroundTerminalStatus.Running, snapshot.Status);
        Assert.Equal("Listening on 3000", snapshot.MatchedLine);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task InteractAsync_ReturnsWhenUnreadOutputMatchesWaitFor()
    {
        var started = await Service.StartAsync(Request(ReadyCommand()) with { RunInBackground = true, YieldTimeMs = 50 });

        var read = await Service.InteractAsync(started.SessionId, string.Empty, 25000, waitFor: Pattern("^Listening"));
        var missed = await Service.InteractAsync(started.SessionId, string.Empty, 300, waitFor: Pattern("never printed"));

        Assert.Equal(BackgroundTerminalStatus.Running, read.Status);
        Assert.Equal("Listening on 3000", read.MatchedLine);
        Assert.Null(missed.MatchedLine);
    }

    [Fact]
    public async Task CheckStalls_ReportsAPromptOnceAndNotifiesTheModel()
    {
        var started = await Service.StartAsync(Request(PromptCommand()) with
        {
            RunInBackground = true,
            YieldTimeMs = 50,
            NotifyOnCompletion = true
        });
        await WaitForQuietAsync();

        Service.CheckStalls();
        Service.CheckStalls();

        var stalled = Assert.Single(_events, evt => evt.EventType == BackgroundTerminalEventTypes.Stalled);
        Assert.Equal(started.SessionId, stalled.Terminal.SessionId);
        var notice = Assert.Single(_events, evt => evt.EventType == BackgroundTerminalEventTypes.StallNotice);
        Assert.True(notice.Terminal.WaitingForInput);
        Assert.Contains("(y/n)", notice.Terminal.Output);
    }

    [Fact]
    public async Task CheckStalls_IgnoresSilenceWithoutAPrompt()
    {
        await Service.StartAsync(Request(SilentCommand()) with
        {
            RunInBackground = true,
            YieldTimeMs = 50,
            NotifyOnCompletion = true
        });
        await WaitForQuietAsync();

        Service.CheckStalls();

        Assert.DoesNotContain(_events, evt => evt.EventType is BackgroundTerminalEventTypes.Stalled or BackgroundTerminalEventTypes.StallNotice);
    }

    [Fact]
    public async Task CheckStalls_EndsAWaitingReadInsteadOfNotifying()
    {
        var started = await Service.StartAsync(Request(PromptCommand()) with
        {
            RunInBackground = true,
            YieldTimeMs = 50,
            NotifyOnCompletion = true
        });
        var read = Service.InteractAsync(started.SessionId, string.Empty, 25000);
        await WaitForQuietAsync();

        Service.CheckStalls();
        var snapshot = await read.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(snapshot.WaitingForInput);
        Assert.Equal(BackgroundTerminalStatus.Running, snapshot.Status);
        Assert.DoesNotContain(_events, evt => evt.EventType == BackgroundTerminalEventTypes.StallNotice);
    }

    private BackgroundTerminalService Service => _service ?? throw new InvalidOperationException("Not initialized.");

    private async Task WaitForQuietAsync()
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (!_events.Any(evt => evt.EventType == "outputDelta") && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(50);
        await Task.Delay(1500);
    }

    private BackgroundTerminalStartRequest Request(string command) => new()
    {
        ThreadId = "thread_waits",
        TurnId = "turn_waits",
        Command = command,
        WorkingDirectory = _tempDir,
        TimeoutSeconds = 60,
        MaxOutputChars = 4000
    };

    private static Regex Pattern(string pattern) =>
        new(pattern, RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string ReadyCommand() =>
        OperatingSystem.IsWindows()
            ? "Write-Output booting; Start-Sleep -Milliseconds 500; Write-Output 'Listening on 3000'; Start-Sleep -Seconds 30"
            : "echo booting; sleep 0.5; echo 'Listening on 3000'; sleep 30";

    private static string PromptCommand() =>
        OperatingSystem.IsWindows()
            ? "Start-Sleep -Milliseconds 500; [Console]::Out.Write('Overwrite existing file? (y/n) '); Start-Sleep -Seconds 30"
            : "sleep 0.5; printf 'Overwrite existing file? (y/n) '; sleep 30";

    private static string SilentCommand() =>
        OperatingSystem.IsWindows() ? "Write-Output started; Start-Sleep -Seconds 30" : "echo started; sleep 30";
}
