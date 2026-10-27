using System.Diagnostics;
using DotCraft.Utilities;
using Xunit;

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
        var result = await GitProcessRunner.RunAsync(
            _workspacePath,
            ["cat-file", "--batch"],
            timeout: TimeSpan.FromSeconds(10),
            ct: CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_WhenTimedOut_ThrowsGitProcessTimeoutException()
    {
        await Assert.ThrowsAsync<GitProcessTimeoutException>(async () =>
            await GitProcessRunner.RunAsync(
                _workspacePath,
                ["-c", "alias.wait=!sleep 30", "wait"],
                timeout: TimeSpan.FromMilliseconds(200),
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
        }
    }
}
