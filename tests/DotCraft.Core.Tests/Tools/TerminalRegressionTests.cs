using System.Text;
using DotCraft.Configuration;
using DotCraft.Security.ShellCommands;
using DotCraft.Tools.BackgroundTerminals;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class TerminalRegressionTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "terminal-regression-" + Guid.NewGuid().ToString("N"));
    private BackgroundTerminalService _service = null!;

    public Task InitializeAsync()
    {
        _service = new(_root, new AppConfig.ShellBackgroundConfig { MaxYieldTimeMs = 5000 });
        return Task.CompletedTask;
    }
    public async Task DisposeAsync()
    {
        await _service.DisposeAsync();
        Directory.Delete(_root, true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task PollAfterExitReturnsFinalOutputRepeatedlyAndAfterRecovery(int exitCode)
    {
        var shell = ShellIdentityResolver.Host.TryResolve(null, out var resolved, out var reason) ? resolved : throw new Exception(reason);
        var command = shell.Family == ShellFamily.PowerShell
            ? $"Write-Output 'final-tail'; exit {exitCode}"
            : $"echo final-tail; exit {exitCode}";
        var ended = await _service.StartAsync(Request(command, shell));
        for (var i = 0; i < 2; i++)
        {
            var polled = await _service.WriteStdinAsync(ended.SessionId, "", 1);
            Assert.Equal(exitCode, polled.ExitCode);
            Assert.Equal(ended.Status, polled.Status);
            Assert.Contains("final-tail", polled.Output);
        }
        await Assert.ThrowsAnyAsync<Exception>(() => _service.WriteStdinAsync(ended.SessionId, "input"));
        await _service.DisposeAsync();
        _service = new(_root, new AppConfig.ShellBackgroundConfig());
        var recovered = await _service.WriteStdinAsync(ended.SessionId, "", 1);
        Assert.Equal(exitCode, recovered.ExitCode);
        Assert.Contains("final-tail", recovered.Output);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.WriteStdinAsync("missing", "", 1));
    }

    [Fact]
    public async Task PollDuringExitDrainsBothStreamsBeforeReturningCompletion()
    {
        var shell = ShellIdentityResolver.Host.TryResolve(null, out var resolved, out var reason) ? resolved : throw new Exception(reason);
        var command = shell.Family == ShellFamily.PowerShell
            ? "Start-Sleep -Milliseconds 300; [Console]::Out.Write('stdout-tail'); [Console]::Error.Write('stderr-tail')"
            : "sleep 0.3; printf stdout-tail; printf stderr-tail >&2";
        var start = await _service.StartAsync(Request(command, shell) with { RunInBackground = true, YieldTimeMs = 1 });
        var end = await _service.WriteStdinAsync(start.SessionId, "", 5000);
        Assert.Equal(BackgroundTerminalStatus.Completed, end.Status);
        Assert.Equal(0, end.ExitCode);
        Assert.Contains("stdout-tail", end.Output);
        Assert.Contains("stderr-tail", end.Output);
    }

    [Fact]
    public async Task CmdPreservesQuotedArgumentsExecutablePathsPipesAndRedirection()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.True(ShellIdentityResolver.Host.TryResolve("cmd", out var shell, out _));
        var filtered = await _service.StartAsync(Request("tasklist /FI \"PID eq 4\" /FO CSV /NH", shell));
        Assert.Equal(0, filtered.ExitCode);
        var folder = Path.Combine(_root, "path with spaces");
        Directory.CreateDirectory(folder);
        var executable = Path.Combine(folder, "cmd.exe");
        File.Copy(shell.ExecutablePath, executable);
        var quoted = await _service.StartAsync(Request($"\"{executable}\" /d /c \"echo quoted argument\"", shell));
        Assert.Equal(0, quoted.ExitCode);
        Assert.Contains("quoted argument", quoted.Output);
        var pipes = await _service.StartAsync(Request("echo payload>\"output file.txt\" & type \"output file.txt\" | findstr payload", shell));
        Assert.Equal(0, pipes.ExitCode);
        Assert.Contains("payload", pipes.Output);
        Assert.Contains("payload", await File.ReadAllTextAsync(Path.Combine(_root, "output file.txt")));
    }

    [Theory]
    [InlineData("pwsh")]
    [InlineData("powershell")]
    public async Task PowerShellBothStreamsKeepChinese(string selector)
    {
        if (!OperatingSystem.IsWindows() && selector == "powershell") return;
        Assert.True(ShellIdentityResolver.Host.TryResolve(selector, out var shell, out var reason), reason);
        var result = await _service.StartAsync(Request("[Console]::Out.Write('标准输出中文'); [Console]::Error.Write('错误输出中文')", shell));
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("标准输出中文", result.Output);
        Assert.Contains("错误输出中文", result.Output);
    }

    private BackgroundTerminalStartRequest Request(string command, ShellIdentity shell) => new()
    {
        ThreadId = "thread", WorkingDirectory = _root, Command = command, Shell = shell, TimeoutSeconds = 10
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidOutputBytesDoNotChangeUtf8Decoding(bool stderr)
    {
        Assert.True(ShellIdentityResolver.Host.TryResolve("pwsh", out var shell, out var reason), reason);
        byte[] bytes = [0xFF, .. Encoding.UTF8.GetBytes("中文😀tail")];
        var data = Convert.ToBase64String(bytes);
        var stream = stderr ? "OpenStandardError" : "OpenStandardOutput";
        var command = $"$b = [Convert]::FromBase64String('{data}'); $o = [Console]::{stream}(); " +
            "foreach ($v in $b) { $o.WriteByte($v); $o.Flush() }";
        var result = await _service.StartAsync(Request(command, shell));
        Assert.Equal(0, result.ExitCode);
        var output = await File.ReadAllTextAsync(result.OutputPath);
        Assert.Equal("\uFFFD中文😀tail", output);
    }

    [Fact]
    public async Task MetadataPersistenceFailureReleasesCapacityAndCompletedEventCanReadSnapshot()
    {
        if (!OperatingSystem.IsWindows()) return;
        await _service.DisposeAsync();
        _service = new(_root, new AppConfig.ShellBackgroundConfig { MaxSessionsPerThread = 1 });
        Assert.True(ShellIdentityResolver.Host.TryResolve("pwsh", out var shell, out var reason), reason);
        var observed = new TaskCompletionSource<BackgroundTerminalSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        _service.TerminalEvent += evt =>
        {
            if (evt.EventType != "completed") return;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { observed.TrySetResult(_service.ReadAsync(evt.Terminal.SessionId, ct: deadline.Token).GetAwaiter().GetResult()); }
            catch (Exception exception) { observed.TrySetException(exception); }
        };
        var started = await _service.StartAsync(Request("Start-Sleep -Seconds 1; Write-Output final-tail", shell)
            with { RunInBackground = true, YieldTimeMs = 1 });
        var metadata = Path.ChangeExtension(started.OutputPath, ".json");
        var attributes = File.GetAttributes(metadata);
        try
        {
            File.SetAttributes(metadata, attributes | FileAttributes.ReadOnly);
            var ended = await _service.WriteStdinAsync(started.SessionId, "", 5000);
            Assert.Equal(0, ended.ExitCode);
            Assert.Contains("final-tail", ended.Output);
            var published = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ended.SessionId, published.SessionId);
            Assert.Equal(0, published.ExitCode);
            var next = await _service.StartAsync(Request("Write-Output next", shell));
            Assert.Equal(0, next.ExitCode);
        }
        finally { File.SetAttributes(metadata, attributes); }
    }
}
