using System.Diagnostics;
using DotCraft.Utilities;

namespace DotCraft.Tests.Sessions.Utilities;

public sealed class GitProcessRunnerTests : IDisposable
{
    private readonly string _workspacePath = Path.Combine(
        Path.GetTempPath(),
        "git-process-runner-tests",
        Guid.NewGuid().ToString("N"));

    public GitProcessRunnerTests()
    {
        Directory.CreateDirectory(_workspacePath);
        RunGitSetup("init");
    }

    [Fact]
    public async Task RunAsync_ReturnsStdoutForNormalCommand()
    {
        var result = await GitProcessRunner.RunAsync(
            _workspacePath,
            ["status", "--short"],
            timeout: TimeSpan.FromSeconds(5),
            ct: CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.StdErr.Length == 0 || !result.StdErr.Contains("fatal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_WhenCallerCancellationRequested_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await GitProcessRunner.RunAsync(
                _workspacePath,
                ["status", "--short"],
                timeout: TimeSpan.FromSeconds(5),
                ct: cts.Token));
    }

    [Fact]
    public async Task RunAsync_WhenGitReadsFromStdin_CompletesBecauseRunnerClosesInput()
    {
        var started = Stopwatch.StartNew();

        var result = await GitProcessRunner.RunAsync(
            _workspacePath,
            ["cat-file", "--batch"],
            timeout: TimeSpan.FromSeconds(3),
            ct: CancellationToken.None);

        started.Stop();
        Assert.InRange(started.ElapsedMilliseconds, 0, 1500);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_WhenTimedOut_ThrowsGitProcessTimeoutException()
    {
        var bulkDir = Path.Combine(_workspacePath, "bulk");
        Directory.CreateDirectory(bulkDir);
        for (var i = 0; i < 4000; i++)
        {
            var filePath = Path.Combine(bulkDir, $"f-{i:0000}.txt");
            await File.WriteAllTextAsync(filePath, $"file-{i}");
        }

        await Assert.ThrowsAsync<GitProcessTimeoutException>(async () =>
            await GitProcessRunner.RunAsync(
                _workspacePath,
                ["status", "--short", "--untracked-files=all"],
                timeout: TimeSpan.FromMilliseconds(1),
                ct: CancellationToken.None));
    }

    private void RunGitSetup(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _workspacePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git setup command.");
        process.StandardInput.Close();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"git {string.Join(" ", args)} timed out.");
        }

        var stderr = process.StandardError.ReadToEnd();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(" ", args)} failed: {stderr}");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workspacePath, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
