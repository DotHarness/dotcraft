using DotCraft.Tools;

namespace DotCraft.Tests.Tools;

public sealed class FileToolsGrepTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(),
        "dotcraft-filetools-grep-tests",
        Guid.NewGuid().ToString("N"));

    public FileToolsGrepTests()
    {
        Directory.CreateDirectory(_workspace);
    }

    [Fact]
    public void ResolveRipgrepPath_ConfiguredPathWinsOverEnvironment()
    {
        var configured = Touch("configured-rg.exe");
        var env = Touch("env-rg.exe");
        var previous = Environment.GetEnvironmentVariable(RipgrepFileSearcher.EnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(RipgrepFileSearcher.EnvironmentVariableName, env);

            var resolved = RipgrepFileSearcher.ResolveRipgrepPath(configured);

            Assert.Equal(Path.GetFullPath(configured), resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(RipgrepFileSearcher.EnvironmentVariableName, previous);
        }
    }

    [Fact]
    public void ResolveRipgrepPath_UsesEnvironmentWhenConfigIsEmpty()
    {
        var env = Touch("env-rg.exe");
        var previous = Environment.GetEnvironmentVariable(RipgrepFileSearcher.EnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(RipgrepFileSearcher.EnvironmentVariableName, env);

            var resolved = RipgrepFileSearcher.ResolveRipgrepPath("");

            Assert.Equal(Path.GetFullPath(env), resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(RipgrepFileSearcher.EnvironmentVariableName, previous);
        }
    }

    [Fact]
    public async Task GrepFiles_FallsBackWhenRipgrepIsMissingAndStillSearchesCraft()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, ".craft", "memory"));
        Directory.CreateDirectory(Path.Combine(_workspace, "node_modules", "pkg"));
        await File.WriteAllTextAsync(Path.Combine(_workspace, ".craft", "memory", "HISTORY.md"), "needle in memory");
        await File.WriteAllTextAsync(Path.Combine(_workspace, "node_modules", "pkg", "ignored.txt"), "needle in dependency");
        var missingRg = Path.Combine(_workspace, "missing-rg.exe");
        var tools = new FileTools(_workspace, requireApprovalOutsideWorkspace: false, ripgrepPath: missingRg);

        var result = await tools.GrepFiles("needle");

        Assert.Contains("HISTORY.md", result, StringComparison.Ordinal);
        Assert.Contains("needle in memory", result, StringComparison.Ordinal);
        Assert.DoesNotContain("ignored.txt", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GrepFiles_InvalidRegexReturnsClearErrorWhenRipgrepIsMissing()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace, "notes.txt"), "hello");
        var missingRg = Path.Combine(_workspace, "missing-rg.exe");
        var tools = new FileTools(_workspace, requireApprovalOutsideWorkspace: false, ripgrepPath: missingRg);

        var result = await tools.GrepFiles("[");

        Assert.StartsWith("Error: Invalid regex pattern:", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GrepFiles_RipgrepTimeoutDoesNotFallBackToManagedSearch()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace, "notes.txt"), "needle");
        var sleepingRg = CreateSleepingCommand("sleeping-rg");
        var tools = new FileTools(
            _workspace,
            requireApprovalOutsideWorkspace: false,
            ripgrepPath: sleepingRg,
            searchTimeout: TimeSpan.FromMilliseconds(100));

        var result = await tools.GrepFiles("needle");

        Assert.StartsWith("Error: search timed out after", result, StringComparison.Ordinal);
        Assert.DoesNotContain("notes.txt", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GrepFiles_CancelledTokenPropagates()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace, "notes.txt"), "needle");
        var missingRg = Path.Combine(_workspace, "missing-rg.exe");
        var tools = new FileTools(_workspace, requireApprovalOutsideWorkspace: false, ripgrepPath: missingRg);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tools.GrepFiles("needle", cancellationToken: cts.Token));
    }

    [Fact]
    public async Task RipgrepSearchAsync_CancelledTokenKillsProcessAndPropagates()
    {
        var sleepingRg = CreateSleepingCommand("cancel-rg");
        var searcher = new RipgrepFileSearcher(sleepingRg);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => searcher.SearchAsync(new RipgrepSearchRequest(
                _workspace,
                "needle",
                null,
                100,
                2000,
                5 * 1024 * 1024,
                TimeSpan.FromSeconds(30)),
                cts.Token));
    }

    private string Touch(string fileName)
    {
        var path = Path.Combine(_workspace, fileName);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private string CreateSleepingCommand(string fileName)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(_workspace, $"{fileName}.cmd");
            File.WriteAllText(path, "@echo off\r\nping -n 3 127.0.0.1 > nul\r\n");
            return path;
        }

        var unixPath = Path.Combine(_workspace, fileName);
        File.WriteAllText(unixPath, "#!/usr/bin/env sh\nsleep 2\n");
        File.SetUnixFileMode(
            unixPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return unixPath;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workspace))
                Directory.Delete(_workspace, recursive: true);
        }
        catch
        {
            // ignored
        }
    }
}
