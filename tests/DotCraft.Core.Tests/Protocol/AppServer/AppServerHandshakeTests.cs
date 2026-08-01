using DotCraft.Configuration;
using DotCraft.Protocol.AppServer;
using DotCraft.Skills;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Tests for the initialize / initialized handshake (spec Section 3).
/// </summary>
public sealed class AppServerHandshakeTests : IDisposable
{
    private readonly AppServerTestHarness _h = new();

    public void Dispose() => _h.Dispose();

    // -------------------------------------------------------------------------
    // initialize response shape
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Initialize_ReturnsServerInfo()
    {
        var initDoc = await _h.InitializeAsync();

        AppServerTestHarness.AssertIsSuccessResponse(initDoc);
        var result = initDoc.RootElement.GetProperty("result");
        var serverInfo = result.GetProperty("serverInfo");
        Assert.Equal("dotcraft", serverInfo.GetProperty("name").GetString());
        Assert.Equal("1", serverInfo.GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public async Task Initialize_ReturnsAllCapabilities()
    {
        var initDoc = await _h.InitializeAsync();

        var caps = initDoc.RootElement
            .GetProperty("result")
            .GetProperty("capabilities");

        Assert.True(caps.GetProperty("threadManagement").GetBoolean());
        Assert.True(caps.GetProperty("threadSubscriptions").GetBoolean());
        Assert.True(caps.GetProperty("approvalFlow").GetBoolean());
        Assert.True(caps.GetProperty("requestUserInput").GetBoolean());
        Assert.True(caps.GetProperty("modeSwitch").GetBoolean());
        Assert.True(caps.GetProperty("configOverride").GetBoolean());
        Assert.True(caps.GetProperty("memoryManagement").GetBoolean());
        Assert.True(caps.GetProperty("manualMemoryConsolidation").GetBoolean());
        Assert.True(caps.GetProperty("threadMaintenanceInterrupt").GetBoolean());
        Assert.False(caps.TryGetProperty("turnQueueReorder", out _));
    }

    [Fact]
    public async Task Initialize_AdvertisesDynamicToolRebind_WhenDynamicToolProxyRegistered()
    {
        using var harness = new AppServerTestHarness(wireDynamicToolProxy: new WireDynamicToolProxy());

        var initDoc = await harness.InitializeAsync();
        var caps = initDoc.RootElement
            .GetProperty("result")
            .GetProperty("capabilities");

        Assert.True(caps.GetProperty("dynamicToolRebind").GetBoolean());
    }

    [Fact]
    public async Task Initialize_AdvertisesRuntimeAdditionalContext_WhenProviderRegistered()
    {
        using var harness = new AppServerTestHarness(wireRuntimeAdditionalContextProvider: new WireRuntimeAdditionalContextProvider());

        var initDoc = await harness.InitializeAsync();
        var caps = initDoc.RootElement
            .GetProperty("result")
            .GetProperty("capabilities");

        Assert.True(caps.GetProperty("runtimeAdditionalContext").GetBoolean());
    }

    [Fact]
    public async Task Initialize_AdvertisesSkillVariants_WhenVariantModeEnabled()
    {
        var craftPath = Path.Combine(Path.GetTempPath(), "dotcraft-appserver-variant-caps", Guid.NewGuid().ToString("N"), ".craft");
        try
        {
            Directory.CreateDirectory(craftPath);
            using var harness = new AppServerTestHarness(
                workspaceCraftPath: craftPath,
                skillsLoader: new SkillsLoader(craftPath));

            var initDoc = await harness.InitializeAsync();
            var caps = initDoc.RootElement
                .GetProperty("result")
                .GetProperty("capabilities");

            Assert.True(caps.GetProperty("skillsManagement").GetBoolean());
            Assert.True(caps.GetProperty("skillVariants").GetBoolean());
        }
        finally
        {
            var root = Directory.GetParent(craftPath)?.FullName;
            if (root != null && Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Initialize_DoesNotAdvertiseSkillVariants_WhenVariantModeDisabled()
    {
        var craftPath = Path.Combine(Path.GetTempPath(), "dotcraft-appserver-variant-caps", Guid.NewGuid().ToString("N"), ".craft");
        try
        {
            Directory.CreateDirectory(craftPath);
            var config = new AppConfig();
            config.Skills.SelfLearning.VariantMode = "disabled";
            using var harness = new AppServerTestHarness(
                workspaceCraftPath: craftPath,
                appConfigMonitor: new AppConfigMonitor(config),
                skillsLoader: new SkillsLoader(craftPath));

            var initDoc = await harness.InitializeAsync();
            var caps = initDoc.RootElement
                .GetProperty("result")
                .GetProperty("capabilities");

            Assert.True(caps.GetProperty("skillsManagement").GetBoolean());
            Assert.False(caps.TryGetProperty("skillVariants", out var skillVariants)
                && skillVariants.GetBoolean());
        }
        finally
        {
            var root = Directory.GetParent(craftPath)?.FullName;
            if (root != null && Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Initialize_SetsClientInfo_OnConnection()
    {
        await _h.InitializeAsync();

        Assert.True(_h.Connection.IsInitialized);
        Assert.Equal("test-client", _h.Connection.ClientInfo?.Name);
    }

    [Fact]
    public async Task Initialize_SetsClientCapabilities_OnConnection()
    {
        await _h.InitializeAsync(
            approvalSupport: true,
            streamingSupport: false,
            toolExecutionLifecycle: true,
            backgroundTerminals: true);

        Assert.True(_h.Connection.SupportsApproval);
        Assert.False(_h.Connection.SupportsStreaming);
        Assert.True(_h.Connection.SupportsToolExecutionLifecycle);
        Assert.True(_h.Connection.SupportsBackgroundTerminals);
    }

    // -------------------------------------------------------------------------
    // AlreadyInitialized error (spec Section 8.3 — code -32003)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Initialize_CalledTwice_ReturnsAlreadyInitializedError()
    {
        await _h.InitializeAsync();

        // Second call should produce AlreadyInitialized (-32003)
        var secondInit = _h.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.Initialize, new
        {
            clientInfo = new { name = "test-client", version = "0.0.1" }
        });
        await _h.ExecuteRequestAsync(secondInit);

        var errorDoc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(errorDoc, AppServerErrors.AlreadyInitializedCode);
    }

    // -------------------------------------------------------------------------
    // NotInitialized guard (spec Section 3.1 — code -32002)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadList_BeforeInitialize_ReturnsNotInitialized()
    {
        var msg = _h.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ThreadList, new
        {
            identity = new { channelName = "test", workspacePath = _h.Identity.WorkspacePath }
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.NotInitializedCode);
    }

    [Fact]
    public async Task TurnStart_BeforeInitialize_ReturnsNotInitialized()
    {
        var msg = _h.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = "thread_fake",
            input = new[] { new { type = "text", text = "hello" } }
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.NotInitializedCode);
    }

    // -------------------------------------------------------------------------
    // Not-ready guard: after initialize, before initialized notification
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadList_AfterInitializeBeforeInitializedNotif_ReturnsNotReady()
    {
        // Send initialize but NOT the initialized notification
        var initMsg = _h.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.Initialize, new
        {
            clientInfo = new { name = "test-client", version = "0.0.1" }
        });
        var result = await _h.Handler.HandleRequestAsync(initMsg, default);
        // Write response inline so the init works
        if (result != null)
            await _h.Transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(initMsg.Id, result));
        _h.Transport.TryReadSent(); // drain init response

        // Connection is initialized but NOT ready (initialized notification not sent)
        Assert.True(_h.Connection.IsInitialized);
        Assert.False(_h.Connection.IsClientReady);

        var listMsg = _h.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ThreadList, new
        {
            identity = new { channelName = "test", workspacePath = _h.Identity.WorkspacePath }
        });
        await _h.ExecuteRequestAsync(listMsg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.InvalidRequestCode);
    }

    // -------------------------------------------------------------------------
    // initialized notification unlocks the connection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InitializedNotification_SetsClientReady()
    {
        var initMsg = _h.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.Initialize, new
        {
            clientInfo = new { name = "test-client", version = "0.0.1" }
        });
        var result = await _h.Handler.HandleRequestAsync(initMsg, default);
        if (result != null)
            await _h.Transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(initMsg.Id, result));
        _h.Transport.TryReadSent();

        Assert.False(_h.Connection.IsClientReady);
        _h.Handler.HandleInitializedNotification();
        Assert.True(_h.Connection.IsClientReady);
    }

    [Fact]
    public async Task ThreadList_AfterFullHandshake_Succeeds()
    {
        await _h.InitializeAsync();

        var msg = _h.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ThreadList, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath }
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        var result = doc.RootElement.GetProperty("result");
        Assert.True(result.TryGetProperty("data", out _));
    }
}
