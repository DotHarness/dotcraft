using System.Net;
using DotCraft.Hub;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class MobileRegistryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "DotCraftMobileRegistry_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void PairingCode_ExpiresAfterTenMinutes()
    {
        var clock = new ManualClock(DateTimeOffset.Parse("2026-10-03T08:00:00Z"));
        var registry = new MobileRegistry(Path.Combine(_directory, "mobile.json"), clock);
        var (_, code, expiresAt) = registry.MintPairing();

        Assert.Equal(clock.Now + TimeSpan.FromMinutes(10), expiresAt);
        clock.Now += TimeSpan.FromMinutes(10);

        Assert.Null(registry.TryConsumePairing(code));
    }

    [Fact]
    public void PairingCode_IsSingleUse_AndOnlyTheNewestIsValid()
    {
        var registry = new MobileRegistry(Path.Combine(_directory, "mobile.json"));
        var (_, older, _) = registry.MintPairing();
        var (pairingId, newest, _) = registry.MintPairing();

        Assert.Null(registry.TryConsumePairing(older));
        Assert.Equal(pairingId, registry.TryConsumePairing(newest));
        Assert.Null(registry.TryConsumePairing(newest));
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("10.20.30.40", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.254", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.1.20", true)]
    [InlineData("169.254.10.1", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("100.127.255.1", true)]
    [InlineData("100.128.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("fe80::1", true)]
    [InlineData("fd12:3456::1", true)]
    [InlineData("2001:db8::1", false)]
    [InlineData("::ffff:192.168.1.20", true)]
    [InlineData("::ffff:8.8.8.8", false)]
    public void SourceFilter_AdmitsOnlyLocalAndPrivateNetworks(string address, bool allowed)
    {
        Assert.Equal(allowed, MobileNetwork.IsAllowedSource(IPAddress.Parse(address)));
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (Exception) { }
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
