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

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private ProviderAuthenticationCoordinator Coordinator() =>
        new(new ModelBackendResolver(new ModelProviderRegistry([]), _registry, _root), new HttpClient(new FailingHandler()));

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

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }
}
