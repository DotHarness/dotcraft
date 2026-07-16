using System.Text.Json;
using DotCraft.AppBinding;
using DotCraft.Configuration;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppBindingProtocolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"appbinding-v2-wire-{Guid.NewGuid():N}");

    [Fact]
    public async Task Initialize_ReportsOnlyAppBindingVersion2()
    {
        using var harness = CreateHarness();
        using var initialized = await harness.InitializeAsync();
        var capabilities = initialized.RootElement.GetProperty("result").GetProperty("capabilities");
        Assert.Equal(2, capabilities.GetProperty("appBindingVersion").GetInt32());
        Assert.False(capabilities.TryGetProperty("appBinding", out _));
        Assert.False(capabilities.TryGetProperty("appContextBlocks", out _));
    }

    [Theory]
    [InlineData("app/binding/request/create")]
    [InlineData("app/binding/accept")]
    [InlineData("app/binding/attachTools")]
    [InlineData("app/binding/context/upsert")]
    [InlineData("ui/resource/read")]
    [InlineData("ui/tool/call")]
    public async Task V1Methods_ReturnStableUpgradeRequired(string method)
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync();
        await harness.ExecuteRequestAsync(harness.BuildRequest(method, new { }));
        using var response = await harness.Transport.ReadNextSentAsync();
        var error = response.RootElement.GetProperty("error");
        Assert.Equal(-32076, error.GetProperty("code").GetInt32());
        Assert.Equal("AppBindingUpgradeRequired", error.GetProperty("data").GetProperty("code").GetString());
        Assert.Equal(2, error.GetProperty("data").GetProperty("params").GetProperty("requiredVersion").GetInt32());
    }

    [Fact]
    public async Task AppPrincipalCannotCallThreadControlPlane()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync();
        harness.Connection.BindAppPrincipal("apppr_test", "com.example.test");
        await harness.ExecuteRequestAsync(harness.BuildRequest("thread/list", new { }));
        using var response = await harness.Transport.ReadNextSentAsync();
        Assert.Equal("AppPrincipalUnauthorized",
            response.RootElement.GetProperty("error").GetProperty("data").GetProperty("code").GetString());
    }

    private AppServerTestHarness CreateHarness()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".craft"));
        var monitor = new AppConfigMonitor(AppConfigTestFactory.CreateOpenAI());
        var control = new AppBindingService();
        var extension = new AppBindingProtocolExtension(control, new AppBindingCoordinator(control), monitor);
        return new AppServerTestHarness(protocolExtensions: [extension], workspaceCraftPath: Path.Combine(_root, ".craft"), appConfigMonitor: monitor);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
