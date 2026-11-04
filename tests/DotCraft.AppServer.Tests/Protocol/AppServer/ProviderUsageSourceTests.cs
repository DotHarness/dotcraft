using System.Text.Json;
using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Contributions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class ProviderUsageSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-usage-source-" + Guid.NewGuid().ToString("N"));
    private readonly ContributionRegistry _registry = new();

    [Fact]
    public async Task FirstSnapshotWinsAndCarriesTheContributingPlugin()
    {
        var declining = new Source(_ => null);
        var failing = new Source(_ => throw new InvalidOperationException("boom"));
        var reporting = new Source(_ => Snapshot() with { PluginId = "spoofed" });
        using var plugin = _registry.CreateRegistrar(ContributionOrigin.Plugin("acme.usage", "g1"));
        _registry.Add<IProviderUsageSource>(declining);
        plugin.Add<IProviderUsageSource>(failing, new ContributionOptions(Order: 1));
        plugin.Add<IProviderUsageSource>(reporting, new ContributionOptions(Order: 2));
        await using var auth = Coordinator();

        var result = ProviderUsageMapping.ToResult(await auth.ReadUsageAsync(Config("https://proxy.example/v1"), "proxy", default));

        var request = Assert.Single(reporting.Requests);
        Assert.Equal(new ProviderUsageRequest("proxy", "anthropic", "https://proxy.example/v1", "sk-test"), request);
        Assert.Single(declining.Requests);
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal("acme.usage", wire.RootElement.GetProperty("pluginId").GetString());
    }

    [Fact]
    public async Task SourcesAreNotAskedWithoutAKeyOrForDeepSeek()
    {
        var source = new Source(_ => Snapshot());
        _registry.Add<IProviderUsageSource>(source);
        await using var auth = Coordinator();

        Assert.Null(await auth.ReadUsageAsync(Config("https://proxy.example/v1", apiKey: ""), "proxy", default));
        await auth.ReadUsageAsync(Config("https://api.deepseek.com"), "proxy", default);

        Assert.Empty(source.Requests);
    }

    [Fact]
    public async Task ASourceThatIgnoresCancellationTimesOutAndLaterSourcesAreAsked()
    {
        var stalled = new StalledSource();
        var reporting = new Source(_ => Snapshot());
        _registry.Add<IProviderUsageSource>(stalled);
        _registry.Add<IProviderUsageSource>(reporting, new ContributionOptions(Order: 1));
        var time = new ManualTime();
        await using var auth = Coordinator(time);

        var read = auth.ReadUsageAsync(Config("https://proxy.example/v1"), "proxy", default);
        await stalled.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromSeconds(15));

        Assert.NotNull(await read.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Single(reporting.Requests);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private ProviderAuthenticationCoordinator Coordinator(TimeProvider? time = null) =>
        new(new ModelBackendResolver(new ModelProviderRegistry([]), _registry, _root), new HttpClient(new FailingHandler()), time);

    private AppConfig Config(string endpoint, string apiKey = "sk-test") => new()
    {
        GlobalConfigPath = Path.Combine(_root, "config.json"),
        Providers = new Dictionary<string, AppConfig.ModelProviderConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["proxy"] = new() { Protocol = "anthropic", EndPoint = endpoint, ApiKey = apiKey }
        }
    };

    private static ProviderUsageSnapshot Snapshot() =>
        new(DateTimeOffset.UnixEpoch, new Dictionary<string, double> { ["usage"] = 1 });

    private sealed class Source(Func<ProviderUsageRequest, ProviderUsageSnapshot?> read) : IProviderUsageSource
    {
        public List<ProviderUsageRequest> Requests { get; } = [];

        public Task<ProviderUsageSnapshot?> ReadUsageAsync(ProviderUsageRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(read(request));
        }
    }

    private sealed class StalledSource : IProviderUsageSource
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProviderUsageSnapshot?> ReadUsageAsync(ProviderUsageRequest request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            return new TaskCompletionSource<ProviderUsageSnapshot?>().Task;
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private TimeSpan _elapsed;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            lock (_timers) _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            ManualTimer[] due;
            lock (_timers)
            {
                _elapsed += by;
                due = _timers.Where(timer => timer.DueAt <= _elapsed).ToArray();
            }
            foreach (var timer in due)
                timer.Fire();
        }

        private sealed class ManualTimer(ManualTime owner, TimerCallback callback, object? state) : ITimer
        {
            public TimeSpan DueAt { get; private set; } = TimeSpan.MaxValue;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? TimeSpan.MaxValue : owner._elapsed + dueTime;
                return true;
            }

            public void Fire()
            {
                DueAt = TimeSpan.MaxValue;
                callback(state);
            }

            public void Dispose() => DueAt = TimeSpan.MaxValue;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }
}
