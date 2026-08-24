using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Tools.Sandbox;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class SandboxToolSourceGenerationTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(), "dotcraft-sandbox-generation-tests", Guid.NewGuid().ToString("N"));
    private readonly string _workspaceOne;
    private readonly string _workspaceTwo;

    public SandboxToolSourceGenerationTests()
    {
        _workspaceOne = Path.Combine(_tempRoot, "workspace-one");
        _workspaceTwo = Path.Combine(_tempRoot, "workspace-two");
        Directory.CreateDirectory(_workspaceOne);
        Directory.CreateDirectory(_workspaceTwo);
    }

    [Fact]
    public async Task WorkspaceChange_RetiresOldManagerUntilNarrowCleanup()
    {
        var instances = new List<TrackedSandboxInstance>();
        var provider = new StubSandboxProvider(_ =>
        {
            var instance = new TrackedSandboxInstance($"sandbox-{instances.Count + 1}");
            instances.Add(instance);
            return Task.FromResult<ISandboxInstance>(instance);
        });

        await using var source = CreateSource(provider);

        await ReadFileAsync(source, _workspaceOne, [_workspaceOne]);
        await ReadFileAsync(source, Path.Combine(_workspaceOne, "."), [_workspaceOne]);

        Assert.Single(instances);

        await ReadFileAsync(source, _workspaceTwo, [_workspaceTwo]);

        Assert.Equal(2, instances.Count);
        Assert.False(instances[0].WasKilled);
        Assert.False(instances[0].WasDisposed);
        Assert.False(instances[1].WasKilled);

        await source.ReleaseRetiredThreadResourcesAsync("thread-sandbox");

        Assert.True(instances[0].WasKilled);
        Assert.True(instances[0].WasDisposed);
        Assert.False(instances[1].WasKilled);
        Assert.False(instances[1].WasDisposed);

        await source.ReleaseThreadAsync("thread-sandbox");

        Assert.True(instances[1].WasKilled);
        Assert.True(instances[1].WasDisposed);
    }

    [Fact]
    public async Task OrderedRootsChange_CreatesNewGeneration()
    {
        var createCalls = 0;
        var provider = new StubSandboxProvider(_ =>
        {
            createCalls++;
            return Task.FromResult<ISandboxInstance>(new StubSandboxInstance
            {
                Id = $"sandbox-{createCalls}"
            });
        });

        await using var source = CreateSource(provider);

        await ReadFileAsync(source, _workspaceOne, [_workspaceOne, _workspaceTwo]);
        await ReadFileAsync(source, _workspaceOne, [_workspaceTwo, _workspaceOne]);

        Assert.Equal(2, createCalls);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp test directories.
        }
    }

    private SandboxToolSource CreateSource(ISandboxProvider provider)
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        config.Tools.Sandbox.Enabled = true;
        config.Tools.Sandbox.SyncWorkspace = false;
        config.Tools.Sandbox.IdleTimeoutSeconds = 0;
        return new SandboxToolSource(
            config,
            provider,
            TestModelProviderRegistry.Create(),
            new SkillsLoader(_tempRoot),
            new AutoApproveApprovalService(),
            ".craft");
    }

    private static async Task ReadFileAsync(
        SandboxToolSource source,
        string workspace,
        IReadOnlyList<string> roots)
    {
        var registrations = await source.GetRegistrationsAsync(new ToolPlanningContext(
            "thread-sandbox",
            null,
            workspace,
            Path.Combine(workspace, ".craft"),
            "agent",
            null,
            [],
            1,
            workspaceRoots: roots,
            requireApprovalOutsideWorkspace: false));
        var readFile = Assert.Single(
            registrations,
            registration => registration.Definition.Name.Name == "ReadFile");

        await readFile.Binding.Runtime.InvokeAsync(
            new ToolInvocationContext(
                "thread-sandbox",
                null,
                Guid.NewGuid().ToString("N"),
                ToolInvocationAudience.Model,
                readFile.Definition.Name,
                readFile.Definition.Id,
                readFile.Binding.Id,
                readFile.Binding.Revision,
                DateTimeOffset.UtcNow),
            new JsonObject { ["path"] = Path.Combine(workspace, "file.txt") });
    }

    private sealed class TrackedSandboxInstance(string id) : ISandboxInstance
    {
        public string Id { get; } = id;

        public bool WasKilled { get; private set; }

        public bool WasDisposed { get; private set; }

        public Task<SandboxCommandResult> RunCommandAsync(
            string command,
            SandboxCommandOptions? options = null,
            SandboxCommandHandlers? handlers = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SandboxCommandResult([], []));

        public Task InterruptCommandAsync(
            string executionId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> ReadFileAsync(
            string path,
            CancellationToken cancellationToken = default) => Task.FromResult("content");

        public Task CreateDirectoriesAsync(
            IReadOnlyList<SandboxDirectoryEntry> entries,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task WriteFilesAsync(
            IReadOnlyList<SandboxWriteEntry> entries,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task KillAsync(CancellationToken cancellationToken = default)
        {
            WasKilled = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            WasDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
