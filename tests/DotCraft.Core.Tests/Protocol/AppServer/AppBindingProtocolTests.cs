using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Abstractions;
using DotCraft.AppBinding;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Memory;
using DotCraft.Plugins;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Security;
using DotCraft.Skills;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppBindingProtocolTests : IDisposable
{
    private const string AppList = "app/list";
    private const string AppConnectionStart = "app/connection/start";
    private const string AppConnectionRequestGet = "app/connection/request/get";
    private const string AppConnectionConnect = "app/connection/connect";
    private const string AppConnectionStatus = "app/connection/status";
    private const string AppConnectionRefreshMetadata = "app/connection/refreshMetadata";
    private const string AppConnectionRevoke = "app/connection/revoke";
    private const string AppBindingRequestCreate = "app/binding/request/create";
    private const string AppBindingRequestGet = "app/binding/request/get";
    private const string AppBindingRequestCancel = "app/binding/request/cancel";
    private const string AppBindingAccept = "app/binding/accept";
    private const string AppBindingAttachTools = "app/binding/attachTools";
    private const string AppBindingContextUpsert = "app/binding/context/upsert";
    private const string AppBindingContextRemove = "app/binding/context/remove";
    private const string AppThreadInputEnqueue = "app/threadInput/enqueue";
    private const string ThreadAppBindingsList = "thread/appBindings/list";
    private const string ThreadAppBindingsRefresh = "thread/appBindings/refresh";
    private const string ThreadAppBindingsRevoke = "thread/appBindings/revoke";
    private const string ThreadAppContextBlocksList = "thread/appContextBlocks/list";
    private const string AppListUpdated = "app/list/updated";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"app_binding_{Guid.NewGuid():N}");
    private readonly string _workspaceCraftPath;

    public AppBindingProtocolTests()
    {
        _workspaceCraftPath = Path.Combine(_tempRoot, ".craft");
        Directory.CreateDirectory(_workspaceCraftPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task Initialize_ReportsAppBindingCapabilityAndListReturnsPluginApp()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);

        using var init = await harness.InitializeAsync();
        Assert.True(init.RootElement
            .GetProperty("result")
            .GetProperty("capabilities")
            .GetProperty("appBinding")
            .GetBoolean());
        Assert.True(init.RootElement
            .GetProperty("result")
            .GetProperty("capabilities")
            .GetProperty("appContextBlocks")
            .GetBoolean());
        Assert.True(init.RootElement
            .GetProperty("result")
            .GetProperty("capabilities")
            .GetProperty("appThreadInputEnqueue")
            .GetBoolean());

        using var response = await ExecuteAndReadResponseAsync(harness, AppList, new { includeDisabled = true });

        AppServerTestHarness.AssertIsSuccessResponse(response);
        var app = Assert.Single(
            response.RootElement.GetProperty("result").GetProperty("apps").EnumerateArray(),
            item => item.GetProperty("appId").GetString() == "com.dotharness.oratorio");
        Assert.Equal("com.dotharness.oratorio", app.GetProperty("appId").GetString());
        Assert.Equal("oratorio", app.GetProperty("toolNamespace").GetString());
        Assert.True(app.GetProperty("installed").GetBoolean());
        Assert.True(app.GetProperty("enabled").GetBoolean());
        Assert.Equal("notConnected", app.GetProperty("connectionState").GetString());
    }

    [Fact]
    public async Task ContextBlocks_UpsertListPromptAndRemoveWithoutCreatingTurns()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        await ConnectAppAsync(harness);
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        var request = await CreateBindingRequestAsync(harness, thread.Id);
        var bindingId = await AcceptBindingAsync(harness, request.BindingRequestId, request.Token);

        using var upsertResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingContextUpsert,
            new
            {
                bindingId,
                appId = "com.dotharness.oratorio",
                grantId = "grant-1",
                blockId = "role",
                kind = "role",
                title = "Reviewer role",
                content = "Prefer concise review notes.",
                order = 10,
                version = "v1"
            });

        AppServerTestHarness.AssertIsSuccessResponse(upsertResponse);
        var block = upsertResponse.RootElement.GetProperty("result").GetProperty("block");
        Assert.Equal(thread.Id, block.GetProperty("threadId").GetString());
        Assert.Equal(bindingId, block.GetProperty("bindingId").GetString());
        Assert.Equal("role", block.GetProperty("kind").GetString());
        Assert.Equal("model", block.GetProperty("visibility").GetString());
        Assert.True(block.GetProperty("active").GetBoolean());

        using var hiddenUpsertResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingContextUpsert,
            new
            {
                bindingId,
                appId = "com.dotharness.oratorio",
                grantId = "grant-1",
                blockId = "debug",
                kind = "teamState",
                title = "Debug state",
                content = "hidden debug state",
                order = 20,
                version = "v1",
                visibility = "hiddenFromModel"
            });
        AppServerTestHarness.AssertIsSuccessResponse(hiddenUpsertResponse);
        Assert.False(hiddenUpsertResponse.RootElement.GetProperty("result").GetProperty("block").GetProperty("active").GetBoolean());

        using var expiredUpsertResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingContextUpsert,
            new
            {
                bindingId,
                appId = "com.dotharness.oratorio",
                grantId = "grant-1",
                blockId = "expired",
                kind = "mailboxDigest",
                title = "Expired digest",
                content = "stale digest",
                order = 30,
                version = "v1",
                expiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            });
        AppServerTestHarness.AssertIsSuccessResponse(expiredUpsertResponse);
        Assert.False(expiredUpsertResponse.RootElement.GetProperty("result").GetProperty("block").GetProperty("active").GetBoolean());

        using var listResponse = await ExecuteAndReadResponseAsync(
            harness,
            ThreadAppContextBlocksList,
            new { threadId = thread.Id });
        AppServerTestHarness.AssertIsSuccessResponse(listResponse);
        var listed = Assert.Single(listResponse.RootElement.GetProperty("result").GetProperty("blocks").EnumerateArray());
        Assert.Equal("Prefer concise review notes.", listed.GetProperty("content").GetString());

        using var includeInactiveResponse = await ExecuteAndReadResponseAsync(
            harness,
            ThreadAppContextBlocksList,
            new { threadId = thread.Id, includeInactive = true });
        AppServerTestHarness.AssertIsSuccessResponse(includeInactiveResponse);
        Assert.Equal(3, includeInactiveResponse.RootElement.GetProperty("result").GetProperty("blocks").GetArrayLength());

        var promptProvider = new AppBindingThreadSystemPromptContextProvider(service);
        var section = promptProvider.GetSystemPromptSection(new ThreadSystemPromptContext(thread.Id, _tempRoot));
        Assert.NotNull(section);
        Assert.Contains("# App Context", section, StringComparison.Ordinal);
        Assert.Contains("<app-context>", section, StringComparison.Ordinal);
        Assert.Contains("</app-context>", section, StringComparison.Ordinal);
        Assert.DoesNotContain("<app-provided-context>", section, StringComparison.Ordinal);
        Assert.Contains("Prefer concise review notes.", section, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden debug state", section, StringComparison.Ordinal);
        Assert.DoesNotContain("stale digest", section, StringComparison.Ordinal);

        var reloaded = await harness.Service.GetThreadAsync(thread.Id);
        Assert.Empty(reloaded.Turns);
        AssertAppBindingAuditContains("binding.context.upsert");

        using var removeResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingContextRemove,
            new
            {
                bindingId,
                appId = "com.dotharness.oratorio",
                grantId = "grant-1",
                blockId = "role"
            });
        AppServerTestHarness.AssertIsSuccessResponse(removeResponse);
        Assert.True(removeResponse.RootElement.GetProperty("result").GetProperty("removed").GetBoolean());

        using var removeUnknownResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingContextRemove,
            new
            {
                bindingId,
                appId = "com.dotharness.oratorio",
                grantId = "grant-1",
                blockId = "role"
            });
        AppServerTestHarness.AssertIsErrorResponse(removeUnknownResponse, AppServerErrors.InvalidParamsCode);
        Assert.Contains(
            "was not found",
            removeUnknownResponse.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());

        using var emptyListResponse = await ExecuteAndReadResponseAsync(
            harness,
            ThreadAppContextBlocksList,
            new { threadId = thread.Id });
        AppServerTestHarness.AssertIsSuccessResponse(emptyListResponse);
        Assert.Empty(emptyListResponse.RootElement.GetProperty("result").GetProperty("blocks").EnumerateArray());
        AssertAppBindingAuditContains("binding.context.remove");
    }

    [Fact]
    public async Task ContextBlocks_RejectWrongGrantAndRevokedBinding()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        await ConnectAppAsync(harness);
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        var request = await CreateBindingRequestAsync(harness, thread.Id);
        var bindingId = await AcceptBindingAsync(harness, request.BindingRequestId, request.Token);
        var expiredThread = await harness.Service.CreateThreadAsync(CreateIdentity());
        var expiredRequest = await CreateBindingRequestAsync(harness, expiredThread.Id);
        var expiredBindingId = await AcceptBindingAsync(
            harness,
            expiredRequest.BindingRequestId,
            expiredRequest.Token,
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        using var wrongGrantResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingContextUpsert,
            new
            {
                bindingId,
                appId = "com.dotharness.oratorio",
                grantId = "wrong-grant",
                blockId = "role",
                kind = "role",
                title = "Reviewer role",
                content = "Prefer concise review notes.",
                order = 10,
                version = "v1"
            });

        AppServerTestHarness.AssertIsErrorResponse(wrongGrantResponse, AppServerErrors.InvalidParamsCode);

        using var revokeResponse = await ExecuteAndReadResponseAsync(
            harness,
            ThreadAppBindingsRevoke,
            new
            {
                threadId = thread.Id,
                bindingId,
                reason = "test revoke"
            },
            expectedNotificationMethod: "thread/appBindings/changed");
        AppServerTestHarness.AssertIsSuccessResponse(revokeResponse);

        using var revokedResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingContextUpsert,
            new
            {
                bindingId,
                appId = "com.dotharness.oratorio",
                grantId = "grant-1",
                blockId = "role",
                kind = "role",
                title = "Reviewer role",
                content = "Prefer concise review notes.",
                order = 10,
                version = "v1"
            });
        AppServerTestHarness.AssertIsErrorResponse(revokedResponse, AppServerErrors.InvalidParamsCode);
        Assert.Contains(
            "is not active",
            revokedResponse.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());

        using var expiredResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingContextUpsert,
            new
            {
                bindingId = expiredBindingId,
                appId = "com.dotharness.oratorio",
                grantId = "grant-1",
                blockId = "role",
                kind = "role",
                title = "Reviewer role",
                content = "Prefer concise review notes.",
                order = 10,
                version = "v1"
            });
        AppServerTestHarness.AssertIsErrorResponse(expiredResponse, AppServerErrors.InvalidParamsCode);
        Assert.Contains(
            "has expired",
            expiredResponse.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ContextBlocks_UpsertReleasesCachedAppContextPromptPage()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        var manager = new ContextPageManager();
        using var harness = CreateHarness(service, contextPageManager: manager);
        await harness.InitializeAsync();
        await ConnectAppAsync(harness);
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        var request = await CreateBindingRequestAsync(harness, thread.Id);
        var bindingId = await AcceptBindingAsync(harness, request.BindingRequestId, request.Token);
        var builder = new PromptBuilder(
            new MemoryStore(_workspaceCraftPath),
            new SkillsLoader(_workspaceCraftPath),
            _workspaceCraftPath,
            _tempRoot,
            toolNamesProvider: () => [],
            contextPageManager: manager,
            threadSystemPromptContextProviders: [new AppBindingThreadSystemPromptContextProvider(service)]);

        var emptyPrompt = builder.BuildSystemPrompt(thread.Id);
        Assert.DoesNotContain("context-from-app", emptyPrompt, StringComparison.Ordinal);

        using var upsertResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingContextUpsert,
            new
            {
                bindingId,
                appId = "com.dotharness.oratorio",
                grantId = "grant-1",
                blockId = "role",
                kind = "role",
                title = "Reviewer role",
                content = "context-from-app",
                order = 10,
                version = "v1"
            });
        AppServerTestHarness.AssertIsSuccessResponse(upsertResponse);

        var refreshedPrompt = builder.BuildSystemPrompt(thread.Id);
        Assert.Contains("context-from-app", refreshedPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThreadInputEnqueue_PersistsTeamTriggerMetadataAndAudit()
    {
        const string teamsAppId = "com.dotharness.dotcraft-teams";
        WriteOratorioPlugin(appId: teamsAppId, toolNamespace: "teams", rootName: "teams-app");
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        await ConnectAppAsync(harness, appId: teamsAppId, accountLabel: "teams-runtime");
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        var request = await CreateBindingRequestAsync(harness, thread.Id, appId: teamsAppId);
        var bindingId = await AcceptBindingAsync(harness, request.BindingRequestId, request.Token, approvedBy: "teams-runtime");

        using var wrongGrantResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppThreadInputEnqueue,
            new
            {
                bindingId,
                appId = teamsAppId,
                grantId = "wrong-grant",
                input = new[] { new { type = "text", text = "dispatch this" } }
            });
        AppServerTestHarness.AssertIsErrorResponse(wrongGrantResponse, AppServerErrors.InvalidParamsCode);

        using var response = await ExecuteAndReadResponseAsync(
            harness,
            AppThreadInputEnqueue,
            new
            {
                bindingId,
                appId = teamsAppId,
                grantId = "grant-1",
                input = new[] { new { type = "text", text = "dispatch this" } },
                displayText = "Dispatch task",
                triggerLabel = "Task: Explore",
                triggerRefId = "task_123",
                startPolicy = "queueOnly"
            });

        AppServerTestHarness.AssertIsSuccessResponse(response);
        var queued = response.RootElement.GetProperty("result").GetProperty("queuedInput");
        Assert.Equal(thread.Id, queued.GetProperty("threadId").GetString());
        Assert.Equal("Dispatch task", queued.GetProperty("displayText").GetString());
        Assert.Equal("team", queued.GetProperty("triggerKind").GetString());
        Assert.Equal("Task: Explore", queued.GetProperty("triggerLabel").GetString());
        Assert.Equal("task_123", queued.GetProperty("triggerRefId").GetString());

        var stored = await harness.Service.GetThreadAsync(thread.Id);
        var storedQueued = Assert.Single(stored.QueuedInputs);
        Assert.Equal("team", storedQueued.TriggerKind);
        Assert.Equal("Task: Explore", storedQueued.TriggerLabel);
        Assert.Equal("task_123", storedQueued.TriggerRefId);
        Assert.Empty(stored.Turns);
        AssertAppBindingAuditContains("binding.threadInput.enqueue");
    }

    [Fact]
    public async Task ThreadInputEnqueue_RunWhenIdleDoesNotInterruptActiveTurn()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        await ConnectAppAsync(harness);

        var busyThread = await harness.Service.CreateThreadAsync(CreateIdentity());
        busyThread.Turns.Add(new SessionTurn
        {
            Id = "turn_running",
            ThreadId = busyThread.Id,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });
        var busyRequest = await CreateBindingRequestAsync(harness, busyThread.Id);
        var busyBindingId = await AcceptBindingAsync(harness, busyRequest.BindingRequestId, busyRequest.Token);

        using var busyResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppThreadInputEnqueue,
            new
            {
                bindingId = busyBindingId,
                appId = "com.dotharness.oratorio",
                grantId = "grant-1",
                input = new[] { new { type = "text", text = "wait for idle" } },
                startPolicy = "runWhenIdle"
            });
        AppServerTestHarness.AssertIsSuccessResponse(busyResponse);
        Assert.Single(busyResponse.RootElement.GetProperty("result").GetProperty("queuedInputs").EnumerateArray());
        Assert.Empty(harness.Service.LastSubmittedContent);
        Assert.Single((await harness.Service.GetThreadAsync(busyThread.Id)).QueuedInputs);

        var idleThread = await harness.Service.CreateThreadAsync(CreateIdentity());
        var idleRequest = await CreateBindingRequestAsync(harness, idleThread.Id);
        var idleBindingId = await AcceptBindingAsync(harness, idleRequest.BindingRequestId, idleRequest.Token);

        using var idleResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppThreadInputEnqueue,
            new
            {
                bindingId = idleBindingId,
                appId = "com.dotharness.oratorio",
                grantId = "grant-1",
                input = new[] { new { type = "text", text = "start while idle" } },
                startPolicy = "runWhenIdle"
            });
        AppServerTestHarness.AssertIsSuccessResponse(idleResponse);
        Assert.Empty(idleResponse.RootElement.GetProperty("result").GetProperty("queuedInputs").EnumerateArray());
        Assert.Empty((await harness.Service.GetThreadAsync(idleThread.Id)).QueuedInputs);
        Assert.Contains(
            harness.Service.LastSubmittedContent,
            content => content is TextContent text && text.Text.Contains("start while idle", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ThreadInputEnqueue_RejectsRevokedAndExpiredBindings()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        await ConnectAppAsync(harness);

        var revokedThread = await harness.Service.CreateThreadAsync(CreateIdentity());
        var revokedRequest = await CreateBindingRequestAsync(harness, revokedThread.Id);
        var revokedBindingId = await AcceptBindingAsync(harness, revokedRequest.BindingRequestId, revokedRequest.Token);
        var expiredThread = await harness.Service.CreateThreadAsync(CreateIdentity());
        var expiredRequest = await CreateBindingRequestAsync(harness, expiredThread.Id);
        var expiredBindingId = await AcceptBindingAsync(
            harness,
            expiredRequest.BindingRequestId,
            expiredRequest.Token,
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        using var revokeResponse = await ExecuteAndReadResponseAsync(
            harness,
            ThreadAppBindingsRevoke,
            new
            {
                threadId = revokedThread.Id,
                bindingId = revokedBindingId,
                reason = "test revoke"
            },
            expectedNotificationMethod: "thread/appBindings/changed");
        AppServerTestHarness.AssertIsSuccessResponse(revokeResponse);

        using var revokedResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppThreadInputEnqueue,
            new
            {
                bindingId = revokedBindingId,
                appId = "com.dotharness.oratorio",
                grantId = "grant-1",
                input = new[] { new { type = "text", text = "should not enqueue" } }
            });
        AppServerTestHarness.AssertIsErrorResponse(revokedResponse, AppServerErrors.InvalidParamsCode);
        Assert.Contains(
            "is not active",
            revokedResponse.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());

        using var expiredResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppThreadInputEnqueue,
            new
            {
                bindingId = expiredBindingId,
                appId = "com.dotharness.oratorio",
                grantId = "grant-1",
                input = new[] { new { type = "text", text = "should not enqueue" } }
            });
        AppServerTestHarness.AssertIsErrorResponse(expiredResponse, AppServerErrors.InvalidParamsCode);
        Assert.Contains(
            "has expired",
            expiredResponse.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());

        Assert.Empty((await harness.Service.GetThreadAsync(revokedThread.Id)).QueuedInputs);
        Assert.Empty((await harness.Service.GetThreadAsync(expiredThread.Id)).QueuedInputs);
    }

    [Fact]
    public async Task AppList_IncludesInstallableOratorioPluginAppCatalogEntry()
    {
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();

        using var response = await ExecuteAndReadResponseAsync(harness, AppList, new { includeCatalog = true });

        AppServerTestHarness.AssertIsSuccessResponse(response);
        var app = Assert.Single(
            response.RootElement.GetProperty("result").GetProperty("apps").EnumerateArray(),
            item => item.GetProperty("appId").GetString() == "com.dotharness.oratorio");
        Assert.Equal("com.dotharness.oratorio", app.GetProperty("appId").GetString());
        Assert.False(app.GetProperty("installed").GetBoolean());
        Assert.False(app.GetProperty("enabled").GetBoolean());
        Assert.Equal("oratorio", app.GetProperty("nativeApp").GetProperty("protocol").GetString());
        Assert.Equal("unknown", app.GetProperty("nativeApp").GetProperty("status").GetString());

        using var hiddenResponse = await ExecuteAndReadResponseAsync(harness, AppList, new { includeCatalog = false });
        AppServerTestHarness.AssertIsSuccessResponse(hiddenResponse);
        Assert.Empty(hiddenResponse.RootElement.GetProperty("result").GetProperty("apps").EnumerateArray());
    }

    [Fact]
    public async Task ConnectionStart_RejectsCatalogAppBeforeOwningPluginInstall()
    {
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();

        using var response = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionStart,
            new { appId = "com.dotharness.oratorio" });

        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
        Assert.Contains(
            "requires an installed and enabled plugin",
            response.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());
    }

    [Fact]
    public async Task PluginInstall_MakesBuiltInOratorioAppConnectable()
    {
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.PluginInstall, new { id = "oratorio" }));

        using var installResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(installResponse);
        Assert.True(installResponse.RootElement
            .GetProperty("result")
            .GetProperty("plugin")
            .GetProperty("installed")
            .GetBoolean());

        using var notification = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsNotification(notification, AppListUpdated);
        Assert.Contains(
            notification.RootElement.GetProperty("params").GetProperty("appIds").EnumerateArray(),
            appId => appId.GetString() == "com.dotharness.oratorio");

        using var listResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppList,
            new { includeCatalog = false });
        AppServerTestHarness.AssertIsSuccessResponse(listResponse);
        var app = Assert.Single(
            listResponse.RootElement.GetProperty("result").GetProperty("apps").EnumerateArray(),
            item => item.GetProperty("appId").GetString() == "com.dotharness.oratorio");
        Assert.True(app.GetProperty("installed").GetBoolean());
        Assert.True(app.GetProperty("enabled").GetBoolean());

        using var startResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionStart,
            new { appId = "com.dotharness.oratorio" },
            expectedNotificationMethod: "app/connection/changed");
        AppServerTestHarness.AssertIsSuccessResponse(startResponse);
    }

    [Fact]
    public async Task ConnectionRequestGet_ReturnsPendingRequestWithoutConsumingToken()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();

        using var startResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionStart,
            new { appId = "com.dotharness.oratorio" },
            expectedNotificationMethod: "app/connection/changed");
        var connectionRequestId = startResponse.RootElement.GetProperty("result").GetProperty("connectionRequestId").GetString()!;
        var token = ExtractToken(startResponse.RootElement
            .GetProperty("result")
            .GetProperty("handoff")
            .GetProperty("uri")
            .GetString()!);

        using var inspectResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionRequestGet,
            new
            {
                connectionRequestId,
                requestToken = token,
                appId = "com.dotharness.oratorio"
            });
        AppServerTestHarness.AssertIsSuccessResponse(inspectResponse);
        Assert.Equal("Oratorio", inspectResponse.RootElement.GetProperty("result").GetProperty("displayName").GetString());

        using var connectResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionConnect,
            new
            {
                connectionRequestId,
                requestToken = token,
                appId = "com.dotharness.oratorio",
                accountLabel = "local-oratorio"
            },
            expectedNotificationMethod: "app/connection/changed");
        AppServerTestHarness.AssertIsSuccessResponse(connectResponse);
    }

    [Fact]
    public async Task ConnectionConnect_ExposesOnlySafePublicMetadata()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();

        using var startResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionStart,
            new { appId = "com.dotharness.oratorio" },
            expectedNotificationMethod: "app/connection/changed");
        var connectionRequestId = startResponse.RootElement.GetProperty("result").GetProperty("connectionRequestId").GetString()!;
        var token = ExtractToken(startResponse.RootElement
            .GetProperty("result")
            .GetProperty("handoff")
            .GetProperty("uri")
            .GetString()!);

        using var connectResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionConnect,
            new
            {
                connectionRequestId,
                requestToken = token,
                appId = "com.dotharness.oratorio",
                accountLabel = "local-oratorio",
                connectionProof = new
                {
                    secret = "not returned"
                },
                publicMetadata = new
                {
                    displayName = "Oratorio Local",
                    ignored = "not returned",
                    surfaceEndpoints = new
                    {
                        boardApiBaseUrl = "http://127.0.0.1:5087/api/v1",
                        unsafeUrl = "https://example.com/private"
                    }
                }
            },
            expectedNotificationMethod: "app/connection/changed");
        AppServerTestHarness.AssertIsSuccessResponse(connectResponse);

        using var statusResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionStatus,
            new { appId = "com.dotharness.oratorio" });
        AppServerTestHarness.AssertIsSuccessResponse(statusResponse);
        var metadata = statusResponse.RootElement.GetProperty("result").GetProperty("publicMetadata");
        Assert.Equal("Oratorio Local", metadata.GetProperty("displayName").GetString());
        Assert.Equal(
            "http://127.0.0.1:5087/api/v1",
            metadata.GetProperty("surfaceEndpoints").GetProperty("boardApiBaseUrl").GetString());
        Assert.False(metadata.TryGetProperty("ignored", out _));
        Assert.False(metadata.GetProperty("surfaceEndpoints").TryGetProperty("unsafeUrl", out _));
        Assert.False(statusResponse.RootElement.GetProperty("result").TryGetProperty("connectionProof", out _));

        using var revokeResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionRevoke,
            new { appId = "com.dotharness.oratorio", reason = "disconnect" },
            expectedNotificationMethod: "app/connection/changed");
        AppServerTestHarness.AssertIsSuccessResponse(revokeResponse);
        Assert.Equal("notConnected", revokeResponse.RootElement.GetProperty("result").GetProperty("state").GetString());
        Assert.False(revokeResponse.RootElement.GetProperty("result").TryGetProperty("publicMetadata", out _));

        using var revokedStatusResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionStatus,
            new { appId = "com.dotharness.oratorio" });
        AppServerTestHarness.AssertIsSuccessResponse(revokedStatusResponse);
        Assert.Equal("notConnected", revokedStatusResponse.RootElement.GetProperty("result").GetProperty("state").GetString());
        Assert.False(revokedStatusResponse.RootElement.GetProperty("result").TryGetProperty("publicMetadata", out _));
    }

    [Fact]
    public async Task ConnectionRefreshMetadata_UpdatesLoopbackEndpointWithMatchingProof()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();

        object Proof() => new { appId = "com.dotharness.oratorio", workspaceLabel = "ws", mode = "deepLink" };

        using var startResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionStart,
            new { appId = "com.dotharness.oratorio" },
            expectedNotificationMethod: "app/connection/changed");
        var connectionRequestId = startResponse.RootElement.GetProperty("result").GetProperty("connectionRequestId").GetString()!;
        var token = ExtractToken(startResponse.RootElement
            .GetProperty("result").GetProperty("handoff").GetProperty("uri").GetString()!);

        using var connectResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionConnect,
            new
            {
                connectionRequestId,
                requestToken = token,
                appId = "com.dotharness.oratorio",
                accountLabel = "local-oratorio",
                connectionProof = Proof(),
                publicMetadata = new { surfaceEndpoints = new { apiBase = "http://127.0.0.1:5087/api/v1" } }
            },
            expectedNotificationMethod: "app/connection/changed");
        AppServerTestHarness.AssertIsSuccessResponse(connectResponse);

        // Re-announce a new dynamic loopback port using the same app-owned proof.
        using var refreshResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionRefreshMetadata,
            new
            {
                appId = "com.dotharness.oratorio",
                connectionProof = Proof(),
                publicMetadata = new
                {
                    surfaceEndpoints = new
                    {
                        apiBase = "http://127.0.0.1:49555/api/v1",
                        unsafeUrl = "https://example.com/private"
                    }
                }
            });
        AppServerTestHarness.AssertIsSuccessResponse(refreshResponse);
        Assert.Equal("connected", refreshResponse.RootElement.GetProperty("result").GetProperty("state").GetString());

        using var statusResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionStatus,
            new { appId = "com.dotharness.oratorio" });
        var endpoints = statusResponse.RootElement.GetProperty("result").GetProperty("publicMetadata").GetProperty("surfaceEndpoints");
        Assert.Equal("http://127.0.0.1:49555/api/v1", endpoints.GetProperty("apiBase").GetString());
        Assert.False(endpoints.TryGetProperty("unsafeUrl", out _));

        // A mismatched proof must be rejected and must not mutate stored metadata.
        using var wrongProofResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionRefreshMetadata,
            new
            {
                appId = "com.dotharness.oratorio",
                connectionProof = new { appId = "com.dotharness.oratorio", workspaceLabel = "ws", mode = "forged" },
                publicMetadata = new { surfaceEndpoints = new { apiBase = "http://127.0.0.1:60000/api/v1" } }
            });
        AppServerTestHarness.AssertIsErrorResponse(wrongProofResponse, AppServerErrors.InvalidParamsCode);

        using var unchangedStatus = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionStatus,
            new { appId = "com.dotharness.oratorio" });
        Assert.Equal(
            "http://127.0.0.1:49555/api/v1",
            unchangedStatus.RootElement.GetProperty("result").GetProperty("publicMetadata")
                .GetProperty("surfaceEndpoints").GetProperty("apiBase").GetString());
    }

    [Fact]
    public async Task ConnectionRefreshMetadata_RejectsWhenNotConnected()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();

        using var response = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionRefreshMetadata,
            new
            {
                appId = "com.dotharness.oratorio",
                connectionProof = new { appId = "com.dotharness.oratorio" },
                publicMetadata = new { surfaceEndpoints = new { apiBase = "http://127.0.0.1:5087/api/v1" } }
            });
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task ConnectionStart_StatusAndAppListReportConnecting()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();

        using var startResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionStart,
            new { appId = "com.dotharness.oratorio" },
            expectedNotificationMethod: "app/connection/changed");
        AppServerTestHarness.AssertIsSuccessResponse(startResponse);

        using var statusResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionStatus,
            new { appId = "com.dotharness.oratorio" });
        AppServerTestHarness.AssertIsSuccessResponse(statusResponse);
        Assert.Equal("connecting", statusResponse.RootElement.GetProperty("result").GetProperty("state").GetString());

        using var listResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppList,
            new { includeDisabled = true });
        var app = Assert.Single(
            listResponse.RootElement.GetProperty("result").GetProperty("apps").EnumerateArray(),
            item => item.GetProperty("appId").GetString() == "com.dotharness.oratorio");
        Assert.Equal("connecting", app.GetProperty("connectionState").GetString());
    }

    [Fact]
    public async Task BindingRequest_ListIncludesPendingAndCancelRemovesIt()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        await ConnectAppAsync(harness);

        var request = await CreateBindingRequestAsync(harness, thread.Id);
        using var inspectResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingRequestGet,
            new
            {
                appId = "com.dotharness.oratorio",
                bindingRequestId = request.BindingRequestId,
                requestToken = request.Token
            });
        AppServerTestHarness.AssertIsSuccessResponse(inspectResponse);
        var inspect = inspectResponse.RootElement.GetProperty("result");
        Assert.Equal(thread.Id, inspect.GetProperty("threadId").GetString());
        Assert.Equal("threadMenu", inspect.GetProperty("source").GetString());
        Assert.Contains(
            inspect.GetProperty("scopeCatalog").EnumerateArray(),
            scope => scope.GetProperty("id").GetString() == "board.read");

        using var pendingResponse = await ExecuteAndReadResponseAsync(
            harness,
            ThreadAppBindingsList,
            new { threadId = thread.Id });
        AppServerTestHarness.AssertIsSuccessResponse(pendingResponse);
        var pending = Assert.Single(pendingResponse.RootElement.GetProperty("result").GetProperty("bindings").EnumerateArray());
        Assert.Equal("pending", pending.GetProperty("state").GetString());
        Assert.Equal(request.BindingRequestId, pending.GetProperty("bindingRequestId").GetString());

        using var cancelResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingRequestCancel,
            new { bindingRequestId = request.BindingRequestId, reason = "changed mind" },
            expectedNotificationMethod: "thread/appBindings/changed");
        AppServerTestHarness.AssertIsSuccessResponse(cancelResponse);

        using var emptyResponse = await ExecuteAndReadResponseAsync(
            harness,
            ThreadAppBindingsList,
            new { threadId = thread.Id });
        Assert.Empty(emptyResponse.RootElement.GetProperty("result").GetProperty("bindings").EnumerateArray());
    }

    [Fact]
    public void CatalogDiscovery_RejectsInvalidAppIdsAndDuplicateNamespaces()
    {
        WriteOratorioPlugin();
        WriteOratorioPlugin(
            pluginId: "bad-app",
            appId: "Not.ReverseDns",
            toolNamespace: "badnamespace",
            rootName: "bad-app");
        WriteOratorioPlugin(
            pluginId: "duplicate-namespace-a",
            appId: "com.dotharness.duplicatea",
            toolNamespace: "duplicate_ns",
            rootName: "duplicate-namespace-a");
        WriteOratorioPlugin(
            pluginId: "duplicate-namespace-b",
            appId: "com.dotharness.duplicateb",
            toolNamespace: "duplicate_ns",
            rootName: "duplicate-namespace-b");

        var catalog = AppBindingCatalog.Discover(new AppConfig(), _tempRoot, _workspaceCraftPath);

        Assert.DoesNotContain(catalog.Entries, entry => entry.Descriptor.AppId == "Not.ReverseDns");
        Assert.Contains(catalog.Entries, entry =>
            entry.Descriptor.AppId == "com.dotharness.oratorio"
            && entry.Plugin.SourceKind == PluginDiscoverySourceKind.Workspace);
        Assert.DoesNotContain(catalog.Entries, entry => entry.Descriptor.AppId == "com.dotharness.duplicatea");
        Assert.DoesNotContain(catalog.Entries, entry => entry.Descriptor.AppId == "com.dotharness.duplicateb");
        Assert.Contains(catalog.Diagnostics, d => d.Code == "InvalidAppId");
        Assert.Contains(catalog.Diagnostics, d => d.Code == "DuplicateAppToolNamespace");
    }

    [Fact]
    public void ResolveOriginApp_AttributesThreadByDeclaredOriginChannel()
    {
        WriteOratorioPlugin(originChannel: "oratorio");
        var service = new AppBindingService();
        var catalog = AppBindingCatalog.Discover(new AppConfig(), _tempRoot, _workspaceCraftPath);

        var origin = service.ResolveOriginApp(catalog, "oratorio");

        Assert.NotNull(origin);
        Assert.Equal("com.dotharness.oratorio", origin!.AppId);
        Assert.Equal("Oratorio", origin.DisplayName);
        Assert.NotNull(origin.Icon);
        Assert.StartsWith("data:image/svg+xml;base64,", origin.Icon!);
    }

    [Fact]
    public void ResolveOriginApp_ReturnsNullForUnmatchedOrBlankChannel()
    {
        WriteOratorioPlugin(originChannel: "oratorio");
        var service = new AppBindingService();
        var catalog = AppBindingCatalog.Discover(new AppConfig(), _tempRoot, _workspaceCraftPath);

        Assert.Null(service.ResolveOriginApp(catalog, "dotcraft-desktop"));
        Assert.Null(service.ResolveOriginApp(catalog, ""));
        Assert.Null(service.ResolveOriginApp(catalog, null));
    }

    [Fact]
    public void ResolveOriginApp_IsOptInAndDoesNotMatchToolNamespace()
    {
        // App is installed but does not declare originChannel: no implicit toolNamespace match.
        WriteOratorioPlugin(originChannel: null);
        var service = new AppBindingService();
        var catalog = AppBindingCatalog.Discover(new AppConfig(), _tempRoot, _workspaceCraftPath);

        Assert.Null(service.ResolveOriginApp(catalog, "oratorio"));
    }

    [Fact]
    public void ResolveOriginApp_AttributesMemberWhenChannelContextMatches()
    {
        WriteOratorioPlugin(originChannel: "oratorio", withOriginMembers: true);
        var service = new AppBindingService();
        var catalog = AppBindingCatalog.Discover(new AppConfig(), _tempRoot, _workspaceCraftPath);

        var origin = service.ResolveOriginApp(catalog, "oratorio", "mission_x:alpha");

        Assert.NotNull(origin);
        Assert.Equal("com.dotharness.oratorio", origin!.AppId);
        Assert.Equal("Alpha", origin.DisplayName);
        Assert.Equal("alpha", origin.MemberId);
        Assert.NotNull(origin.Icon);
        Assert.StartsWith("data:image/svg+xml;base64,", origin.Icon!);
    }

    [Fact]
    public void ResolveOriginApp_FallsBackToAppWhenNoMemberMatches()
    {
        WriteOratorioPlugin(originChannel: "oratorio", withOriginMembers: true);
        var service = new AppBindingService();
        var catalog = AppBindingCatalog.Discover(new AppConfig(), _tempRoot, _workspaceCraftPath);

        var origin = service.ResolveOriginApp(catalog, "oratorio", "mission_x:gamma");

        Assert.NotNull(origin);
        Assert.Equal("Oratorio", origin!.DisplayName);
        Assert.Null(origin.MemberId);
    }

    [Fact]
    public async Task PluginLifecycle_ForAppPluginEmitsAppListUpdated()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.PluginSetEnabled,
            new { id = "oratorio", enabled = false }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.False(response.RootElement.GetProperty("result").GetProperty("plugin").GetProperty("enabled").GetBoolean());

        using var notification = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsNotification(notification, AppListUpdated);
        Assert.Equal("oratorio", notification.RootElement.GetProperty("params").GetProperty("pluginId").GetString());
        Assert.Equal("plugin/disable", notification.RootElement.GetProperty("params").GetProperty("reason").GetString());
        Assert.Contains(
            notification.RootElement.GetProperty("params").GetProperty("appIds").EnumerateArray(),
            appId => appId.GetString() == "com.dotharness.oratorio");
    }

    [Fact]
    public async Task PluginDisable_MovesActiveBindingsOfflineAndNotifiesThreads()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());

        await ConnectAppAsync(harness);
        var bindingId = await CreateAcceptAndAttachAsync(harness, thread.Id);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.PluginSetEnabled,
            new { id = "oratorio", enabled = false }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        using var appListNotification = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsNotification(appListNotification, AppListUpdated);

        using var bindingNotification = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsNotification(bindingNotification, "thread/appBindings/changed");
        var bindingParams = bindingNotification.RootElement.GetProperty("params");
        Assert.Equal(thread.Id, bindingParams.GetProperty("threadId").GetString());
        Assert.Equal(bindingId, bindingParams.GetProperty("bindingId").GetString());
        Assert.Equal("offline", bindingParams.GetProperty("state").GetString());
        Assert.Equal("active", bindingParams.GetProperty("previousState").GetString());

        using var listResponse = await ExecuteAndReadResponseAsync(
            harness,
            ThreadAppBindingsList,
            new { threadId = thread.Id });
        AppServerTestHarness.AssertIsSuccessResponse(listResponse);
        var binding = Assert.Single(listResponse.RootElement.GetProperty("result").GetProperty("bindings").EnumerateArray());
        Assert.Equal("offline", binding.GetProperty("state").GetString());
    }

    [Fact]
    public async Task BindingFlow_ConnectsAcceptsAttachesAndDispatchesRuntimeTool()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());

        await ConnectAppAsync(harness);
        var bindingId = await CreateAcceptAndAttachAsync(harness, thread.Id);

        using var listResponse = await ExecuteAndReadResponseAsync(
            harness,
            ThreadAppBindingsList,
            new { threadId = thread.Id });
        AppServerTestHarness.AssertIsSuccessResponse(listResponse);
        var binding = Assert.Single(listResponse.RootElement.GetProperty("result").GetProperty("bindings").EnumerateArray());
        Assert.Equal(bindingId, binding.GetProperty("bindingId").GetString());
        Assert.Equal("active", binding.GetProperty("state").GetString());
        Assert.StartsWith("data:image/svg+xml;base64,", binding.GetProperty("icon").GetString());
        Assert.Equal(1, binding.GetProperty("attachedToolCount").GetInt32());
        Assert.Contains(thread.Id, harness.Service.RefreshedThreadAgents);

        harness.Transport.DrainSent();
        harness.Transport.ApprovalHandler = (method, @params) =>
            InMemoryTransport.BuildClientResponse(
                1,
                new DynamicToolCallResult
                {
                    Success = true,
                    ContentItems = [new ExtChannelToolContentItem { Type = "text", Text = "created card" }],
                    StructuredResult = JsonNode.Parse("""{"cardId":"card-1"}""")
                });

        var runtimeTool = Assert.IsAssignableFrom<AIFunction>(
            Assert.Single(service.CreateRuntimeToolsForThread(thread, new HashSet<string>(StringComparer.Ordinal))));
        var turn = AppServerTestHarness.MakeTurn(thread.Id);
        var seq = 0;
        using var scope = PluginFunctionExecutionScope.Set(new PluginFunctionExecutionContext
        {
            ThreadId = thread.Id,
            TurnId = turn.Id,
            OriginChannel = "appserver",
            WorkspacePath = thread.WorkspacePath,
            RequireApprovalOutsideWorkspace = false,
            ApprovalService = new AutoApproveApprovalService(),
            Turn = turn,
            NextItemSequence = () => ++seq,
            EmitItemStarted = _ => { },
            EmitItemCompleted = _ => { }
        });

        await runtimeTool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["title"] = "Ship App Binding"
        }));

        using var toolCall = await harness.Transport.ReadNextSentAsync();
        Assert.Equal(AppServerMethods.ItemToolCall, toolCall.RootElement.GetProperty("method").GetString());
        Assert.Equal("CreateCard", toolCall.RootElement.GetProperty("params").GetProperty("tool").GetString());
        Assert.Equal("Ship App Binding", toolCall.RootElement.GetProperty("params").GetProperty("arguments").GetProperty("title").GetString());

        var payload = Assert.IsType<DynamicToolCallPayload>(Assert.Single(turn.Items).Payload);
        Assert.True(payload.Success);
        Assert.Equal("created card", Assert.Single(payload.ContentItems!).Text);
        Assert.Equal("card-1", payload.StructuredResult?["cardId"]?.GetValue<string>());
    }

    [Fact]
    public async Task RefreshBindings_MarksActiveBindingOfflineWhenToolChannelClosed()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());

        await ConnectAppAsync(harness);
        var attachmentConnection = new AppServerConnection();
        var bindingId = await CreateAcceptAndAttachWithConnectionAsync(service, harness, thread.Id, attachmentConnection);
        attachmentConnection.MarkClosed();

        using var refreshResponse = await ExecuteAndReadResponseAsync(
            harness,
            ThreadAppBindingsRefresh,
            new { threadId = thread.Id, bindingId },
            expectedNotificationMethod: "thread/appBindings/changed");
        AppServerTestHarness.AssertIsSuccessResponse(refreshResponse);
        var refreshed = Assert.Single(refreshResponse.RootElement.GetProperty("result").GetProperty("bindings").EnumerateArray());
        Assert.Equal(bindingId, refreshed.GetProperty("bindingId").GetString());
        Assert.Equal("offline", refreshed.GetProperty("state").GetString());

        using var listResponse = await ExecuteAndReadResponseAsync(
            harness,
            ThreadAppBindingsList,
            new { threadId = thread.Id });
        AppServerTestHarness.AssertIsSuccessResponse(listResponse);
        var binding = Assert.Single(listResponse.RootElement.GetProperty("result").GetProperty("bindings").EnumerateArray());
        Assert.Equal("offline", binding.GetProperty("state").GetString());
        Assert.Equal("connected", binding.GetProperty("connectionState").GetString());

        using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(_workspaceCraftPath, "app-bindings", "state.json")));
        Assert.Contains(
            state.RootElement.GetProperty("audit").EnumerateArray(),
            audit => audit.GetProperty("event").GetString() == "binding.offline");
    }

    [Fact]
    public async Task RefreshBindings_RestoresOfflineBindingAfterToolReattach()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());

        await ConnectAppAsync(harness);
        var attachmentConnection = new AppServerConnection();
        var bindingId = await CreateAcceptAndAttachWithConnectionAsync(service, harness, thread.Id, attachmentConnection);
        attachmentConnection.MarkClosed();
        using var offlineResponse = await ExecuteAndReadResponseAsync(
            harness,
            ThreadAppBindingsRefresh,
            new { threadId = thread.Id, bindingId },
            expectedNotificationMethod: "thread/appBindings/changed");
        AppServerTestHarness.AssertIsSuccessResponse(offlineResponse);

        AttachToolsDirect(service, bindingId, thread.Id, new AppServerConnection());

        using var refreshResponse = await ExecuteAndReadResponseAsync(
            harness,
            ThreadAppBindingsRefresh,
            new { threadId = thread.Id, bindingId },
            expectedNotificationMethod: "thread/appBindings/changed");
        AppServerTestHarness.AssertIsSuccessResponse(refreshResponse);
        var refreshed = Assert.Single(refreshResponse.RootElement.GetProperty("result").GetProperty("bindings").EnumerateArray());
        Assert.Equal(bindingId, refreshed.GetProperty("bindingId").GetString());
        Assert.Equal("active", refreshed.GetProperty("state").GetString());

        using var listResponse = await ExecuteAndReadResponseAsync(
            harness,
            ThreadAppBindingsList,
            new { threadId = thread.Id });
        AppServerTestHarness.AssertIsSuccessResponse(listResponse);
        var binding = Assert.Single(listResponse.RootElement.GetProperty("result").GetProperty("bindings").EnumerateArray());
        Assert.Equal("active", binding.GetProperty("state").GetString());
        Assert.Equal("connected", binding.GetProperty("connectionState").GetString());
    }

    [Fact]
    public async Task ThreadWire_IncludesLightweightAppBindingSummaries()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());

        await ConnectAppAsync(harness);
        var bindingId = await CreateAcceptAndAttachAsync(harness, thread.Id);

        using var readResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppServerMethods.ThreadRead,
            new { threadId = thread.Id, includeTurns = false });
        AppServerTestHarness.AssertIsSuccessResponse(readResponse);
        var readBinding = Assert.Single(readResponse.RootElement
            .GetProperty("result")
            .GetProperty("thread")
            .GetProperty("appBindings")
            .EnumerateArray());
        Assert.Equal(bindingId, readBinding.GetProperty("bindingId").GetString());
        Assert.Equal("com.dotharness.oratorio", readBinding.GetProperty("appId").GetString());
        Assert.Equal("Oratorio", readBinding.GetProperty("displayName").GetString());
        Assert.Equal("oratorio", readBinding.GetProperty("toolNamespace").GetString());
        Assert.Equal("active", readBinding.GetProperty("state").GetString());
        Assert.Equal("connected", readBinding.GetProperty("connectionState").GetString());
        Assert.StartsWith("data:image/svg+xml;base64,", readBinding.GetProperty("icon").GetString());

        using var listResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppServerMethods.ThreadList,
            new { identity = CreateIdentity() });
        AppServerTestHarness.AssertIsSuccessResponse(listResponse);
        var summaryBinding = Assert.Single(listResponse.RootElement
            .GetProperty("result")
            .GetProperty("data")[0]
            .GetProperty("appBindings")
            .EnumerateArray());
        Assert.Equal(bindingId, summaryBinding.GetProperty("bindingId").GetString());
        Assert.StartsWith("data:image/svg+xml;base64,", summaryBinding.GetProperty("icon").GetString());
    }

    [Fact]
    public async Task ThreadDelete_RevokesPersistedAppBindings()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());

        await ConnectAppAsync(harness);
        var bindingId = await CreateAcceptAndAttachAsync(harness, thread.Id);

        using var deleteResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppServerMethods.ThreadDelete,
            new { threadId = thread.Id });
        AppServerTestHarness.AssertIsSuccessResponse(deleteResponse);

        using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(_workspaceCraftPath, "app-bindings", "state.json")));
        var binding = Assert.Single(state.RootElement.GetProperty("bindings").EnumerateArray());
        Assert.Equal(bindingId, binding.GetProperty("bindingId").GetString());
        Assert.Equal("revoked", binding.GetProperty("state").GetString());
        Assert.Contains(
            state.RootElement.GetProperty("audit").EnumerateArray(),
            audit => audit.GetProperty("event").GetString() == "binding.revoked.threadDeleted");
    }

    [Fact]
    public async Task ConnectionRevoke_MovesActiveBindingsOfflineAndNotifiesThreads()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());

        await ConnectAppAsync(harness);
        var bindingId = await CreateAcceptAndAttachAsync(harness, thread.Id);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppConnectionRevoke,
            new { appId = "com.dotharness.oratorio", reason = "disconnect" }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.Equal("notConnected", response.RootElement.GetProperty("result").GetProperty("state").GetString());

        using var connectionNotification = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsNotification(connectionNotification, "app/connection/changed");

        using var bindingNotification = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsNotification(bindingNotification, "thread/appBindings/changed");
        var bindingParams = bindingNotification.RootElement.GetProperty("params");
        Assert.Equal(thread.Id, bindingParams.GetProperty("threadId").GetString());
        Assert.Equal(bindingId, bindingParams.GetProperty("bindingId").GetString());
        Assert.Equal("offline", bindingParams.GetProperty("state").GetString());
        Assert.Equal("active", bindingParams.GetProperty("previousState").GetString());
        Assert.Equal("offline", bindingParams.GetProperty("changeKind").GetString());

        using var listResponse = await ExecuteAndReadResponseAsync(
            harness,
            ThreadAppBindingsList,
            new { threadId = thread.Id });
        AppServerTestHarness.AssertIsSuccessResponse(listResponse);
        var binding = Assert.Single(listResponse.RootElement.GetProperty("result").GetProperty("bindings").EnumerateArray());
        Assert.Equal("offline", binding.GetProperty("state").GetString());
    }

    [Fact]
    public async Task RevokeBinding_InterruptsActiveTurn()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());

        await ConnectAppAsync(harness);
        var bindingId = await CreateAcceptAndAttachAsync(harness, thread.Id);
        thread.Turns.Add(new SessionTurn
        {
            Id = "turn_running",
            ThreadId = thread.Id,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });

        using var revokeResponse = await ExecuteAndReadResponseAsync(
            harness,
            ThreadAppBindingsRevoke,
            new { threadId = thread.Id, bindingId, reason = "user revoked" },
            expectedNotificationMethod: "thread/appBindings/changed");
        AppServerTestHarness.AssertIsSuccessResponse(revokeResponse);

        var cancelled = Assert.Single(harness.Service.CancelledTurns);
        Assert.Equal(thread.Id, cancelled.threadId);
        Assert.Equal("turn_running", cancelled.turnId);
    }

    [Fact]
    public async Task AttachTools_RejectsToolsOutsideDeclaredNamespace()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        await ConnectAppAsync(harness);

        var requestToken = await CreateBindingRequestAsync(harness, thread.Id);
        var bindingId = await AcceptBindingAsync(harness, requestToken.BindingRequestId, requestToken.Token);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppBindingAttachTools,
            new
            {
                bindingId,
                threadId = thread.Id,
                appId = "com.dotharness.oratorio",
                grantId = "grant-1",
                tools = new[]
                {
                    new DynamicToolSpec
                    {
                        Namespace = "other",
                        Name = "CreateCard",
                        Description = "Create a card",
                        InputSchema = CreateCardSchema()
                    }
                }
            }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
        Assert.Contains(
            "must use namespace 'oratorio'",
            response.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());
    }

    [Fact]
    public async Task DynamicCatalogApp_AllowsUrlOnlyDescriptorAndEmptyRequestedTools()
    {
        WriteUnityDynamicPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        await ConnectAppAsync(harness, "com.example.unitydynamic", "unity-editor");

        using var listResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppList,
            new { includeDisabled = true });
        var app = Assert.Single(
            listResponse.RootElement.GetProperty("result").GetProperty("apps").EnumerateArray(),
            item => item.GetProperty("appId").GetString() == "com.example.unitydynamic");
        Assert.Equal("", app.GetProperty("nativeApp").GetProperty("protocol").GetString());
        Assert.True(app.GetProperty("dynamicToolCatalog").GetProperty("enabled").GetBoolean());
        Assert.Empty(app.GetProperty("toolCatalog").EnumerateArray());

        var request = await CreateBindingRequestAsync(
            harness,
            thread.Id,
            appId: "com.example.unitydynamic",
            requestedScopes: ["unity.read", "unity.edit"],
            requestedTools: null,
            omitRequestedTools: true);
        using var inspectResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingRequestGet,
            new
            {
                appId = "com.example.unitydynamic",
                bindingRequestId = request.BindingRequestId,
                requestToken = request.Token
            });
        AppServerTestHarness.AssertIsSuccessResponse(inspectResponse);
        var inspect = inspectResponse.RootElement.GetProperty("result");
        Assert.True(inspect.GetProperty("dynamicToolCatalog").GetProperty("enabled").GetBoolean());
        Assert.Empty(inspect.GetProperty("requestedTools").EnumerateArray());
    }

    [Fact]
    public async Task AttachTools_AcceptsDynamicCatalogAndEnforcesMutateDeferred()
    {
        WriteUnityDynamicPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        await ConnectAppAsync(harness, "com.example.unitydynamic", "unity-editor");

        var request = await CreateBindingRequestAsync(
            harness,
            thread.Id,
            appId: "com.example.unitydynamic",
            requestedScopes: ["unity.read", "unity.edit"],
            requestedTools: null,
            omitRequestedTools: true);
        var bindingId = await AcceptBindingAsync(
            harness,
            request.BindingRequestId,
            request.Token,
            grantedScopes: ["unity.read", "unity.edit"],
            approvedBy: "unity-editor");

        using var attachResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingAttachTools,
            new
            {
                bindingId,
                threadId = thread.Id,
                appId = "com.example.unitydynamic",
                grantId = "grant-1",
                tools = new[]
                {
                    new DynamicToolSpec
                    {
                        Namespace = "unity_test",
                        Name = "unity_move_object",
                        Description = "Move an object.",
                        InputSchema = CreateCardSchema()
                    }
                },
                toolCatalog = new[]
                {
                    new AppToolCatalogEntry
                    {
                        Name = "unity_move_object",
                        Scope = "unity.edit",
                        Risk = "mutate",
                        DefaultExposure = "direct",
                        Description = "Move an object."
                    }
                },
                directToolNames = new[] { "unity_move_object" }
            },
            expectedNotificationMethod: "thread/appBindings/changed");

        AppServerTestHarness.AssertIsSuccessResponse(attachResponse);
        var result = attachResponse.RootElement.GetProperty("result");
        Assert.Equal(1, result.GetProperty("acceptedToolCount").GetInt32());
        Assert.Contains(
            result.GetProperty("warnings").EnumerateArray(),
            warning => warning.GetString()!.Contains("deferred exposure was enforced", StringComparison.Ordinal));

        var runtimeTool = Assert.IsAssignableFrom<IDynamicToolRuntimeTool>(
            Assert.Single(service.CreateRuntimeToolsForThread(thread, new HashSet<string>(StringComparer.Ordinal))));
        Assert.True(runtimeTool.Spec.DeferLoading);
    }

    [Fact]
    public async Task AttachTools_RejectsDynamicToolWithoutCatalogEntry()
    {
        WriteUnityDynamicPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        await ConnectAppAsync(harness, "com.example.unitydynamic", "unity-editor");

        var request = await CreateBindingRequestAsync(
            harness,
            thread.Id,
            appId: "com.example.unitydynamic",
            requestedScopes: ["unity.read"],
            requestedTools: null,
            omitRequestedTools: true);
        var bindingId = await AcceptBindingAsync(
            harness,
            request.BindingRequestId,
            request.Token,
            grantedScopes: ["unity.read"],
            approvedBy: "unity-editor");

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppBindingAttachTools,
            new
            {
                bindingId,
                threadId = thread.Id,
                appId = "com.example.unitydynamic",
                grantId = "grant-1",
                tools = new[]
                {
                    new DynamicToolSpec
                    {
                        Namespace = "unity_test",
                        Name = "unity_scene_query",
                        Description = "Query scene.",
                        InputSchema = CreateCardSchema()
                    }
                }
            }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
        Assert.Contains(
            "is not declared in the app tool catalog",
            response.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());
    }

    [Fact]
    public async Task AttachTools_RejectsDynamicToolWithUngrantedScope()
    {
        WriteUnityDynamicPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        await ConnectAppAsync(harness, "com.example.unitydynamic", "unity-editor");

        var request = await CreateBindingRequestAsync(
            harness,
            thread.Id,
            appId: "com.example.unitydynamic",
            requestedScopes: ["unity.read"],
            requestedTools: null,
            omitRequestedTools: true);
        var bindingId = await AcceptBindingAsync(
            harness,
            request.BindingRequestId,
            request.Token,
            grantedScopes: ["unity.read"],
            approvedBy: "unity-editor");

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppBindingAttachTools,
            new
            {
                bindingId,
                threadId = thread.Id,
                appId = "com.example.unitydynamic",
                grantId = "grant-1",
                tools = new[]
                {
                    new DynamicToolSpec
                    {
                        Namespace = "unity_test",
                        Name = "unity_move_object",
                        Description = "Move an object.",
                        InputSchema = CreateCardSchema()
                    }
                },
                toolCatalog = new[]
                {
                    new AppToolCatalogEntry
                    {
                        Name = "unity_move_object",
                        Scope = "unity.edit",
                        Risk = "mutate",
                        DefaultExposure = "deferred"
                    }
                }
            }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
        Assert.Contains(
            "requires ungranted scope 'unity.edit'",
            response.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());
    }

    private AppServerTestHarness CreateHarness(
        AppBindingService service,
        AppConfig? config = null,
        IContextPageManager? contextPageManager = null)
    {
        config ??= new AppConfig();
        var monitor = new AppConfigMonitor(config);
        return new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            protocolExtensions: [new AppBindingProtocolExtension(service, monitor, builtInPluginSourceRoots: [BundledPluginSourceRoot()])],
            appConfigMonitor: monitor,
            appBindingService: service,
            contextPageManager: contextPageManager,
            builtInPluginSourceRoots: [BundledPluginSourceRoot()]);
    }

    private void AssertAppBindingAuditContains(string eventName)
    {
        var statePath = Path.Combine(_workspaceCraftPath, "app-bindings", "state.json");
        using var document = JsonDocument.Parse(File.ReadAllText(statePath));
        Assert.Contains(
            document.RootElement.GetProperty("audit").EnumerateArray(),
            audit => audit.GetProperty("event").GetString() == eventName);
    }

    private SessionIdentity CreateIdentity() =>
        new()
        {
            ChannelName = "appserver",
            UserId = "test_user",
            WorkspacePath = _tempRoot
        };

    private async Task ConnectAppAsync(
        AppServerTestHarness harness,
        string appId = "com.dotharness.oratorio",
        string accountLabel = "local-oratorio")
    {
        using var startResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionStart,
            new { appId },
            expectedNotificationMethod: "app/connection/changed");
        var connectionRequestId = startResponse.RootElement
            .GetProperty("result")
            .GetProperty("connectionRequestId")
            .GetString()!;
        var token = ExtractToken(startResponse.RootElement
            .GetProperty("result")
            .GetProperty("handoff")
            .GetProperty("uri")
            .GetString()!);

        using var connectResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionConnect,
            new
            {
                connectionRequestId,
                requestToken = token,
                appId,
                accountLabel
            },
            expectedNotificationMethod: "app/connection/changed");
        AppServerTestHarness.AssertIsSuccessResponse(connectResponse);
        Assert.Equal("connected", connectResponse.RootElement.GetProperty("result").GetProperty("state").GetString());
    }

    private async Task<string> CreateAcceptAndAttachAsync(AppServerTestHarness harness, string threadId)
    {
        var request = await CreateBindingRequestAsync(harness, threadId);
        var bindingId = await AcceptBindingAsync(harness, request.BindingRequestId, request.Token);
        using var attachResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingAttachTools,
            new
            {
                bindingId,
                threadId,
                appId = "com.dotharness.oratorio",
                grantId = "grant-1",
                tools = new[]
                {
                    new DynamicToolSpec
                    {
                        Namespace = "oratorio",
                        Name = "CreateCard",
                        Description = "Create a card",
                        InputSchema = CreateCardSchema()
                    }
                }
            },
            expectedNotificationMethod: "thread/appBindings/changed");
        AppServerTestHarness.AssertIsSuccessResponse(attachResponse);
        Assert.Equal(1, attachResponse.RootElement.GetProperty("result").GetProperty("acceptedToolCount").GetInt32());
        return bindingId;
    }

    private async Task<string> CreateAcceptAndAttachWithConnectionAsync(
        AppBindingService service,
        AppServerTestHarness harness,
        string threadId,
        AppServerConnection attachmentConnection)
    {
        var request = await CreateBindingRequestAsync(harness, threadId);
        var bindingId = await AcceptBindingAsync(harness, request.BindingRequestId, request.Token);
        AttachToolsDirect(service, bindingId, threadId, attachmentConnection);
        return bindingId;
    }

    private void AttachToolsDirect(
        AppBindingService service,
        string bindingId,
        string threadId,
        AppServerConnection attachmentConnection)
    {
        var catalog = AppBindingCatalog.Discover(new AppConfig(), _tempRoot, _workspaceCraftPath);
        var result = service.AttachTools(
            catalog,
            _workspaceCraftPath,
            new InMemoryTransport(),
            attachmentConnection,
            new AppBindingAttachToolsParams
            {
                BindingId = bindingId,
                ThreadId = threadId,
                AppId = "com.dotharness.oratorio",
                GrantId = "grant-1",
                Tools =
                [
                    new DynamicToolSpec
                    {
                        Namespace = "oratorio",
                        Name = "CreateCard",
                        Description = "Create a card",
                        InputSchema = CreateCardSchema()
                    }
                ]
            });
        Assert.Equal(1, result.AcceptedToolCount);
    }

    private async Task<(string BindingRequestId, string Token)> CreateBindingRequestAsync(
        AppServerTestHarness harness,
        string threadId,
        string appId = "com.dotharness.oratorio",
        string[]? requestedScopes = null,
        string[]? requestedTools = null,
        bool omitRequestedTools = false)
    {
        requestedScopes ??= ["board.read", "board.manage"];
        if (!omitRequestedTools)
            requestedTools ??= ["CreateCard"];
        using var response = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingRequestCreate,
            new
            {
                threadId,
                appId,
                requestedScopes,
                requestedTools = omitRequestedTools ? null : requestedTools,
                source = "threadMenu"
            });
        AppServerTestHarness.AssertIsSuccessResponse(response);
        return (
            response.RootElement.GetProperty("result").GetProperty("bindingRequestId").GetString()!,
            ExtractToken(response.RootElement.GetProperty("result").GetProperty("handoff").GetProperty("uri").GetString()!));
    }

    private async Task<string> AcceptBindingAsync(
        AppServerTestHarness harness,
        string bindingRequestId,
        string token,
        string[]? grantedScopes = null,
        string approvedBy = "local-oratorio",
        DateTimeOffset? expiresAt = null)
    {
        grantedScopes ??= ["board.read", "board.manage"];
        using var response = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingAccept,
            new
            {
                bindingRequestId,
                requestToken = token,
                grantId = "grant-1",
                grantedScopes,
                expiresAt,
                approvalMode = "interactive",
                approvedBy,
                auditRef = "audit-1"
            },
            expectedNotificationMethod: "thread/appBindings/changed");
        AppServerTestHarness.AssertIsSuccessResponse(response);
        return response.RootElement.GetProperty("result").GetProperty("binding").GetProperty("bindingId").GetString()!;
    }

    private static async Task<JsonDocument> ExecuteAndReadResponseAsync(
        AppServerTestHarness harness,
        string method,
        object @params,
        string? expectedNotificationMethod = null)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(method, @params));
        var response = await harness.Transport.ReadNextSentAsync();
        if (!string.IsNullOrWhiteSpace(expectedNotificationMethod))
        {
            using var notification = await harness.Transport.ReadNextSentAsync();
            AppServerTestHarness.AssertIsNotification(notification, expectedNotificationMethod);
        }

        return response;
    }

    private static JsonObject CreateCardSchema() =>
        new()
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["title"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray("title")
        };

    private static string ExtractToken(string uri)
    {
        var queryStart = uri.IndexOf('?', StringComparison.Ordinal);
        Assert.True(queryStart >= 0, $"URI does not include a query string: {uri}");
        var query = uri[(queryStart + 1)..];
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0] == "token")
                return Uri.UnescapeDataString(pair[1]);
        }

        throw new InvalidOperationException($"URI does not include token query parameter: {uri}");
    }

    private static string BundledPluginSourceRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (File.Exists(Path.Combine(dir, "dotcraft.sln")))
                return Path.Combine(dir, "desktop", "resources", "plugins", "dotcraft-bundled", "plugins");
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    private void WriteOratorioPlugin(
        string pluginId = "oratorio",
        string appId = "com.dotharness.oratorio",
        string toolNamespace = "oratorio",
        string rootName = "oratorio",
        string? originChannel = "oratorio",
        bool withOriginMembers = false)
    {
        var originChannelJson = string.IsNullOrEmpty(originChannel)
            ? ""
            : $"      \"originChannel\": \"{originChannel}\",\n";
        var originMembersJson = withOriginMembers
            ? "      \"originMembers\": ["
              + "{ \"match\": \"alpha\", \"displayName\": \"Alpha\", \"icon\": \"./member-alpha.svg\" },"
              + "{ \"match\": \"beta\", \"displayName\": \"Beta\", \"icon\": \"./member-beta.svg\" }],\n"
            : "";
        var pluginRoot = Path.Combine(_workspaceCraftPath, "plugins", rootName);
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
{
  "schemaVersion": 1,
  "id": "{{pluginId}}",
  "version": "1.0.0",
  "displayName": "Oratorio",
  "description": "Manage Oratorio boards from selected DotCraft threads.",
  "capabilities": ["app"],
  "apps": "./apps.json"
}
""");
        File.WriteAllText(
            Path.Combine(pluginRoot, "apps.json"),
            $$"""
{
  "apps": [
    {
      "appId": "{{appId}}",
      "toolNamespace": "{{toolNamespace}}",
      "displayName": "Oratorio",
      "developerName": "DotHarness",
      "description": "Manage Oratorio boards from selected DotCraft threads.",
      "category": "Productivity",
      "icon": "./oratorio.svg",
{{originChannelJson}}{{originMembersJson}}      "nativeApplication": {
        "displayName": "Oratorio",
        "protocol": "oratorio",
        "installUrl": "https://github.com/DotHarness/oratorio/releases"
      },
      "connection": {
        "handoffModes": [
          {
            "mode": "customProtocol",
            "uriTemplate": "oratorio://dotcraft/{operation}?app={appId}&request={requestId}&token={requestToken}&endpoint={endpoint}"
          }
        ]
      },
      "scopes": [
        {
          "id": "board.read",
          "displayName": "Read boards",
          "description": "Read board metadata and card state.",
          "risk": "read",
          "defaultSelected": true
        },
        {
          "id": "board.manage",
          "displayName": "Manage boards",
          "description": "Create and update board cards.",
          "risk": "mutate"
        }
      ],
      "toolCatalog": [
        {
          "name": "CreateCard",
          "scope": "board.manage",
          "risk": "mutate",
          "defaultExposure": "direct",
          "description": "Create a board card."
        }
      ]
    }
  ]
}
""");
        File.WriteAllText(
            Path.Combine(pluginRoot, "oratorio.svg"),
            """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><rect width="24" height="24" rx="6" fill="#5865f2"/></svg>""");
        if (withOriginMembers)
        {
            foreach (var (name, fill) in new[] { ("member-alpha", "#10b981"), ("member-beta", "#f59e0b") })
            {
                File.WriteAllText(
                    Path.Combine(pluginRoot, $"{name}.svg"),
                    $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" fill="{fill}"/></svg>""");
            }
        }
    }

    private void WriteUnityDynamicPlugin()
    {
        var pluginRoot = Path.Combine(_workspaceCraftPath, "plugins", "dotcraft-unity");
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            """
{
  "schemaVersion": 1,
  "id": "dotcraft-unity",
  "version": "1.0.0",
  "displayName": "dotcraft-unity",
  "description": "Bind Unity Editor tools.",
  "capabilities": ["app"],
  "apps": "./apps.json"
}
""");
        File.WriteAllText(
            Path.Combine(pluginRoot, "apps.json"),
            """
{
  "apps": [
    {
      "appId": "com.example.unitydynamic",
      "toolNamespace": "unity_test",
      "displayName": "Unity Editor",
      "developerName": "DotHarness",
      "description": "Bind enabled Unity runtime tools.",
      "nativeApplication": {
        "displayName": "Unity Editor",
        "installUrl": "https://unity.com/download"
      },
      "connection": {
        "handoffModes": [
          {
            "mode": "url",
            "uriTemplate": "http://127.0.0.1:39777/dotcraft/{operation}?app={appId}&request={requestId}&token={requestToken}&endpoint={endpoint}&scopes={scopes}"
          }
        ]
      },
      "scopes": [
        {
          "id": "unity.read",
          "displayName": "Read Unity",
          "description": "Read Unity state.",
          "risk": "read",
          "defaultSelected": true
        },
        {
          "id": "unity.edit",
          "displayName": "Edit Unity",
          "description": "Mutate Unity state.",
          "risk": "mutate"
        }
      ],
      "dynamicToolCatalog": {
        "enabled": true
      },
      "toolCatalog": []
    }
  ]
}
""");
    }
}
