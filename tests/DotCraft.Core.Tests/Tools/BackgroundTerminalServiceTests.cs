using System.Collections.Concurrent;
using DotCraft.Configuration;
using DotCraft.Tools.BackgroundTerminals;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class BackgroundTerminalServiceTests : IAsyncLifetime
{
    private readonly string _tempDir = Path.Combine(
        Directory.GetCurrentDirectory(),
        "TestArtifacts",
        "DotCraftBackgroundTerminals_" + Guid.NewGuid().ToString("N"));

    private BackgroundTerminalService? _service;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_tempDir);
        _service = new BackgroundTerminalService(
            _tempDir,
            new AppConfig.ShellBackgroundConfig
            {
                DefaultYieldTimeMs = 100,
                MaxYieldTimeMs = 5000,
                DefaultReadMaxOutputChars = 4000
            });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_service != null)
            await _service.DisposeAsync();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task StartAsync_ForegroundCommand_ReturnsCompletedOutput()
    {
        var snapshot = await Service.StartAsync(new BackgroundTerminalStartRequest
        {
            ThreadId = "thread_test",
            Command = EchoCommand("hello"),
            WorkingDirectory = _tempDir,
            TimeoutSeconds = 5,
            MaxOutputChars = 1000
        });

        Assert.Equal(BackgroundTerminalStatus.Completed, snapshot.Status);
        Assert.Contains("hello", snapshot.Output);
        Assert.True(File.Exists(snapshot.OutputPath));
    }

    [Fact]
    public async Task StartAsync_ForegroundCommand_OutputDeltaCarriesCorrelationIds()
    {
        var outputDeltaSource = new TaskCompletionSource<BackgroundTerminalEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Service.TerminalEvent += terminalEvent =>
        {
            if (terminalEvent.EventType == "outputDelta"
                && terminalEvent.Delta?.Contains("live", StringComparison.Ordinal) == true)
            {
                outputDeltaSource.TrySetResult(terminalEvent);
            }
        };

        var snapshot = await Service.StartAsync(new BackgroundTerminalStartRequest
        {
            ThreadId = "thread_live",
            TurnId = "turn_live",
            CallId = "call_live",
            Command = EchoCommand("live"),
            WorkingDirectory = _tempDir,
            TimeoutSeconds = 5,
            MaxOutputChars = 1000
        });

        Assert.Equal(BackgroundTerminalStatus.Completed, snapshot.Status);
        var outputDelta = await outputDeltaSource.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("call_live", outputDelta.Terminal.CallId);
        Assert.Equal("thread_live", outputDelta.Terminal.ThreadId);
        Assert.Equal("turn_live", outputDelta.Terminal.TurnId);
        Assert.Contains("live", outputDelta.Delta);
    }

    [Fact]
    public async Task StartAsync_BackgroundCommand_CanBeReadAfterCompletion()
    {
        var started = await Service.StartAsync(new BackgroundTerminalStartRequest
        {
            ThreadId = "thread_test",
            Command = DelayedEchoCommand("done"),
            WorkingDirectory = _tempDir,
            RunInBackground = true,
            YieldTimeMs = 100,
            MaxOutputChars = 1000
        });

        Assert.Equal(BackgroundTerminalStatus.Running, started.Status);
        Assert.False(string.IsNullOrWhiteSpace(started.SessionId));

        var completed = await Service.ReadAsync(started.SessionId, waitMs: 5000, maxOutputChars: 1000);
        Assert.Equal(BackgroundTerminalStatus.Completed, completed.Status);
        Assert.Contains("done", completed.Output);
    }

    [Fact]
    public async Task StartAsync_ConcurrentStdoutAndStderr_AppendsToSameLog()
    {
        var started = await Service.StartAsync(new BackgroundTerminalStartRequest
        {
            ThreadId = "thread_streams",
            Command = ConcurrentStdoutAndStderrCommand(),
            WorkingDirectory = _tempDir,
            RunInBackground = true,
            YieldTimeMs = 100,
            MaxOutputChars = 20_000
        });

        await Service.ReadAsync(started.SessionId, waitMs: 3000, maxOutputChars: 20_000);
        var log = await ReadUntilContainsAsync(started.OutputPath, "out-100", "err-100");
        var completed = await Service.ReadAsync(started.SessionId, maxOutputChars: 20_000);

        Assert.Equal(BackgroundTerminalStatus.Completed, completed.Status);
        Assert.Contains("out-1", completed.Output);
        Assert.Contains("err-1", completed.Output);
        Assert.Contains("out-100", log);
        Assert.Contains("err-100", log);
    }

    [Fact]
    public async Task StartAsync_BurstOutput_CoalescesDeltasAndFlushesBeforeCompleted()
    {
        var events = new ConcurrentQueue<BackgroundTerminalEvent>();
        Service.TerminalEvent += events.Enqueue;

        var completed = await Service.StartAsync(new BackgroundTerminalStartRequest
        {
            ThreadId = "thread_burst",
            CallId = "call_burst",
            Command = BurstOutputCommand(500),
            WorkingDirectory = _tempDir,
            TimeoutSeconds = 10,
            MaxOutputChars = 100_000
        });

        var captured = events.Where(evt => evt.Terminal.CallId == "call_burst").ToArray();
        var deltas = captured.Where(evt => evt.EventType == "outputDelta").ToArray();
        var deltaOutput = string.Concat(deltas.Select(evt => evt.Delta));
        var persistedOutput = await File.ReadAllTextAsync(completed.OutputPath);

        Assert.Equal(BackgroundTerminalStatus.Completed, completed.Status);
        Assert.NotEmpty(deltas);
        Assert.True(deltas.Length < 100, $"Expected burst coalescing, received {deltas.Length} deltas.");
        Assert.Equal(persistedOutput, deltaOutput);
        Assert.Equal(persistedOutput.TrimEnd('\r', '\n'), completed.Output);
        Assert.Equal("started", captured[0].EventType);
        Assert.Equal("completed", captured[^1].EventType);
        Assert.DoesNotContain(captured.SkipWhile(evt => evt.EventType != "completed").Skip(1),
            evt => evt.EventType == "outputDelta");
    }

    [Fact]
    public async Task StartAsync_Timeout_FlushesAcceptedOutputBeforeCompletion()
    {
        var events = new ConcurrentQueue<BackgroundTerminalEvent>();
        Service.TerminalEvent += events.Enqueue;

        var completed = await Service.StartAsync(new BackgroundTerminalStartRequest
        {
            ThreadId = "thread_timeout",
            CallId = "call_timeout",
            Command = EchoThenSleepCommand("before-timeout"),
            WorkingDirectory = _tempDir,
            TimeoutSeconds = 1,
            MaxOutputChars = 10_000
        });

        var captured = events.Where(evt => evt.Terminal.CallId == "call_timeout").ToArray();
        Assert.Equal(BackgroundTerminalStatus.TimedOut, completed.Status);
        Assert.Contains("before-timeout", completed.Output);
        Assert.Equal("completed", captured[^1].EventType);
        Assert.Contains("before-timeout", await File.ReadAllTextAsync(completed.OutputPath));
    }

    [Fact]
    public async Task StartAsync_FourParallelBursts_KeepEachLifecycleOrderedAndBounded()
    {
        var events = new ConcurrentQueue<BackgroundTerminalEvent>();
        Service.TerminalEvent += events.Enqueue;

        var snapshots = await Task.WhenAll(Enumerable.Range(0, 4).Select(index =>
            Service.StartAsync(new BackgroundTerminalStartRequest
            {
                ThreadId = $"thread_parallel_{index}",
                CallId = $"call_parallel_{index}",
                Command = BurstOutputCommand(1_000),
                WorkingDirectory = _tempDir,
                TimeoutSeconds = 15,
                MaxOutputChars = 200_000
            })));

        Assert.All(snapshots, snapshot =>
        {
            Assert.Equal(BackgroundTerminalStatus.Completed, snapshot.Status);
            Assert.Contains("burst-1000", snapshot.Output);
        });

        var totalDeltaCount = 0;
        for (var index = 0; index < 4; index++)
        {
            var callId = $"call_parallel_{index}";
            var lifecycle = events.Where(evt => evt.Terminal.CallId == callId).ToArray();
            var deltaCount = lifecycle.Count(evt => evt.EventType == "outputDelta");
            totalDeltaCount += deltaCount;
            Assert.Equal("started", lifecycle[0].EventType);
            Assert.Equal("completed", lifecycle[^1].EventType);
            Assert.InRange(deltaCount, 1, 99);
        }

        Assert.InRange(totalDeltaCount, 4, 399);
    }

    [Fact]
    public async Task StopAsync_KillsRunningBackgroundCommand()
    {
        var started = await Service.StartAsync(new BackgroundTerminalStartRequest
        {
            ThreadId = "thread_stop",
            Command = SleepCommand(),
            WorkingDirectory = _tempDir,
            RunInBackground = true,
            YieldTimeMs = 100,
            MaxOutputChars = 1000
        });

        var stopped = await Service.StopAsync(started.SessionId);

        Assert.Equal(BackgroundTerminalStatus.Killed, stopped.Status);
        var sessions = await Service.ListAsync("thread_stop");
        Assert.Contains(sessions, s => s.SessionId == started.SessionId && s.Status == BackgroundTerminalStatus.Killed);
    }

    [Fact]
    public async Task CleanThreadAsync_StopsActiveTerminalButPreservesArtifacts()
    {
        var started = await Service.StartAsync(new BackgroundTerminalStartRequest
        {
            ThreadId = "thread_archive",
            Command = SleepCommand(),
            WorkingDirectory = _tempDir,
            RunInBackground = true,
            YieldTimeMs = 100,
            MaxOutputChars = 1000
        });

        await WaitForFileAsync(Path.ChangeExtension(started.OutputPath, ".json"));
        await Service.CleanThreadAsync("thread_archive");

        Assert.True(File.Exists(Path.ChangeExtension(started.OutputPath, ".json")));
    }

    [Fact]
    public async Task DeleteThreadArtifactsAsync_StopsAndRemovesCurrentThreadDirectory()
    {
        var started = await Service.StartAsync(new BackgroundTerminalStartRequest
        {
            ThreadId = "thread:with-invalid-path",
            Command = EchoCommand("delete-me"),
            WorkingDirectory = _tempDir,
            MaxOutputChars = 1000
        });

        var failures = await Service.DeleteThreadArtifactsAsync("thread:with-invalid-path");

        Assert.Empty(failures);
        Assert.False(File.Exists(started.OutputPath));
        Assert.Empty(await Service.ListAsync("thread:with-invalid-path"));
        Assert.Equal(
            "thread-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("thread:with-invalid-path"))).ToLowerInvariant(),
            BackgroundTerminalService.GetCanonicalThreadDirectoryName("thread:with-invalid-path"));
    }

    [Fact]
    public async Task CleanupExpiredArtifactsAsync_DoesNotRemoveActiveTerminal()
    {
        var started = await Service.StartAsync(new BackgroundTerminalStartRequest
        {
            ThreadId = "thread_retention",
            Command = SleepCommand(),
            WorkingDirectory = _tempDir,
            RunInBackground = true,
            YieldTimeMs = 100,
            MaxOutputChars = 1000
        });

        await WaitForFileAsync(Path.ChangeExtension(started.OutputPath, ".json"));
        var removed = await Service.CleanupExpiredArtifactsAsync();

        Assert.Equal(0, removed);
        Assert.True(File.Exists(Path.ChangeExtension(started.OutputPath, ".json")));
        await Service.StopAsync(started.SessionId);
    }

    [Fact]
    public async Task Constructor_MarksPersistedRunningSessionsAsLost()
    {
        var started = await Service.StartAsync(new BackgroundTerminalStartRequest
        {
            ThreadId = "thread_lost",
            Command = SleepCommand(),
            WorkingDirectory = _tempDir,
            RunInBackground = true,
            YieldTimeMs = 100,
            MaxOutputChars = 1000
        });

        await Service.DisposeAsync();
        var metadataPath = Path.Combine(
            Path.GetDirectoryName(started.OutputPath)!,
            started.SessionId + ".json");
        var metadataJson = await File.ReadAllTextAsync(metadataPath);
        await File.WriteAllTextAsync(
            metadataPath,
            metadataJson.Replace("\"status\": \"killed\"", "\"status\": \"running\""));
        _service = new BackgroundTerminalService(_tempDir, new AppConfig.ShellBackgroundConfig());

        var sessions = await Service.ListAsync("thread_lost");
        Assert.Contains(sessions, s => s.SessionId == started.SessionId && s.Status == BackgroundTerminalStatus.Lost);
    }

    private BackgroundTerminalService Service => _service ?? throw new InvalidOperationException("Not initialized.");

    private static string EchoCommand(string text) =>
        OperatingSystem.IsWindows() ? $"Write-Output {QuotePowerShell(text)}" : $"echo {QuoteBash(text)}";

    private static string DelayedEchoCommand(string text) =>
        OperatingSystem.IsWindows()
            ? $"Start-Sleep -Milliseconds 400; Write-Output {QuotePowerShell(text)}"
            : $"sleep 0.4; echo {QuoteBash(text)}";

    private static string SleepCommand() =>
        OperatingSystem.IsWindows() ? "Start-Sleep -Seconds 5" : "sleep 5";

    private static string ConcurrentStdoutAndStderrCommand() =>
        OperatingSystem.IsWindows()
            ? "1..100 | ForEach-Object { [Console]::Out.WriteLine(\"out-$_\"); [Console]::Error.WriteLine(\"err-$_\") }"
            : "for i in $(seq 1 100); do echo out-$i; echo err-$i >&2; done";

    private static string BurstOutputCommand(int count) =>
        OperatingSystem.IsWindows()
            ? $"1..{count} | ForEach-Object {{ [Console]::Out.WriteLine(\"burst-$_\") }}"
            : $"for i in $(seq 1 {count}); do echo burst-$i; done";

    private static string EchoThenSleepCommand(string text) =>
        OperatingSystem.IsWindows()
            ? $"Write-Output {QuotePowerShell(text)}; Start-Sleep -Seconds 5"
            : $"echo {QuoteBash(text)}; sleep 5";

    private static async Task WaitForFileAsync(string path)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!File.Exists(path) && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(20);

        Assert.True(File.Exists(path), $"Expected artifact file to be created: {path}");
    }

    private static async Task<string> ReadUntilContainsAsync(
        string path,
        params string[] expectedValues)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        var content = string.Empty;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                content = await File.ReadAllTextAsync(path);
                if (expectedValues.All(value => content.Contains(value, StringComparison.Ordinal)))
                    return content;
            }

            await Task.Delay(20);
        }

        return content;
    }

    private static string QuotePowerShell(string value) => "'" + value.Replace("'", "''") + "'";

    private static string QuoteBash(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
