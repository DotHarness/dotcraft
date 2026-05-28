using DotCraft.Protocol.AppServer;
using DotCraft.Tracing;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerNodeReplTests
{
    [Fact]
    public async Task ThreadStart_WithNodeReplAndBrowserUseCapabilities_BindsThreadAndRefreshesAgent()
    {
        var proxy = new WireNodeReplProxy();
        using var harness = new AppServerTestHarness(wireNodeReplProxy: proxy);
        await harness.InitializeAsync(nodeReplBrowserUse: true);

        var msg = harness.BuildRequest(AppServerMethods.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = harness.Identity.WorkspacePath }
        });
        await harness.ExecuteRequestAsync(msg);

        var response = await harness.Transport.ReadNextSentAsync();
        var threadId = response.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;

        Assert.Contains(threadId, harness.Service.RefreshedThreadAgents);
        AssertThreadNodeReplAvailable(proxy, threadId);
    }

    [Fact]
    public async Task ThreadResume_WithNodeReplAndBrowserUseCapabilities_BindsExistingThreadAndRefreshesAgent()
    {
        var proxy = new WireNodeReplProxy();
        using var harness = new AppServerTestHarness(wireNodeReplProxy: proxy);
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        await harness.InitializeAsync(nodeReplBrowserUse: true);

        var msg = harness.BuildRequest(AppServerMethods.ThreadResume, new { threadId = thread.Id });
        await harness.ExecuteRequestAsync(msg);

        Assert.Contains(thread.Id, harness.Service.RefreshedThreadAgents);
        AssertThreadNodeReplAvailable(proxy, thread.Id);
    }

    [Fact]
    public async Task Initialize_WithBrowserUseBackends_PreservesLegacyBackendAndBackendsList()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(nodeReplBrowserUse: true, browserUseBackends: ["desktop-iab", "chrome-extension"]);

        Assert.Equal("desktop-iab", harness.Connection.BrowserUse?.Backend);
        Assert.Equal(["desktop-iab", "chrome-extension"], harness.Connection.BrowserUseBackends);
    }

    [Fact]
    public async Task Initialize_WithBrowserUseCapabilities_PreservesChromeRuntimeMetadata()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(nodeReplBrowserUse: true, browserUseBackends: ["desktop-iab", "chrome-extension"]);

        Assert.Equal(1, harness.Connection.BrowserUse?.BrowserSessionProtocolVersion);
        Assert.True(harness.Connection.BrowserUse?.SupportsCommandCancel);
        Assert.Equal(1048576, harness.Connection.BrowserUse?.MaxBrowserResultBytes);
        Assert.Equal(15000, harness.Connection.BrowserUse?.DefaultCommandTimeoutMs);
        Assert.Equal(120000, harness.Connection.BrowserUse?.MaxCommandTimeoutMs);
        Assert.True(harness.Connection.BrowserUse?.SupportsTypedFinalize);
        Assert.True(harness.Connection.BrowserUse?.SupportsChromeDiagnostics);
    }

    private static void AssertThreadNodeReplAvailable(WireNodeReplProxy proxy, string threadId)
    {
        var previous = TracingChatClient.CurrentSessionKey;
        try
        {
            TracingChatClient.CurrentSessionKey = threadId;
            Assert.True(proxy.IsAvailable);
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = previous;
        }
    }
}
