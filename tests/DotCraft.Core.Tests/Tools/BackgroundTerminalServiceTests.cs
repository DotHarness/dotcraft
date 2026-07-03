using DotCraft.Configuration;
using DotCraft.Tools.BackgroundTerminals;

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
        var events = new List<BackgroundTerminalEvent>();
        var eventsLock = new object();
        Service.TerminalEvent += terminalEvent =>
        {
            lock (eventsLock)
                events.Add(terminalEvent);
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
        BackgroundTerminalEvent[] capturedEvents;
        lock (eventsLock)
            capturedEvents = events.ToArray();
        var outputDelta = Assert.Single(capturedEvents, e => e.EventType == "outputDelta");
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

        var completed = await Service.ReadAsync(started.SessionId, waitMs: 3000, maxOutputChars: 20_000);
        var log = await File.ReadAllTextAsync(started.OutputPath);

        Assert.Equal(BackgroundTerminalStatus.Completed, completed.Status);
        Assert.Contains("out-1", completed.Output);
        Assert.Contains("err-1", completed.Output);
        Assert.Contains("out-100", log);
        Assert.Contains("err-100", log);
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

    private static string QuotePowerShell(string value) => "'" + value.Replace("'", "''") + "'";

    private static string QuoteBash(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
