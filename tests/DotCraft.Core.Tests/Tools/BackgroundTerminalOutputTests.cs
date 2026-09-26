using System.Collections.Concurrent;
using System.Text;
using DotCraft.Configuration;
using DotCraft.Tools.BackgroundTerminals;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class BackgroundTerminalOutputTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "terminal_output_" + Guid.NewGuid().ToString("N"));
    private BackgroundTerminalService? _service;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_service != null)
            await _service.DisposeAsync();
        Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task LargeUnterminatedUnicodeOutput_HasBoundedFramesAndPreviewAndCompleteLogAfterRestart()
    {
        var content = string.Concat(Enumerable.Repeat("abc中文😀", 230_000)) + "final-tail";
        var events = new ConcurrentQueue<BackgroundTerminalEvent>();
        _service = CreateService();
        _service.TerminalEvent += events.Enqueue;

        var result = await _service.StartAsync(await RequestAsync(content));

        Assert.Equal(BackgroundTerminalStatus.Completed, result.Status);
        Assert.Equal(content, await File.ReadAllTextAsync(result.OutputPath));
        Assert.Equal(content.Length, result.OriginalOutputChars);
        Assert.True(result.Truncated);
        Assert.EndsWith("final-tail", result.Output);
        Assert.InRange(Encoding.UTF8.GetByteCount(result.Output), 1, 1024 * 1024 + 100);
        Assert.DoesNotContain("\uFFFD", result.Output);
        Assert.All(events.Where(e => e.EventType == "outputDelta"), e =>
        {
            Assert.InRange(Encoding.UTF8.GetByteCount(e.Delta ?? ""), 0, 8192);
            Assert.DoesNotContain("\uFFFD", e.Delta ?? "");
        });
        Assert.Equal("completed", events.Last().EventType);

        await _service.DisposeAsync();
        _service = CreateService();
        var recovered = await _service.ReadAsync(result.SessionId, maxOutputChars: 0);
        Assert.Equal(result.Output, recovered.Output);
        Assert.Equal(result.OriginalOutputChars, recovered.OriginalOutputChars);
    }

    [Fact]
    public async Task LiveBudget_StopsDeltasButRetainsCompleteOutputAndCompletion()
    {
        _service = CreateService(maxBytes: 8192);
        var events = new ConcurrentQueue<BackgroundTerminalEvent>();
        _service.TerminalEvent += events.Enqueue;
        var content = new string('x', 200_000) + "end";

        var result = await _service.StartAsync(await RequestAsync(content));

        Assert.Equal(content, await File.ReadAllTextAsync(result.OutputPath));
        var deltas = events.Where(e => e.EventType == "outputDelta").ToArray();
        Assert.InRange(deltas.Sum(e => Encoding.UTF8.GetByteCount(e.Delta ?? "")), 1, 8192);
        Assert.All(deltas, e => Assert.False(string.IsNullOrEmpty(e.Delta)));
        Assert.Equal("completed", events.Last().EventType);
        Assert.Equal(content, result.Output);
        Assert.EndsWith("end", events.Last().Terminal.Output);
        Assert.Equal(content.Length, events.Last().Terminal.OriginalOutputChars);
    }

    [Fact]
    public async Task BlockedLiveConsumer_DoesNotPreventLogDrainAndDropsExcessNotifications()
    {
        _service = CreateService();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new ConcurrentQueue<BackgroundTerminalEvent>();
        _service.TerminalEvent += e =>
        {
            events.Enqueue(e);
            if (e.EventType != "outputDelta") return;
            entered.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(30));
        };
        var content = new string('a', 2 * 1024 * 1024) + "done";
        var run = _service.StartAsync(await RequestAsync(content));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!Directory.EnumerateFiles(_directory, "*.log", SearchOption.AllDirectories)
                       .Any(p => new FileInfo(p).Length == content.Length))
                await Task.Delay(20, timeout.Token);
        }
        finally
        {
            release.Set();
        }
        await run;
        var deltas = events.Where(e => e.EventType == "outputDelta").ToArray();
        Assert.All(deltas, e => Assert.False(string.IsNullOrEmpty(e.Delta)));
        Assert.InRange(deltas.Sum(e => e.Delta!.Length), 1, content.Length - 1);
        Assert.Equal(content, await File.ReadAllTextAsync(result.OutputPath));
        Assert.Equal("completed", events.Last().EventType);
        Assert.EndsWith("done", events.Last().Terminal.Output);
    }

    [Fact]
    public async Task PersistedLog_IsReadWithBoundedPreview()
    {
        _service = CreateService();
        var first = await _service.StartAsync(await RequestAsync("initial"));
        await _service.DisposeAsync();
        await File.WriteAllTextAsync(first.OutputPath, new string('a', 2 * 1024 * 1024) + "tail");
        _service = CreateService();

        var result = await _service.ReadAsync(first.SessionId, maxOutputChars: -1);

        Assert.True(result.Truncated);
        Assert.EndsWith("tail", result.Output);
        Assert.True(result.Output.Length <= 1024 * 1024 + 100);
    }

    [Fact]
    public async Task UnwritableLog_FailsExplicitlyAndPublishesFailedCompletion()
    {
        _service = CreateService();
        FileStream? held = null;
        var events = new ConcurrentQueue<BackgroundTerminalEvent>();
        _service.TerminalEvent += e =>
        {
            events.Enqueue(e);
            if (e.EventType == "started")
                held = new FileStream(e.Terminal.OutputPath, FileMode.Open, FileAccess.Read, FileShare.None);
        };
        try
        {
            var request = await RequestAsync("hello");
            await Assert.ThrowsAnyAsync<IOException>(() => _service.StartAsync(request));
            Assert.Equal(BackgroundTerminalStatus.Failed, events.Last().Terminal.Status);
            Assert.Equal("completed", events.Last().EventType);
        }
        finally
        {
            held?.Dispose();
        }
    }

    private BackgroundTerminalService CreateService(long maxBytes = 64L * 1024 * 1024) =>
        new(_directory, new AppConfig.ShellBackgroundConfig { OutputMaxBytes = maxBytes });

    private async Task<BackgroundTerminalStartRequest> RequestAsync(string content)
    {
        var input = Path.Combine(_directory, "input.txt");
        await File.WriteAllTextAsync(input, content, new UTF8Encoding(false));
        var quoted = "'" + input.Replace("'", "''") + "'";
        return new BackgroundTerminalStartRequest
        {
            ThreadId = "output_test",
            WorkingDirectory = _directory,
            Command = OperatingSystem.IsWindows()
                ? $"[Console]::Out.Write([IO.File]::ReadAllText({quoted}))"
                : $"cat '{input.Replace("'", "'\\''")}'",
            TimeoutSeconds = 30,
            MaxOutputChars = 0
        };
    }
}
