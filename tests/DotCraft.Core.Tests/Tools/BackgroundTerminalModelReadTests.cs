using System.Collections.Concurrent;
using DotCraft.Configuration;
using DotCraft.Tools.BackgroundTerminals;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class BackgroundTerminalModelReadTests : IAsyncLifetime
{
    private readonly string _tempDir = Path.Combine(
        Directory.GetCurrentDirectory(),
        "TestArtifacts",
        "DotCraftTerminalModelReads_" + Guid.NewGuid().ToString("N"));

    private readonly ConcurrentQueue<BackgroundTerminalEvent> _events = new();
    private BackgroundTerminalService? _service;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_tempDir);
        _service = new BackgroundTerminalService(_tempDir, new AppConfig.ShellBackgroundConfig { MaxYieldTimeMs = 10000 });
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
    public async Task InteractAsync_ReturnsOnlyOutputTheModelHasNotRead()
    {
        var started = await Service.StartAsync(Request(TwoPartCommand("first-part", "second-part")) with
        {
            RunInBackground = true,
            YieldTimeMs = 200
        });

        var finished = await Service.InteractAsync(started.SessionId, string.Empty, 10000);
        var again = await Service.InteractAsync(started.SessionId, string.Empty, 1000);

        Assert.Equal(BackgroundTerminalStatus.Completed, finished.Status);
        var combined = started.Output + "\n" + finished.Output;
        Assert.Equal(1, Occurrences(combined, "first-part"));
        Assert.Equal(1, Occurrences(combined, "second-part"));
        Assert.Equal(string.Empty, again.Output);
    }

    [Fact]
    public async Task CompletionNotice_IsRaisedOnceAndLeavesItsOutputUnread()
    {
        var completed = WaitForEvent("completed");
        var started = await Service.StartAsync(Request(TwoPartCommand("early", "late-tail")) with
        {
            RunInBackground = true,
            YieldTimeMs = 200,
            NotifyOnCompletion = true
        });
        await completed.WaitAsync(TimeSpan.FromSeconds(15));
        await Task.Delay(200);

        Assert.Equal(BackgroundTerminalStatus.Running, started.Status);
        Assert.True(started.CompletionNoticeArmed);
        var notice = Assert.Single(Notices());
        Assert.Equal(started.SessionId, notice.Terminal.SessionId);
        Assert.Equal(0, notice.Terminal.ExitCode);
        Assert.Contains("late-tail", notice.Terminal.Output);
        var read = await Service.InteractAsync(started.SessionId, string.Empty, 1000);
        Assert.Contains("late-tail", read.Output);
    }

    [Fact]
    public async Task CompletionNotice_IsNotRaisedWhenTheModelSawTheFinalStatus()
    {
        var foreground = await Service.StartAsync(Request(EchoCommand("quick")) with { NotifyOnCompletion = true });
        var background = await Service.StartAsync(Request(TwoPartCommand("a", "b")) with
        {
            RunInBackground = true,
            YieldTimeMs = 200,
            NotifyOnCompletion = true
        });
        var read = await Service.InteractAsync(background.SessionId, string.Empty, 10000);
        await Task.Delay(300);

        Assert.Equal(BackgroundTerminalStatus.Completed, foreground.Status);
        Assert.Equal(BackgroundTerminalStatus.Completed, read.Status);
        Assert.Empty(Notices());
    }

    [Fact]
    public async Task EndOfTextInput_IsOrdinaryInputThatLeavesTheTerminalRunning()
    {
        var started = await Service.StartAsync(Request(LongCommand()) with
        {
            RunInBackground = true,
            YieldTimeMs = 100,
            Interactive = true,
            NotifyOnCompletion = true
        });

        var written = await Service.InteractAsync(started.SessionId, ((char)3).ToString(), 300);
        await Service.StopAsync(started.SessionId);
        await Task.Delay(300);

        Assert.Equal(BackgroundTerminalStatus.Running, written.Status);
        var notice = Assert.Single(Notices());
        Assert.Equal(started.SessionId, notice.Terminal.SessionId);
        Assert.Equal(BackgroundTerminalStatus.Killed, notice.Terminal.Status);
    }

    [Fact]
    public async Task StartAsync_HandsOverAWaitingForegroundCommandWhenSignalled()
    {
        using var handOver = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var started = DateTimeOffset.UtcNow;

        var snapshot = await Service.StartAsync(Request(LongCommand()) with
        {
            TimeoutSeconds = 60,
            HandOverToken = handOver.Token
        });

        Assert.Equal(BackgroundTerminalStatus.Running, snapshot.Status);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(20));
        await Service.StopAsync(snapshot.SessionId);
    }

    private BackgroundTerminalService Service => _service ?? throw new InvalidOperationException("Not initialized.");

    private IEnumerable<BackgroundTerminalEvent> Notices() =>
        _events.Where(evt => evt.EventType == BackgroundTerminalEventTypes.CompletionNotice);

    private Task WaitForEvent(string eventType)
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Service.TerminalEvent += evt =>
        {
            if (evt.EventType == eventType)
                source.TrySetResult();
        };
        return source.Task;
    }

    private BackgroundTerminalStartRequest Request(string command) => new()
    {
        ThreadId = "thread_model_reads",
        TurnId = "turn_model_reads",
        Command = command,
        WorkingDirectory = _tempDir,
        TimeoutSeconds = 30,
        MaxOutputChars = 4000
    };

    private static int Occurrences(string text, string value) =>
        (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;

    private static string EchoCommand(string text) =>
        OperatingSystem.IsWindows() ? $"Write-Output '{text}'" : $"echo '{text}'";

    private static string TwoPartCommand(string first, string second) =>
        OperatingSystem.IsWindows()
            ? $"Write-Output '{first}'; Start-Sleep -Milliseconds 1200; Write-Output '{second}'"
            : $"echo '{first}'; sleep 1.2; echo '{second}'";

    private static string LongCommand() =>
        OperatingSystem.IsWindows() ? "Write-Output waiting; Start-Sleep -Seconds 30" : "echo waiting; sleep 30";
}
