using DotCraft.Hub;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class ManagedLocalServiceRegistryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DotCraftManagedService_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task EnsureAsync_CoalescesConcurrentStartsAndReusesCredentials()
    {
        var executable = CreateExecutablePlaceholder();
        await using var registry = CreateRegistry(out var starts);

        var requests = Enumerable.Range(0, 8)
            .Select(_ => registry.EnsureAsync(new EnsureManagedServiceRequest
            {
                ServiceId = "oratorio",
                Executable = executable
            }, CancellationToken.None));

        var responses = await Task.WhenAll(requests);

        Assert.Single(starts);
        Assert.All(responses, item => Assert.Equal(HubManagedServiceStates.Running, item.State));
        Assert.Single(responses.Select(item => item.AccessToken).Distinct());
        Assert.Single(responses.Select(item => item.Pid).Distinct());
    }

    [Fact]
    public async Task EnsureAsync_RejectsUnregisteredServiceBeforeLaunch()
    {
        await using var registry = CreateRegistry(out var starts);

        var error = await Assert.ThrowsAsync<HubProtocolException>(() => registry.EnsureAsync(
            new EnsureManagedServiceRequest { ServiceId = "third-party", Executable = CreateExecutablePlaceholder() },
            CancellationToken.None));

        Assert.Equal("managedServiceNotRegistered", error.Code);
        Assert.Empty(starts);
    }

    [Fact]
    public async Task StopAndRestartReplaceProcessAndToken()
    {
        var executable = CreateExecutablePlaceholder();
        await using var registry = CreateRegistry(out var starts);
        var first = await registry.EnsureAsync(new EnsureManagedServiceRequest
        {
            ServiceId = "oratorio",
            Executable = executable
        }, CancellationToken.None);

        var stopped = await registry.StopAsync("oratorio", CancellationToken.None);
        var restarted = await registry.RestartAsync(new ManagedServiceRequest
        {
            ServiceId = "oratorio",
            Executable = executable
        }, CancellationToken.None);

        Assert.Equal(HubManagedServiceStates.Stopped, stopped.State);
        Assert.Null(stopped.AccessToken);
        Assert.Equal(HubManagedServiceStates.Running, restarted.State);
        Assert.NotEqual(first.AccessToken, restarted.AccessToken);
        Assert.Equal(2, starts.Count);
        Assert.True(starts[0].Disposed);
    }

    [Fact]
    public async Task EnsureAsync_ReportsStableFailureAndDisposesFailedProcess()
    {
        var executable = CreateExecutablePlaceholder();
        await using var registry = CreateRegistry(out var starts);
        registry.ProbeHealthAsync = (_, _) => throw new HttpRequestException("health failed");

        var error = await Assert.ThrowsAsync<HubProtocolException>(() => registry.EnsureAsync(
            new EnsureManagedServiceRequest { ServiceId = "oratorio", Executable = executable },
            CancellationToken.None));

        Assert.Equal("managedServiceStartFailed", error.Code);
        Assert.True(starts.Single().Disposed);
        var state = registry.Get("oratorio");
        Assert.Equal(HubManagedServiceStates.Unhealthy, state.State);
        Assert.Null(state.AccessToken);
    }

    private ManagedLocalServiceRegistry CreateRegistry(out List<FakeProcess> starts)
    {
        var captured = new List<FakeProcess>();
        starts = captured;
        var registry = new ManagedLocalServiceRegistry([
            new ManagedLocalServiceDefinition("oratorio", Path.Combine(_root, "state"), "/health")
        ]);
        registry.StartProcessAsync = (launch, _) =>
        {
            var process = new FakeProcess(
                1000 + captured.Count,
                new ManagedServiceReady(launch.ServiceId, launch.Endpoint, "test"));
            captured.Add(process);
            return Task.FromResult<IManagedLocalServiceProcess>(process);
        };
        registry.ProbeHealthAsync = (_, _) => Task.CompletedTask;
        return registry;
    }

    private string CreateExecutablePlaceholder()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "service.exe");
        File.WriteAllText(path, string.Empty);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeProcess(int processId, ManagedServiceReady ready) : IManagedLocalServiceProcess
    {
        public int ProcessId { get; } = processId;
        public bool IsRunning => !Disposed;
        public string? RecentStderr => null;
        public bool Disposed { get; private set; }
        public Task<ManagedServiceReady> WaitForReadyAsync(CancellationToken cancellationToken) => Task.FromResult(ready);
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
