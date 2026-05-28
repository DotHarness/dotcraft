using System.Text.Json;
using DotCraft.Modules;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerExtensionTests : IDisposable
{
    private readonly TestExtension _extension = new();
    private readonly AppServerTestHarness _h;

    public AppServerExtensionTests()
    {
        _h = new AppServerTestHarness(protocolExtensions: [_extension]);
    }

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Initialize_MergesExtensionCapabilities()
    {
        var initDoc = await _h.InitializeAsync();
        var caps = initDoc.RootElement
            .GetProperty("result")
            .GetProperty("capabilities");

        Assert.True(caps.GetProperty("extensions").GetProperty("testExtension").GetBoolean());
    }

    [Fact]
    public async Task UnknownExtensionMethod_RoutesToExtension()
    {
        await _h.InitializeAsync();

        var msg = _h.BuildRequest("test/echo", new { value = "hello" });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        Assert.Equal("hello", doc.RootElement.GetProperty("result").GetProperty("echo").GetString());
    }

    [Fact]
    public void DuplicateExtensionMethods_ThrowAtConstruction()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new AppServerRequestHandler(
                _h.Service,
                _h.Connection,
                _h.Transport,
                new ModuleRegistryChannelListContributor(new ModuleRegistry(), null, null),
                protocolExtensions: [new TestExtension(), new TestExtension()]));

        Assert.Contains("Duplicate AppServer extension method registration", ex.Message);
    }

    private sealed class TestExtension : IAppServerProtocolExtension
    {
        public IReadOnlyCollection<string> Methods { get; } = ["test/echo"];

        public void ContributeCapabilities(AppServerCapabilityBuilder builder)
        {
            builder.SetExtension("testExtension", true);
        }

        public Task<object?> HandleAsync(AppServerIncomingMessage msg, AppServerExtensionContext context)
        {
            var payload = JsonSerializer.Deserialize<TestParams>(
                msg.Params.HasValue ? msg.Params.Value.GetRawText() : "{}",
                SessionWireJsonOptions.Default) ?? new TestParams();
            return Task.FromResult<object?>(new { echo = payload.Value });
        }
    }

    private sealed class TestParams
    {
        public string? Value { get; set; }
    }
}
