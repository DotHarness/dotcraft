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

    [Fact]
    public async Task EnsureAsync_CancellationDisposesStartingProcessAndPropagatesCancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registry = CreateRegistry(out var starts, process =>
        {
            process.WaitForReady = async token =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return process.Ready;
            };
        });
        using var cancellation = new CancellationTokenSource();
        var ensure = registry.EnsureAsync(
            new EnsureManagedServiceRequest { ServiceId = "oratorio", Executable = CreateExecutablePlaceholder() },
            cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ensure.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(starts.Single().Disposed);
        Assert.Null(registry.Get("oratorio").Pid);
        Assert.Null(registry.Get("oratorio").AccessToken);
    }

    private ManagedLocalServiceRegistry CreateRegistry(out List<FakeProcess> starts, Action<FakeProcess>? configure = null)
    {
        var captured = new List<FakeProcess>();
        starts = captured;
        var registry = new ManagedLocalServiceRegistry([
            new ManagedLocalServiceDefinition("oratorio", Path.Combine(_root, "state"), "/health", ["oratorio", "serve"])
        ]);
        registry.StartProcessAsync = (launch, _) =>
        {
            var process = new FakeProcess(
                1000 + captured.Count,
                new ManagedServiceReady(launch.ServiceId, launch.Endpoint, "test"));
            configure?.Invoke(process);
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
        public ManagedServiceReady Ready { get; } = ready;
        public Func<CancellationToken, Task<ManagedServiceReady>>? WaitForReady { get; set; }
        public Task<ManagedServiceReady> WaitForReadyAsync(CancellationToken cancellationToken) =>
            WaitForReady?.Invoke(cancellationToken) ?? Task.FromResult(Ready);
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
