using DotCraft.Configuration;
using DotCraft.Tools.Sandbox;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class SandboxProviderBoundaryTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "DotCraftSandboxProviderBoundaryTests_" + Guid.NewGuid().ToString("N"));

    public SandboxProviderBoundaryTests() => Directory.CreateDirectory(_tempRoot);

    [Fact]
    public async Task ShellTool_FormatsProviderNeutralOutputAndError()
    {
        var client = new StubSandboxCommandClient
        {
            RunHandler = (_, options, _, _) =>
            {
                Assert.Equal(17, options.TimeoutSeconds);
                return Task.FromResult(new SandboxCommandResult(
                    [new SandboxCommandLogLine("output")],
                    [new SandboxCommandLogLine("warning")],
                    new SandboxCommandError("exit", "7")));
            }
        };

        var result = await new SandboxShellTools(client, timeoutSeconds: 17).Exec("false");

        Assert.Equal("output\n\nSTDERR:\nwarning\n\nError: exit: 7", result.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task FileTool_WritesThroughProviderNeutralFileOperations()
    {
        var directories = new List<SandboxDirectoryEntry>();
        var writes = new List<SandboxWriteEntry>();
        var instance = new StubSandboxInstance
        {
            CreateDirectoriesHandler = (entries, _) =>
            {
                directories.AddRange(entries);
                return Task.CompletedTask;
            },
            WriteFilesHandler = (entries, _) =>
            {
                writes.AddRange(entries);
                return Task.CompletedTask;
            }
        };
        await using var manager = new SandboxSessionManager(
            new AppConfig.SandboxConfig { SyncWorkspace = false, IdleTimeoutSeconds = 0 },
            new StubSandboxProvider(_ => Task.FromResult<ISandboxInstance>(instance)),
            _tempRoot);

        var result = await new SandboxFileTools(manager).WriteFile("src/app.cs", "content");

        Assert.Contains(directories, entry => entry == new SandboxDirectoryEntry("/workspace/src", 755));
        Assert.Contains(writes, entry => entry == new SandboxWriteEntry("/workspace/src/app.cs", "content", 644));
        Assert.Contains("Successfully wrote", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileTool_FormatsNormalizedProviderFailure()
    {
        var instance = new StubSandboxInstance
        {
            WriteFilesHandler = (_, _) => throw new SandboxProviderException("write_failed", "disk full")
        };
        await using var manager = new SandboxSessionManager(
            new AppConfig.SandboxConfig { SyncWorkspace = false, IdleTimeoutSeconds = 0 },
            new StubSandboxProvider(_ => Task.FromResult<ISandboxInstance>(instance)),
            _tempRoot);

        var result = await new SandboxFileTools(manager).WriteFile("app.cs", "content");

        Assert.Equal("Sandbox error: [write_failed] disk full", result);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best effort test cleanup.
        }
    }
}
