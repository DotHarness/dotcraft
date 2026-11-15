using System.Collections.Concurrent;
using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Tools.BackgroundTerminals;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class BackgroundTerminalHandoffTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DotCraftHandoff_" + Guid.NewGuid().ToString("N"));
    private readonly ConcurrentQueue<BackgroundTerminalEvent> _events = new();
    private BackgroundTerminalService _service = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _service = new BackgroundTerminalService(_root, new AppConfig.ShellBackgroundConfig { MaxYieldTimeMs = 5000 });
        _service.TerminalEvent += _events.Enqueue;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _service.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Theory]
    [InlineData("explicit")]
    [InlineData("yield")]
    public async Task Handoff_PublishesOnceAfterPersistence_AndSurvivesFinalAndRestoredSnapshots(string mode)
    {
        var started = new TaskCompletionSource<BackgroundTerminalSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? publishedMetadata = null;
        _service.TerminalEvent += evt =>
        {
            if (evt.EventType == "started") started.TrySetResult(evt.Terminal);
            if (evt.EventType == BackgroundTerminalEventTypes.Backgrounded)
                publishedMetadata = File.ReadAllText(Path.ChangeExtension(evt.Terminal.OutputPath, ".json"));
        };
        var work = _service.StartAsync(Request(HeldCommand()) with
        {
            RunInBackground = mode == "explicit",
            YieldTimeMs = 100
        });
        var initial = await started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.False(initial.IsBackground);
        var handed = await work.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(BackgroundTerminalStatus.Running, handed.Status);
        Assert.True(handed.IsBackground);
        Assert.False(handed.CompletionNoticeArmed);
        Assert.Equal(mode == "explicit" ? "runInBackground" : null, handed.BackgroundReason);
        var backgrounded = Assert.Single(_events, evt => evt.EventType == BackgroundTerminalEventTypes.Backgrounded);
        Assert.Equal(handed.SessionId, backgrounded.Terminal.SessionId);
        Assert.True(backgrounded.Terminal.IsBackground);
        using var metadata = JsonDocument.Parse(Assert.IsType<string>(publishedMetadata));
        Assert.True(metadata.RootElement.GetProperty("isBackground").GetBoolean());
        Assert.True((await _service.ReadAsync(handed.SessionId)).IsBackground);
        Assert.True(Assert.Single(await _service.ListAsync("thread-handoff")).IsBackground);
        Assert.True((await _service.InteractAsync(handed.SessionId, string.Empty, 0)).IsBackground);
        Assert.Single(_events, evt => evt.EventType == BackgroundTerminalEventTypes.Backgrounded);

        var ended = await _service.StopAsync(handed.SessionId);
        Assert.True(ended.IsBackground);
        Assert.Equal(BackgroundTerminalStatus.Killed, ended.Status);
        Assert.True(Assert.Single(_events, evt => evt.EventType == BackgroundTerminalEventTypes.Completed).Terminal.IsBackground);
        await using var restored = new BackgroundTerminalService(_root, new AppConfig.ShellBackgroundConfig());
        Assert.True((await restored.ReadAsync(handed.SessionId)).IsBackground);
        Assert.True(Assert.Single(await restored.ListAsync("thread-handoff")).IsBackground);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommandExitingBeforeHandoff_RemainsForeground(bool requestedBackground)
    {
        var ended = await _service.StartAsync(Request(OperatingSystem.IsWindows() ? "Write-Output done" : "echo done") with
        {
            RunInBackground = requestedBackground,
            YieldTimeMs = 5000
        });

        Assert.Equal(BackgroundTerminalStatus.Completed, ended.Status);
        Assert.False(ended.IsBackground);
        Assert.False((await _service.ReadAsync(ended.SessionId)).IsBackground);
        Assert.False(Assert.Single(await _service.ListAsync("thread-handoff")).IsBackground);
        Assert.DoesNotContain(_events, evt => evt.EventType == BackgroundTerminalEventTypes.Backgrounded);
    }

    [Fact]
    public async Task ReadsDoNotHandoff_AndCompletionReservationWinsOverConcurrentHandoff()
    {
        using var handover = new CancellationTokenSource();
        var started = new TaskCompletionSource<BackgroundTerminalSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        _service.TerminalEvent += evt =>
        {
            if (evt.EventType == "started") started.TrySetResult(evt.Terminal);
        };
        var work = _service.StartAsync(Request(HeldCommand()) with { YieldTimeMs = 5000, HandOverToken = handover.Token });
        var initial = await started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.False((await _service.ReadAsync(initial.SessionId)).IsBackground);
        Assert.False(Assert.Single(await _service.ListAsync("thread-handoff")).IsBackground);

        var stopping = _service.StopAsync(initial.SessionId);
        handover.Cancel();
        var returned = await work.WaitAsync(TimeSpan.FromSeconds(15));
        await stopping;

        Assert.Equal(BackgroundTerminalStatus.Killed, returned.Status);
        Assert.False(returned.IsBackground);
        Assert.DoesNotContain(_events, evt => evt.EventType == BackgroundTerminalEventTypes.Backgrounded);
        Assert.False((await _service.ReadAsync(initial.SessionId)).IsBackground);
    }

    [Fact]
    public async Task FailedHandoffPersistence_LeavesForegroundStateRetryable()
    {
        string? metadataPath = null;
        _service.TerminalEvent += evt =>
        {
            if (evt.EventType != "started") return;
            metadataPath = Path.ChangeExtension(evt.Terminal.OutputPath, ".json");
            File.Move(metadataPath, metadataPath + ".saved");
            Directory.CreateDirectory(metadataPath);
        };
        var failure = await Record.ExceptionAsync(() => _service.StartAsync(Request(HeldCommand()) with { YieldTimeMs = 100 }));
        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
        var initial = Assert.Single(_events, evt => evt.EventType == "started").Terminal;
        Assert.False((await _service.ReadAsync(initial.SessionId)).IsBackground);
        Assert.False(Assert.Single(await _service.ListAsync("thread-handoff")).IsBackground);
        Assert.DoesNotContain(_events, evt => evt.EventType == BackgroundTerminalEventTypes.Backgrounded);

        Directory.Delete(Assert.IsType<string>(metadataPath));
        var recovered = await _service.InteractAsync(initial.SessionId, string.Empty, 0);

        Assert.True(recovered.IsBackground);
        var backgrounded = Assert.Single(_events, evt => evt.EventType == BackgroundTerminalEventTypes.Backgrounded);
        Assert.True(backgrounded.Terminal.IsBackground);
        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath!));
        Assert.True(persisted.RootElement.GetProperty("isBackground").GetBoolean());
    }

    [Fact]
    public async Task CompletionCannotOvertakeHandoffPublicationToLaterSubscribers()
    {
        var publishing = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var observed = new ConcurrentQueue<string>();
        _service.TerminalEvent += evt =>
        {
            if (evt.EventType != BackgroundTerminalEventTypes.Backgrounded) return;
            publishing.TrySetResult(evt.Terminal.SessionId);
            release.Wait(TimeSpan.FromSeconds(15));
        };
        _service.TerminalEvent += evt =>
        {
            if (evt.EventType is BackgroundTerminalEventTypes.Backgrounded or BackgroundTerminalEventTypes.Completed)
                observed.Enqueue(evt.EventType);
        };
        var work = _service.StartAsync(Request(HeldCommand()) with { YieldTimeMs = 100 });
        var sessionId = await publishing.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var stopping = _service.StopAsync(sessionId);
        try
        {
            await Task.WhenAny(stopping, Task.Delay(200));
            Assert.Empty(observed);
        }
        finally
        {
            release.Set();
        }
        await stopping.WaitAsync(TimeSpan.FromSeconds(15));
        await work.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal([BackgroundTerminalEventTypes.Backgrounded, BackgroundTerminalEventTypes.Completed], observed);
    }

    private BackgroundTerminalStartRequest Request(string command) => new()
    {
        ThreadId = "thread-handoff",
        TurnId = "turn-handoff",
        CallId = "call-handoff",
        Command = command,
        WorkingDirectory = _root,
        TimeoutSeconds = 30,
        MaxOutputChars = 1000
    };

    private string HeldCommand()
    {
        var release = Path.Combine(_root, "release").Replace("'", "''", StringComparison.Ordinal);
        return OperatingSystem.IsWindows()
            ? $"Write-Output ready; while (-not (Test-Path -LiteralPath '{release}')) {{ Start-Sleep -Milliseconds 20 }}"
            : $"echo ready; while [ ! -f '{release}' ]; do sleep 0.02; done";
    }
}
