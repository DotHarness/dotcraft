using System.Net;
using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Contributions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class DeepSeekBalanceUsageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-balance-" + Guid.NewGuid().ToString("N"));
    private readonly Handler _handler = new();
    private readonly Clock _clock = new();

    [Fact]
    public async Task OfficialDeepSeekProviderReportsBalancesOnTheWire()
    {
        _handler.Body = """
            {"is_available":true,"balance_infos":[
              {"currency":"CNY","total_balance":"110.00","granted_balance":"10.00","topped_up_balance":"100.00"}]}
            """;
        await using var auth = Coordinator();

        var result = ProviderUsageMapping.ToResult(await auth.ReadUsageAsync(Config("https://api.deepseek.com/anthropic"), "ds", default));

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("https://api.deepseek.com/user/balance", request.RequestUri!.ToString());
        Assert.Equal("Bearer sk-test", request.Headers.Authorization!.ToString());
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.True(wire.RootElement.GetProperty("available").GetBoolean());
        Assert.False(wire.RootElement.TryGetProperty("limitReachedKind", out _));
        var balance = Assert.Single(wire.RootElement.GetProperty("balances").EnumerateArray().ToArray());
        Assert.Equal("CNY", balance.GetProperty("currency").GetString());
        Assert.Equal("110.00", balance.GetProperty("total").GetString());
        Assert.Equal("10.00", balance.GetProperty("granted").GetString());
        Assert.Equal("100.00", balance.GetProperty("toppedUp").GetString());
    }

    [Fact]
    public async Task InsufficientBalanceIsReportedAsLimitReached()
    {
        _handler.Body = """{"is_available":false,"balance_infos":[]}""";
        await using var auth = Coordinator();

        var usage = await auth.ReadUsageAsync(Config("https://api.deepseek.com"), "ds", default);

        Assert.Equal("balance", usage!.LimitReachedKind);
        Assert.Empty(usage.Balances!);
    }

    [Fact]
    public async Task BalanceIsReusedBrieflyThenRead()
    {
        _handler.Body = """{"is_available":true,"balance_infos":[]}""";
        await using var auth = Coordinator();
        var config = Config("https://api.deepseek.com/v1");

        await auth.ReadUsageAsync(config, "ds", default);
        await auth.ReadUsageAsync(config, "ds", default);
        Assert.Single(_handler.Requests);
        _clock.Advance(TimeSpan.FromSeconds(31));
        await auth.ReadUsageAsync(config, "ds", default);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task FailedReadsAndOtherEndpointsReportNoUsage()
    {
        _handler.Status = HttpStatusCode.Unauthorized;
        await using var auth = Coordinator();

        Assert.Null(await auth.ReadUsageAsync(Config("https://api.deepseek.com"), "ds", default));
        Assert.Null(await auth.ReadUsageAsync(Config("https://proxy.example/v1"), "ds", default));
        Assert.Single(_handler.Requests);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private ProviderAuthenticationCoordinator Coordinator() =>
        new(new ModelBackendResolver(new ModelProviderRegistry([]), new ContributionRegistry(), _root), new HttpClient(_handler), _clock);

    private AppConfig Config(string endpoint) => new()
    {
        GlobalConfigPath = Path.Combine(_root, "config.json"),
        Providers = new Dictionary<string, AppConfig.ModelProviderConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["ds"] = new() { Protocol = "anthropic", EndPoint = endpoint, ApiKey = "sk-test" }
        }
    };

    private sealed class Handler : HttpMessageHandler
    {
        public string Body { get; set; } = "{}";
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
