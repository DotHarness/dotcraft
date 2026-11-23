using System.Collections.Concurrent;
using System.Text;
using DotCraft.Configuration;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class BackgroundTerminalOutputTests : IAsyncLifetime
{
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(10);
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
        var content = "initial-head" + string.Concat(Enumerable.Repeat("abc中文😀", 100_000)) + "final-tail";
        var events = new ConcurrentQueue<BackgroundTerminalEvent>();
        _service = CreateService();
        var completion = ObserveCompletion(_service, events.Enqueue);

        var result = await _service.StartAsync(await RequestAsync(content));
        await completion.WaitAsync(CompletionTimeout);

        Assert.Equal(BackgroundTerminalStatus.Completed, result.Status);
        Assert.Equal(content, await File.ReadAllTextAsync(result.OutputPath));
        Assert.Equal(content.Length, result.OriginalOutputChars);
        Assert.True(result.Truncated);
        Assert.StartsWith("initial-head", result.Output);
        Assert.EndsWith("final-tail", result.Output);
        Assert.InRange(Encoding.UTF8.GetByteCount(result.Output), 1, 1024 * 1024 + 100);
        Assert.DoesNotContain("\uFFFD", result.Output);
        Assert.All(events.Where(e => e.EventType == "outputDelta"), e =>
        {
            Assert.InRange(Encoding.UTF8.GetByteCount(e.Delta ?? ""), 0, 8192);
            Assert.DoesNotContain("\uFFFD", e.Delta ?? "");
        });

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
        var completion = ObserveCompletion(_service, events.Enqueue);
        var content = new string('x', 50_000) + "end";

        var result = await _service.StartAsync(await RequestAsync(content));
        var completed = await completion.WaitAsync(CompletionTimeout);

        Assert.Equal(content, await File.ReadAllTextAsync(result.OutputPath));
        var liveBytes = events.Where(e => e.EventType == "outputDelta")
            .Sum(e => Encoding.UTF8.GetByteCount(e.Delta!));
        Assert.InRange(liveBytes, 1, 8192);
        Assert.Equal(content, result.Output);
        Assert.EndsWith("end", completed.Output);
        Assert.Equal(content.Length, completed.OriginalOutputChars);
    }

    [Fact]
    public async Task BlockedLiveConsumer_DoesNotPreventLogDrainAndDropsExcessNotifications()
    {
        _service = CreateService();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new ConcurrentQueue<BackgroundTerminalEvent>();
        var completion = ObserveCompletion(_service, e =>
        {
            events.Enqueue(e);
            if (e.EventType != "outputDelta") return;
            entered.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(30));
        });
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
        var result = await run;
        var completed = await completion.WaitAsync(CompletionTimeout);
        var liveChars = events.Where(e => e.EventType == "outputDelta").Sum(e => e.Delta!.Length);
        Assert.InRange(liveChars, 1, content.Length - 1);
        Assert.Equal(content, await File.ReadAllTextAsync(result.OutputPath));
        Assert.EndsWith("done", completed.Output);
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
    public async Task ForegroundExec_TruncatedOutputKeepsBothEndsAndPointsToCompleteLog()
    {
        _service = CreateService();
        var content = "initial-head" + new string('x', 20_000) + "final-tail";
        var request = await RequestAsync(content);
        var tools = new ShellTools(_directory, _service, requireApprovalOutsideWorkspace: false);

        var result = await tools.Exec(request.Command, maxOutputChars: 1000);

        Assert.True(result.Success);
        Assert.StartsWith("initial-head", result.Content);
        Assert.Contains("middle chars", result.Content);
        Assert.Contains("final-tail", result.Content);
        var terminal = Assert.Single(await _service.ListAsync());
        Assert.Contains(terminal.OutputPath, result.Content);
        Assert.Equal(content, await File.ReadAllTextAsync(terminal.OutputPath));
    }

    [Fact]
    public async Task UnwritableLog_FailsExplicitlyAndPublishesFailedCompletion()
    {
        _service = CreateService();
        FileStream? held = null;
        var completion = ObserveCompletion(_service, e =>
        {
            if (e.EventType == "started")
                held = new FileStream(e.Terminal.OutputPath, FileMode.Open, FileAccess.Read, FileShare.None);
        });
        try
        {
            var request = await RequestAsync("hello");
            await Assert.ThrowsAnyAsync<IOException>(() => _service.StartAsync(request));
            var completed = await completion.WaitAsync(CompletionTimeout);
            Assert.Equal(BackgroundTerminalStatus.Failed, completed.Status);
        }
        finally
        {
            held?.Dispose();
        }
    }

    private static Task<BackgroundTerminalSnapshot> ObserveCompletion(
        BackgroundTerminalService service, Action<BackgroundTerminalEvent> onEvent)
    {
        var completion = new TaskCompletionSource<BackgroundTerminalSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.TerminalEvent += e =>
        {
            onEvent(e);
            if (e.EventType == BackgroundTerminalEventTypes.Completed)
                completion.TrySetResult(e.Terminal);
        };
        return completion.Task;
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
