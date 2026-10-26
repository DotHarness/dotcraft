using DotCraft.Configuration;
using DotCraft.Skills;
using DotCraft.AppServer;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerHandshakeTests : IDisposable
{
    private readonly AppServerTestHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Initialize_ReturnsServerInfoAndCapabilitiesAndRecordsTheClient()
    {
        using var initDoc = await _h.InitializeAsync(
            approvalSupport: true,
            streamingSupport: false,
            toolExecutionLifecycle: true,
            backgroundTerminals: true);

        AppServerTestHarness.AssertIsSuccessResponse(initDoc);
        var result = initDoc.RootElement.GetProperty("result");
        var serverInfo = result.GetProperty("serverInfo");
        Assert.Equal("dotcraft", serverInfo.GetProperty("name").GetString());
        Assert.Equal("1", serverInfo.GetProperty("protocolVersion").GetString());

        var caps = result.GetProperty("capabilities");
        Assert.True(caps.GetProperty("threadManagement").GetBoolean());
        Assert.True(caps.GetProperty("threadSubscriptions").GetBoolean());
        Assert.True(caps.GetProperty("approvalFlow").GetBoolean());
        Assert.True(caps.GetProperty("requestUserInput").GetBoolean());
        Assert.True(caps.GetProperty("modeSwitch").GetBoolean());
        Assert.True(caps.GetProperty("configOverride").GetBoolean());
        Assert.True(caps.GetProperty("memoryManagement").GetBoolean());
        Assert.True(caps.GetProperty("threadMaintenanceInterrupt").GetBoolean());
        Assert.False(caps.TryGetProperty("turnQueueReorder", out _));
        Assert.False(caps.TryGetProperty("dynamicToolRebind", out _));

        Assert.True(_h.Connection.IsInitialized);
        Assert.Equal("test-client", _h.Connection.ClientInfo?.Name);
        Assert.True(_h.Connection.SupportsApproval);
        Assert.False(_h.Connection.SupportsStreaming);
        Assert.True(_h.Connection.SupportsToolExecutionLifecycle);
        Assert.True(_h.Connection.SupportsBackgroundTerminals);
    }

    [Fact]
    public async Task Initialize_AdvertisesCapabilitiesOfRegisteredProviders()
    {
        using var harness = new AppServerTestHarness(
            wireDynamicToolProxy: new WireDynamicToolProxy(),
            wireRuntimeAdditionalContextProvider: new WireRuntimeAdditionalContextProvider());

        using var initDoc = await harness.InitializeAsync();
        var caps = initDoc.RootElement.GetProperty("result").GetProperty("capabilities");

        Assert.True(caps.GetProperty("dynamicToolRebind").GetBoolean());
        Assert.True(caps.GetProperty("runtimeAdditionalContext").GetBoolean());
    }

    [Fact]
    public async Task InitializedNotification_IsRequiredBeforeRequestsAreServed()
    {
        var initMsg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.Initialize, new
        {
            clientInfo = new { name = "test-client", version = "0.0.1" }
        });
        var result = await _h.Handler.HandleRequestAsync(initMsg, default);
        if (result != null)
            await _h.Transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(initMsg.Id, result));
        _h.Transport.TryReadSent();

        Assert.True(_h.Connection.IsInitialized);
        Assert.False(_h.Connection.IsClientReady);

        var listMsg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadList, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath }
        });
        await _h.ExecuteRequestAsync(listMsg);
        using (var notReady = await _h.Transport.ReadNextSentAsync())
            AppServerTestHarness.AssertIsErrorResponse(notReady, AppServerErrors.InvalidRequestCode);

        _h.Handler.HandleInitializedNotification();
        Assert.True(_h.Connection.IsClientReady);

        await _h.ExecuteRequestAsync(listMsg);
        using var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        Assert.True(doc.RootElement.GetProperty("result").TryGetProperty("data", out _));
    }
}
