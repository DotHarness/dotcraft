using DotCraft.Configuration;
using DotCraft.Tools.Sandbox;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class SandboxSessionManagerTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "DotCraftSandboxSessionManagerTests_" + Guid.NewGuid().ToString("N"));

    public SandboxSessionManagerTests() => Directory.CreateDirectory(_tempRoot);

    [Fact]
    public async Task GetOrCreateAsync_ReusesSessionAndIsolatesDifferentKeys()
    {
        var created = 0;
        var provider = new StubSandboxProvider(_ => Task.FromResult<ISandboxInstance>(
            new StubSandboxInstance { Id = $"sandbox-{++created}" }));
        await using var manager = CreateManager(provider, syncWorkspace: false);

        var first = await manager.GetOrCreateAsync("first");
        var reused = await manager.GetOrCreateAsync("first");
        var second = await manager.GetOrCreateAsync("second");

        Assert.Same(first, reused);
        Assert.NotSame(first, second);
        Assert.Equal(2, created);
    }

    [Fact]
    public async Task GetOrCreateAsync_SynchronizesWorkspaceThroughProviderNeutralOperations()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempRoot, "hello.txt"), "hello sandbox");
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
        await using var manager = CreateManager(
            new StubSandboxProvider(_ => Task.FromResult<ISandboxInstance>(instance)),
            syncWorkspace: true);

        await manager.GetOrCreateAsync();

        Assert.Contains(directories, entry => entry.Path == "/workspace");
        var write = Assert.Single(writes, entry => entry.Path == "/workspace/hello.txt");
        Assert.Equal("hello sandbox", write.Data);
    }

    [Fact]
    public async Task ReleaseAndDispose_KillAndDisposeOwnedInstances()
    {
        var killed = 0;
        var disposed = 0;
        var provider = new StubSandboxProvider(_ => Task.FromResult<ISandboxInstance>(new StubSandboxInstance
        {
            KillHandler = _ =>
            {
                killed++;
                return Task.CompletedTask;
            },
            DisposeHandler = () =>
            {
                disposed++;
                return ValueTask.CompletedTask;
            }
        }));
        var manager = CreateManager(provider, syncWorkspace: false);
        await manager.GetOrCreateAsync("released");
        await manager.ReleaseAsync("released");
        await manager.GetOrCreateAsync("shutdown");

        await manager.DisposeAsync();

        Assert.Equal(2, killed);
        Assert.Equal(2, disposed);
    }

    [Fact]
    public async Task CleanupIdleSandboxesAsync_ReleasesExpiredInstance()
    {
        var killed = false;
        var config = CreateConfig(syncWorkspace: false);
        config.IdleTimeoutSeconds = -1;
        var instance = new StubSandboxInstance
        {
            KillHandler = _ =>
            {
                killed = true;
                return Task.CompletedTask;
            }
        };
        await using var manager = new SandboxSessionManager(
            config,
            new StubSandboxProvider(_ => Task.FromResult<ISandboxInstance>(instance)),
            _tempRoot,
            ".craft");
        await manager.GetOrCreateAsync("idle");

        await manager.CleanupIdleSandboxesAsync();

        Assert.True(killed);
    }

    private SandboxSessionManager CreateManager(ISandboxProvider provider, bool syncWorkspace) =>
        new(CreateConfig(syncWorkspace), provider, _tempRoot, ".craft");

    private static AppConfig.SandboxConfig CreateConfig(bool syncWorkspace) => new()
    {
        IdleTimeoutSeconds = 0,
        SyncWorkspace = syncWorkspace
    };

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
