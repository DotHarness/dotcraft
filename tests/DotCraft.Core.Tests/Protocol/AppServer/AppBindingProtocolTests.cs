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
    private const string AppSocialBindingResolve = "app/socialBinding/resolve";
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
    public async Task SocialBinding_CreateAcceptResolveAndRejectDuplicateTarget()
    {
        const string qqAppId = "com.dotharness.channel.qq";
        var registry = new ChannelRuntimeRegistry();
        registry.Register(new RecordingChannelRuntime("qq"));
        var service = new AppBindingService([
            new SocialChannelAppBindingRuntime(
                "qq",
                "QQ",
                "Continue this thread in QQ.",
                registry)
        ]);
        using var harness = CreateHarness(service, channelRuntimeRegistry: registry);
        await InitializeChannelAdapterAsync(harness, "qq");
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());

        using var createResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingRequestCreate,
            new
            {
                threadId = thread.Id,
                appId = qqAppId,
                requestedScopes = new[] { "conversation.receive", "message.send" },
                requestedTools = new[] { "SendMessageToBoundConversation" },
                source = "threadMenu",
                bindingKind = "socialChannel",
                socialIntent = new
                {
                    channelName = "qq",
                    targetSelection = "confirmInChannel",
                    displayHint = "QQ"
                }
            });
        AppServerTestHarness.AssertIsSuccessResponse(createResponse);
        var createResult = createResponse.RootElement.GetProperty("result");
        Assert.Equal("bindCode", createResult.GetProperty("handoff").GetProperty("mode").GetString());
        var bindCode = createResult.GetProperty("handoff").GetProperty("bindCode").GetString()!;
        Assert.Matches("^[1-9][0-9]{5}$", bindCode);
        var bindingRequestId = createResult.GetProperty("bindingRequestId").GetString()!;

        using var inspectResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingRequestGet,
            new
            {
                appId = qqAppId,
                requestToken = bindCode,
                bindCode
            });
        AppServerTestHarness.AssertIsSuccessResponse(inspectResponse);
        Assert.Equal("socialChannel", inspectResponse.RootElement.GetProperty("result").GetProperty("bindingKind").GetString());
        Assert.Equal("qq", inspectResponse.RootElement.GetProperty("result").GetProperty("socialIntent").GetProperty("channelName").GetString());

        using var acceptResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingAccept,
            new
            {
                bindingRequestId,
                requestToken = bindCode,
                grantId = "social-grant-1",
                grantedScopes = new[] { "conversation.receive", "message.send" },
                approvalMode = "channelBindCode",
                approvedBy = "9988",
                socialTarget = QqSocialTarget()
            },
            expectedNotificationMethod: "thread/appBindings/changed");
        AppServerTestHarness.AssertIsSuccessResponse(acceptResponse);
        var binding = acceptResponse.RootElement.GetProperty("result").GetProperty("binding");
        Assert.Equal("socialChannel", binding.GetProperty("bindingKind").GetString());
        Assert.Equal("social-grant-1", binding.GetProperty("grantId").GetString());
        Assert.Equal("group:123456", binding.GetProperty("socialTarget").GetProperty("deliveryTarget").GetString());
        Assert.Equal(1, binding.GetProperty("attachedToolCount").GetInt32());

        using var resolveResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppSocialBindingResolve,
            new
            {
                appId = qqAppId,
                channelName = "qq",
                conversationKind = "group",
                conversationId = "123456"
            });
        AppServerTestHarness.AssertIsSuccessResponse(resolveResponse);
        var resolved = resolveResponse.RootElement.GetProperty("result").GetProperty("binding");
        Assert.Equal(thread.Id, resolved.GetProperty("threadId").GetString());
        Assert.Equal("social-grant-1", resolved.GetProperty("grantId").GetString());
        Assert.Equal("socialChannel", resolved.GetProperty("bindingKind").GetString());

        var duplicateThread = await harness.Service.CreateThreadAsync(CreateIdentity());
        using var duplicateCreateResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingRequestCreate,
            new
            {
                threadId = duplicateThread.Id,
                appId = qqAppId,
                requestedScopes = new[] { "conversation.receive", "message.send" },
                source = "threadMenu",
                bindingKind = "socialChannel",
                socialIntent = new { channelName = "qq" }
            });
        var duplicateBindCode = duplicateCreateResponse.RootElement.GetProperty("result").GetProperty("handoff").GetProperty("bindCode").GetString()!;
        using var duplicateAcceptResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingAccept,
            new
            {
                requestToken = duplicateBindCode,
                grantId = "social-grant-2",
                grantedScopes = new[] { "conversation.receive", "message.send" },
                approvalMode = "channelBindCode",
                approvedBy = "9988",
                socialTarget = QqSocialTarget()
            });
        AppServerTestHarness.AssertIsErrorResponse(duplicateAcceptResponse, AppServerErrors.InvalidParamsCode);
        Assert.Contains(
            "already bound",
            duplicateAcceptResponse.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ThreadArchive_ReleasesSocialBindingTargetAndPreservesRevokedRecord()
    {
        const string qqAppId = "com.dotharness.channel.qq";
        var registry = new ChannelRuntimeRegistry();
        registry.Register(new RecordingChannelRuntime("qq"));
        var service = new AppBindingService([
            new SocialChannelAppBindingRuntime(
                "qq",
                "QQ",
                "Continue this thread in QQ.",
                registry)
        ]);
        using var harness = CreateHarness(service, channelRuntimeRegistry: registry);
        await InitializeChannelAdapterAsync(harness, "qq");
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        var bindingId = await CreateAcceptedQqSocialBindingAsync(harness, qqAppId, thread.Id);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.ThreadArchive,
            new { threadId = thread.Id }));
        using var archiveResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(archiveResponse);
        using var statusNotification = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsNotification(statusNotification, AppServerMethods.ThreadStatusChanged);
        using var bindingNotification = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsNotification(bindingNotification, "thread/appBindings/changed");
        var notificationParams = bindingNotification.RootElement.GetProperty("params");
        Assert.Equal(thread.Id, notificationParams.GetProperty("threadId").GetString());
        Assert.Equal(bindingId, notificationParams.GetProperty("bindingId").GetString());
        Assert.Equal("revoked", notificationParams.GetProperty("state").GetString());
        Assert.Equal("active", notificationParams.GetProperty("previousState").GetString());
        Assert.Equal("threadArchived", notificationParams.GetProperty("changeKind").GetString());
        Assert.Null(harness.Transport.TryReadSent());

        using var resolveResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppSocialBindingResolve,
            new
            {
                appId = qqAppId,
                channelName = "qq",
                conversationKind = "group",
                conversationId = "123456"
            });
        AppServerTestHarness.AssertIsSuccessResponse(resolveResponse);
        Assert.Equal(JsonValueKind.Null, resolveResponse.RootElement.GetProperty("result").GetProperty("binding").ValueKind);

        using var listResponse = await ExecuteAndReadResponseAsync(
            harness,
            ThreadAppBindingsList,
            new { threadId = thread.Id, includeRevoked = true });
        AppServerTestHarness.AssertIsSuccessResponse(listResponse);
        var archivedBinding = Assert.Single(listResponse.RootElement.GetProperty("result").GetProperty("bindings").EnumerateArray());
        Assert.Equal(bindingId, archivedBinding.GetProperty("bindingId").GetString());
        Assert.Equal("revoked", archivedBinding.GetProperty("state").GetString());
        Assert.Equal("The thread was archived.", archivedBinding.GetProperty("diagnostic").GetString());

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.ThreadArchive,
            new { threadId = thread.Id }));
        using var idempotentResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(idempotentResponse);
        Assert.Null(harness.Transport.TryReadSent());

        var replacementThread = await harness.Service.CreateThreadAsync(CreateIdentity());
        var replacementBindingId = await CreateAcceptedQqSocialBindingAsync(harness, qqAppId, replacementThread.Id);
        Assert.NotEqual(bindingId, replacementBindingId);
        AssertAppBindingAuditContains("binding.revoked.threadArchived");
    }

    [Fact]
    public async Task ThreadArchive_CancelsPendingSocialBindingRequest()
    {
        const string qqAppId = "com.dotharness.channel.qq";
        var registry = new ChannelRuntimeRegistry();
        registry.Register(new RecordingChannelRuntime("qq"));
        var service = new AppBindingService([
            new SocialChannelAppBindingRuntime(
                "qq",
                "QQ",
                "Continue this thread in QQ.",
                registry)
        ]);
        using var harness = CreateHarness(service, channelRuntimeRegistry: registry);
        await InitializeChannelAdapterAsync(harness, "qq");
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());

        using var createResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingRequestCreate,
            new
            {
                threadId = thread.Id,
                appId = qqAppId,
                requestedScopes = new[] { "conversation.receive", "message.send" },
                source = "threadMenu",
                bindingKind = "socialChannel",
                socialIntent = new { channelName = "qq" }
            });
        AppServerTestHarness.AssertIsSuccessResponse(createResponse);
        var createResult = createResponse.RootElement.GetProperty("result");
        var bindingRequestId = createResult.GetProperty("bindingRequestId").GetString()!;
        var bindCode = createResult.GetProperty("handoff").GetProperty("bindCode").GetString()!;

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.ThreadArchive,
            new { threadId = thread.Id }));
        using var archiveResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(archiveResponse);
        using var statusNotification = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsNotification(statusNotification, AppServerMethods.ThreadStatusChanged);
        using var requestNotification = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsNotification(requestNotification, "thread/appBindings/changed");
        var notificationParams = requestNotification.RootElement.GetProperty("params");
        Assert.Equal(thread.Id, notificationParams.GetProperty("threadId").GetString());
        Assert.Equal(bindingRequestId, notificationParams.GetProperty("bindingRequestId").GetString());
        Assert.Equal("cancelled", notificationParams.GetProperty("state").GetString());
        Assert.Equal("pending", notificationParams.GetProperty("previousState").GetString());
        Assert.Equal("threadArchived", notificationParams.GetProperty("changeKind").GetString());
        Assert.Null(harness.Transport.TryReadSent());

        using var inspectResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingRequestGet,
            new
            {
                appId = qqAppId,
                requestToken = bindCode,
                bindCode
            });
        AppServerTestHarness.AssertIsErrorResponse(inspectResponse, AppServerErrors.InvalidParamsCode);
        Assert.Contains(
            "no longer pending",
            inspectResponse.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());
        AssertAppBindingAuditContains("binding.request.cancelled.threadArchived");
    }

    [Fact]
    public async Task ThreadArchive_DoesNotRevokeOrdinaryAppBinding()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        await ConnectAppAsync(harness);
        var bindingId = await CreateAcceptAndAttachAsync(harness, thread.Id);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.ThreadArchive,
            new { threadId = thread.Id }));
        using var archiveResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(archiveResponse);
        using var statusNotification = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsNotification(statusNotification, AppServerMethods.ThreadStatusChanged);
        Assert.Null(harness.Transport.TryReadSent());

        using var listResponse = await ExecuteAndReadResponseAsync(
            harness,
            ThreadAppBindingsList,
            new { threadId = thread.Id, includeRevoked = true });
        AppServerTestHarness.AssertIsSuccessResponse(listResponse);
        var binding = Assert.Single(listResponse.RootElement.GetProperty("result").GetProperty("bindings").EnumerateArray());
        Assert.Equal(bindingId, binding.GetProperty("bindingId").GetString());
        Assert.Equal("active", binding.GetProperty("state").GetString());
    }

    [Fact]
    public async Task SocialBindingRequestGet_ReturnsNoLongerPendingForCancelledBindCode()
    {
        const string qqAppId = "com.dotharness.channel.qq";
        var registry = new ChannelRuntimeRegistry();
        registry.Register(new RecordingChannelRuntime("qq"));
        var service = new AppBindingService([
            new SocialChannelAppBindingRuntime(
                "qq",
                "QQ",
                "Continue this thread in QQ.",
                registry)
        ]);
        using var harness = CreateHarness(service, channelRuntimeRegistry: registry);
        await InitializeChannelAdapterAsync(harness, "qq");
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());

        using var createResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingRequestCreate,
            new
            {
                threadId = thread.Id,
                appId = qqAppId,
                requestedScopes = new[] { "conversation.receive", "message.send" },
                source = "threadMenu",
                bindingKind = "socialChannel",
                socialIntent = new { channelName = "qq" }
            });
        AppServerTestHarness.AssertIsSuccessResponse(createResponse);
        var createResult = createResponse.RootElement.GetProperty("result");
        var bindingRequestId = createResult.GetProperty("bindingRequestId").GetString()!;
        var bindCode = createResult.GetProperty("handoff").GetProperty("bindCode").GetString()!;

        using var cancelResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingRequestCancel,
            new { bindingRequestId },
            expectedNotificationMethod: "thread/appBindings/changed");
        AppServerTestHarness.AssertIsSuccessResponse(cancelResponse);

        using var inspectResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingRequestGet,
            new
            {
                appId = qqAppId,
                requestToken = bindCode,
                bindCode
            });
        AppServerTestHarness.AssertIsErrorResponse(inspectResponse, AppServerErrors.InvalidParamsCode);
        Assert.Contains(
            "no longer pending",
            inspectResponse.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());
    }

    [Fact]
    public async Task SocialChannelAppList_ReflectsRuntimeReadiness()
    {
        const string qqAppId = "com.dotharness.channel.qq";
        var registry = new ChannelRuntimeRegistry();
        var service = new AppBindingService([
            new SocialChannelAppBindingRuntime(
                "qq",
                "QQ",
                "Continue this thread in QQ.",
                registry)
        ]);
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();

        using var offlineResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppList,
            new
            {
                includeDisabled = true,
                surface = AppBindingCatalogSurfaces.ThreadBinding
            });
        AppServerTestHarness.AssertIsSuccessResponse(offlineResponse);
        var offlineApp = Assert.Single(
            offlineResponse.RootElement.GetProperty("result").GetProperty("apps").EnumerateArray(),
            item => item.GetProperty("appId").GetString() == qqAppId);
        Assert.Equal("notConnected", offlineApp.GetProperty("connectionState").GetString());

        registry.Register(new RecordingChannelRuntime("qq", isReady: false));

        using var connectingResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppList,
            new
            {
                includeDisabled = true,
                forceRefresh = true,
                surface = AppBindingCatalogSurfaces.ThreadBinding
            });
        AppServerTestHarness.AssertIsSuccessResponse(connectingResponse);
        var connectingApp = Assert.Single(
            connectingResponse.RootElement.GetProperty("result").GetProperty("apps").EnumerateArray(),
            item => item.GetProperty("appId").GetString() == qqAppId);
        Assert.Equal("connecting", connectingApp.GetProperty("connectionState").GetString());

        registry.Register(new RecordingChannelRuntime("qq"));

        using var onlineResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppList,
            new
            {
                includeDisabled = true,
                forceRefresh = true,
                surface = AppBindingCatalogSurfaces.ThreadBinding
            });
        AppServerTestHarness.AssertIsSuccessResponse(onlineResponse);
        var onlineApp = Assert.Single(
            onlineResponse.RootElement.GetProperty("result").GetProperty("apps").EnumerateArray(),
            item => item.GetProperty("appId").GetString() == qqAppId);
        Assert.Equal("connected", onlineApp.GetProperty("connectionState").GetString());
    }

    [Fact]
    public async Task SocialChannelConnectionStatus_ReflectsRuntimeReadiness()
    {
        const string qqAppId = "com.dotharness.channel.qq";
        var registry = new ChannelRuntimeRegistry();
        var service = new AppBindingService([
            new SocialChannelAppBindingRuntime(
                "qq",
                "QQ",
                "Continue this thread in QQ.",
                registry)
        ]);
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();

        using var offlineResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionStatus,
            new { appId = qqAppId });
        AppServerTestHarness.AssertIsSuccessResponse(offlineResponse);
        Assert.Equal("notConnected", offlineResponse.RootElement.GetProperty("result").GetProperty("state").GetString());

        registry.Register(new RecordingChannelRuntime("qq", isReady: false));

        using var connectingResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionStatus,
            new { appId = qqAppId });
        AppServerTestHarness.AssertIsSuccessResponse(connectingResponse);
        Assert.Equal("connecting", connectingResponse.RootElement.GetProperty("result").GetProperty("state").GetString());

        registry.Register(new RecordingChannelRuntime("qq"));

        using var onlineResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppConnectionStatus,
            new { appId = qqAppId });
        AppServerTestHarness.AssertIsSuccessResponse(onlineResponse);
        Assert.Equal("connected", onlineResponse.RootElement.GetProperty("result").GetProperty("state").GetString());
    }

    [Fact]
    public async Task SocialChannelAppList_ReportsInvalidNativeToolDiagnostics()
    {
        const string qqAppId = "com.dotharness.channel.qq";
        var registry = new ChannelRuntimeRegistry();
        registry.Register(new RecordingChannelRuntime(
            "qq",
            [
                new ChannelToolDescriptor
                {
                    Name = "QQValidTool",
                    Description = "Valid native QQ tool.",
                    InputSchema = CreateCardSchema()
                },
                new ChannelToolDescriptor
                {
                    Name = "QQInvalidTool",
                    Description = string.Empty,
                    InputSchema = CreateCardSchema()
                }
            ]));
        var service = new AppBindingService([
            new SocialChannelAppBindingRuntime(
                "qq",
                "QQ",
                "Continue this thread in QQ.",
                registry)
        ]);
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();

        using var response = await ExecuteAndReadResponseAsync(
            harness,
            AppList,
            new
            {
                includeDisabled = true,
                surface = AppBindingCatalogSurfaces.ThreadBinding
            });
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var app = Assert.Single(
            response.RootElement.GetProperty("result").GetProperty("apps").EnumerateArray(),
            item => item.GetProperty("appId").GetString() == qqAppId);
        Assert.Contains(
            app.GetProperty("toolCatalog").EnumerateArray(),
            tool => tool.GetProperty("name").GetString() == "QQValidTool");
        Assert.DoesNotContain(
            app.GetProperty("toolCatalog").EnumerateArray(),
            tool => tool.GetProperty("name").GetString() == "QQInvalidTool");
        Assert.Contains(
            app.GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("code").GetString() == "InvalidChannelToolDescriptor"
                          && diagnostic.GetProperty("message").GetString()?.Contains("must declare a description") == true);
    }

    [Fact]
    public async Task SocialBindingRequestCreate_RejectsOfflineRuntime()
    {
        const string qqAppId = "com.dotharness.channel.qq";
        var registry = new ChannelRuntimeRegistry();
        var service = new AppBindingService([
            new SocialChannelAppBindingRuntime(
                "qq",
                "QQ",
                "Continue this thread in QQ.",
                registry)
        ]);
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());

        using var createResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingRequestCreate,
            new
            {
                threadId = thread.Id,
                appId = qqAppId,
                requestedScopes = new[] { "conversation.receive", "message.send" },
                source = "threadMenu",
                bindingKind = "socialChannel",
                socialIntent = new { channelName = "qq" }
            });
        AppServerTestHarness.AssertIsErrorResponse(createResponse, AppServerErrors.InvalidParamsCode);
        Assert.Contains(
            "not connected",
            createResponse.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());
    }

    [Fact]
    public async Task SocialBindingAccept_RejectsWhenRuntimeDisconnectsAfterCreate()
    {
        const string qqAppId = "com.dotharness.channel.qq";
        var registry = new ChannelRuntimeRegistry();
        registry.Register(new RecordingChannelRuntime("qq"));
        var service = new AppBindingService([
            new SocialChannelAppBindingRuntime(
                "qq",
                "QQ",
                "Continue this thread in QQ.",
                registry)
        ]);
        using var harness = CreateHarness(service, channelRuntimeRegistry: registry);
        await InitializeChannelAdapterAsync(harness, "qq");
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());

        using var createResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingRequestCreate,
            new
            {
                threadId = thread.Id,
                appId = qqAppId,
                requestedScopes = new[] { "conversation.receive", "message.send" },
                source = "threadMenu",
                bindingKind = "socialChannel",
                socialIntent = new { channelName = "qq" }
            });
        AppServerTestHarness.AssertIsSuccessResponse(createResponse);
        var bindingRequestId = createResponse.RootElement.GetProperty("result").GetProperty("bindingRequestId").GetString()!;
        var bindCode = createResponse.RootElement.GetProperty("result").GetProperty("handoff").GetProperty("bindCode").GetString()!;

        Assert.True(registry.TryRemove("qq"));

        using var acceptResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingAccept,
            new
            {
                bindingRequestId,
                requestToken = bindCode,
                grantId = "social-grant-1",
                grantedScopes = new[] { "conversation.receive", "message.send" },
                approvalMode = "channelBindCode",
                approvedBy = "9988",
                socialTarget = QqSocialTarget()
            });
        AppServerTestHarness.AssertIsErrorResponse(acceptResponse, AppServerErrors.InvalidParamsCode);
        Assert.Contains(
            "not connected",
            acceptResponse.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());
    }

    [Fact]
    public async Task SocialBindingResolve_RejectsNonChannelAndCrossChannelCallers()
    {
        const string qqAppId = "com.dotharness.channel.qq";

        using (var desktopHarness = CreateHarness(new AppBindingService()))
        {
            await desktopHarness.InitializeAsync();

            using var desktopResolveResponse = await ExecuteAndReadResponseAsync(
                desktopHarness,
                AppSocialBindingResolve,
                new
                {
                    appId = qqAppId,
                    channelName = "qq",
                    conversationKind = "group",
                    conversationId = "123456"
                });
            AppServerTestHarness.AssertIsErrorResponse(desktopResolveResponse, AppServerErrors.InvalidParamsCode);
            Assert.Contains(
                "may only be called by channel adapters",
                desktopResolveResponse.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());
        }

        using var wecomHarness = CreateHarness(new AppBindingService());
        await InitializeChannelAdapterAsync(wecomHarness, "wecom");

        using var crossChannelResolveResponse = await ExecuteAndReadResponseAsync(
            wecomHarness,
            AppSocialBindingResolve,
            new
            {
                appId = qqAppId,
                channelName = "qq",
                conversationKind = "group",
                conversationId = "123456"
            });
        AppServerTestHarness.AssertIsErrorResponse(crossChannelResolveResponse, AppServerErrors.InvalidParamsCode);
        Assert.Contains(
            "cannot resolve bindings for another channel",
            crossChannelResolveResponse.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());

        using var mismatchedAppResponse = await ExecuteAndReadResponseAsync(
            wecomHarness,
            AppSocialBindingResolve,
            new
            {
                appId = qqAppId,
                channelName = "wecom",
                conversationKind = "chat",
                conversationId = "chat-1"
            });
        AppServerTestHarness.AssertIsErrorResponse(mismatchedAppResponse, AppServerErrors.InvalidParamsCode);
        Assert.Contains(
            "appId does not match channelName",
            mismatchedAppResponse.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());
    }

    [Fact]
    public async Task SocialBindingRequestGet_RejectsNonChannelAndCrossChannelCallers()
    {
        const string qqAppId = "com.dotharness.channel.qq";
        var registry = new ChannelRuntimeRegistry();
        registry.Register(new RecordingChannelRuntime("qq"));
        var service = new AppBindingService([
            new SocialChannelAppBindingRuntime(
                "qq",
                "QQ",
                "Continue this thread in QQ.",
                registry)
        ]);

        using var qqHarness = CreateHarness(service, channelRuntimeRegistry: registry);
        await InitializeChannelAdapterAsync(qqHarness, "qq");
        var thread = await qqHarness.Service.CreateThreadAsync(CreateIdentity());

        using var createResponse = await ExecuteAndReadResponseAsync(
            qqHarness,
            AppBindingRequestCreate,
            new
            {
                threadId = thread.Id,
                appId = qqAppId,
                requestedScopes = new[] { "conversation.receive", "message.send" },
                source = "threadMenu",
                bindingKind = "socialChannel",
                socialIntent = new { channelName = "qq" }
            });
        AppServerTestHarness.AssertIsSuccessResponse(createResponse);
        var bindCode = createResponse.RootElement.GetProperty("result").GetProperty("handoff").GetProperty("bindCode").GetString()!;

        using (var desktopHarness = CreateHarness(service))
        {
            await desktopHarness.InitializeAsync();
            using var desktopInspectResponse = await ExecuteAndReadResponseAsync(
                desktopHarness,
                AppBindingRequestGet,
                new
                {
                    appId = qqAppId,
                    requestToken = bindCode,
                    bindCode
                });
            AppServerTestHarness.AssertIsErrorResponse(desktopInspectResponse, AppServerErrors.InvalidParamsCode);
            Assert.Contains(
                "may only be inspected by channel adapters",
                desktopInspectResponse.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());
        }

        using (var wecomHarness = CreateHarness(service))
        {
            await InitializeChannelAdapterAsync(wecomHarness, "wecom");
            using var crossChannelInspectResponse = await ExecuteAndReadResponseAsync(
                wecomHarness,
                AppBindingRequestGet,
                new
                {
                    appId = qqAppId,
                    requestToken = bindCode,
                    bindCode
                });
            AppServerTestHarness.AssertIsErrorResponse(crossChannelInspectResponse, AppServerErrors.InvalidParamsCode);
            Assert.Contains(
                "cannot inspect binding requests for another channel",
                crossChannelInspectResponse.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());
        }

        using var qqInspectResponse = await ExecuteAndReadResponseAsync(
            qqHarness,
            AppBindingRequestGet,
            new
            {
                appId = qqAppId,
                requestToken = bindCode,
                bindCode
            });
        AppServerTestHarness.AssertIsSuccessResponse(qqInspectResponse);
        Assert.Equal("socialChannel", qqInspectResponse.RootElement.GetProperty("result").GetProperty("bindingKind").GetString());
    }

    [Fact]
    public async Task SocialBindingAccept_RejectsNonChannelAndCrossChannelCallers()
    {
        using (var desktopHarness = CreateHarness(new AppBindingService()))
        {
            await desktopHarness.InitializeAsync();

            using var desktopAcceptResponse = await ExecuteAndReadResponseAsync(
                desktopHarness,
                AppBindingAccept,
                new
                {
                    requestToken = "DTC-123456",
                    grantId = "social-grant-1",
                    grantedScopes = new[] { "conversation.receive", "message.send" },
                    approvalMode = "channelBindCode",
                    socialTarget = QqSocialTarget()
                });
            AppServerTestHarness.AssertIsErrorResponse(desktopAcceptResponse, AppServerErrors.InvalidParamsCode);
            Assert.Contains(
                "may only be accepted by channel adapters",
                desktopAcceptResponse.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());
        }

        using var wecomHarness = CreateHarness(new AppBindingService());
        await InitializeChannelAdapterAsync(wecomHarness, "wecom");

        using var crossChannelAcceptResponse = await ExecuteAndReadResponseAsync(
            wecomHarness,
            AppBindingAccept,
            new
            {
                requestToken = "DTC-123456",
                grantId = "social-grant-1",
                grantedScopes = new[] { "conversation.receive", "message.send" },
                approvalMode = "channelBindCode",
                socialTarget = QqSocialTarget()
            });
        AppServerTestHarness.AssertIsErrorResponse(crossChannelAcceptResponse, AppServerErrors.InvalidParamsCode);
        Assert.Contains(
            "cannot accept bindings for another channel",
            crossChannelAcceptResponse.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ThreadInputEnqueue_FromSocialBindingDeliversCompletedReplyToBoundTarget()
    {
        const string qqAppId = "com.dotharness.channel.qq";
        var registry = new ChannelRuntimeRegistry();
        var runtime = new RecordingChannelRuntime("qq");
        registry.Register(runtime);
        var service = new AppBindingService([
            new SocialChannelAppBindingRuntime(
                "qq",
                "QQ",
                "Continue this thread in QQ.",
                registry)
        ]);
        using var harness = CreateHarness(service, channelRuntimeRegistry: registry);
        await InitializeChannelAdapterAsync(harness, "qq");
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());

        using var createResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingRequestCreate,
            new
            {
                threadId = thread.Id,
                appId = qqAppId,
                requestedScopes = new[] { "conversation.receive", "message.send" },
                source = "threadMenu",
                bindingKind = "socialChannel",
                socialIntent = new { channelName = "qq" }
            });
        var bindingRequestId = createResponse.RootElement.GetProperty("result").GetProperty("bindingRequestId").GetString()!;
        var bindCode = createResponse.RootElement.GetProperty("result").GetProperty("handoff").GetProperty("bindCode").GetString()!;

        using var acceptResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingAccept,
            new
            {
                bindingRequestId,
                requestToken = bindCode,
                grantId = "social-grant-1",
                grantedScopes = new[] { "conversation.receive", "message.send" },
                approvalMode = "channelBindCode",
                approvedBy = "9988",
                socialTarget = QqSocialTarget()
            },
            expectedNotificationMethod: "thread/appBindings/changed");
        AppServerTestHarness.AssertIsSuccessResponse(acceptResponse);
        var bindingId = acceptResponse.RootElement.GetProperty("result").GetProperty("binding").GetProperty("bindingId").GetString()!;

        using var enqueueResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppThreadInputEnqueue,
            new
            {
                bindingId,
                appId = qqAppId,
                grantId = "social-grant-1",
                input = new[] { new { type = "text", text = "hello from qq" } },
                sender = new
                {
                    senderId = "9988",
                    senderName = "Ada",
                    senderRole = "admin",
                    groupId = "group:123456"
                },
                startPolicy = "runWhenIdle"
            });
        AppServerTestHarness.AssertIsSuccessResponse(enqueueResponse);
        var queuedInputId = enqueueResponse.RootElement.GetProperty("result").GetProperty("queuedInput").GetProperty("id").GetString()!;
        Assert.Equal(bindingId, harness.Service.LastStartedQueuedInput?.DeliveryBindingId);

        await harness.Service.WaitForThreadSubscriberAsync(thread.Id, TimeSpan.FromSeconds(2));
        var input = new SessionItem
        {
            Id = "item_input",
            TurnId = "turn_001",
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new UserMessagePayload
            {
                Text = "hello from qq",
                DeliveryMode = "queued",
                QueuedInputId = queuedInputId,
                DeliveryBindingId = bindingId
            }
        };
        var turn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = thread.Id,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            Input = input,
            Items = [input]
        };
        var completed = new SessionTurn
        {
            Id = turn.Id,
            ThreadId = thread.Id,
            Status = TurnStatus.Completed,
            StartedAt = turn.StartedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            Input = input,
            Items =
            [
                input,
                new SessionItem
                {
                    Id = "item_agent",
                    TurnId = turn.Id,
                    Type = ItemType.AgentMessage,
                    Status = ItemStatus.Completed,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Payload = new AgentMessagePayload { Text = "reply to bound qq" }
                }
            ]
        };
        harness.Service.PublishThreadEvent(new SessionEvent
        {
            EventId = "evt_1",
            EventType = SessionEventType.TurnStarted,
            ThreadId = thread.Id,
            TurnId = turn.Id,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = turn
        });
        harness.Service.PublishThreadEvent(new SessionEvent
        {
            EventId = "evt_2",
            EventType = SessionEventType.TurnCompleted,
            ThreadId = thread.Id,
            TurnId = turn.Id,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = completed
        });

        var delivery = await runtime.WaitForDeliveryAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("group:123456", delivery.Target);
        Assert.Equal("text", delivery.Message.Kind);
        Assert.Equal("reply to bound qq", delivery.Message.Text);
        await WaitForAppBindingAuditContainsAsync("binding.socialDelivery.delivered");
    }

    [Fact]
    public async Task ThreadInputEnqueue_FromSocialBindingRecordsObserverFailureDiagnostic()
    {
        const string qqAppId = "com.dotharness.channel.qq";
        var registry = new ChannelRuntimeRegistry();
        registry.Register(new RecordingChannelRuntime("qq", throwOnDeliver: true));
        var service = new AppBindingService([
            new SocialChannelAppBindingRuntime(
                "qq",
                "QQ",
                "Continue this thread in QQ.",
                registry)
        ]);
        using var harness = CreateHarness(service, channelRuntimeRegistry: registry);
        await InitializeChannelAdapterAsync(harness, "qq");
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        var bindingId = await CreateAcceptedQqSocialBindingAsync(harness, qqAppId, thread.Id);

        using var enqueueResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppThreadInputEnqueue,
            new
            {
                bindingId,
                appId = qqAppId,
                grantId = "social-grant-1",
                input = new[] { new { type = "text", text = "hello from qq" } },
                sender = new
                {
                    senderId = "9988",
                    senderName = "Ada",
                    groupId = "group:123456"
                },
                startPolicy = "runWhenIdle"
            });
        AppServerTestHarness.AssertIsSuccessResponse(enqueueResponse);
        var queuedInputId = enqueueResponse.RootElement.GetProperty("result").GetProperty("queuedInput").GetProperty("id").GetString()!;

        await harness.Service.WaitForThreadSubscriberAsync(thread.Id, TimeSpan.FromSeconds(2));
        var input = new SessionItem
        {
            Id = "item_input",
            TurnId = "turn_delivery_failure",
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new UserMessagePayload
            {
                Text = "hello from qq",
                DeliveryMode = "queued",
                QueuedInputId = queuedInputId,
                DeliveryBindingId = bindingId
            }
        };
        var turn = new SessionTurn
        {
            Id = "turn_delivery_failure",
            ThreadId = thread.Id,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            Input = input,
            Items = [input]
        };
        var completed = new SessionTurn
        {
            Id = turn.Id,
            ThreadId = thread.Id,
            Status = TurnStatus.Completed,
            StartedAt = turn.StartedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            Input = input,
            Items =
            [
                input,
                new SessionItem
                {
                    Id = "item_agent",
                    TurnId = turn.Id,
                    Type = ItemType.AgentMessage,
                    Status = ItemStatus.Completed,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Payload = new AgentMessagePayload { Text = "reply that will fail delivery" }
                }
            ]
        };
        harness.Service.PublishThreadEvent(new SessionEvent
        {
            EventId = "evt_delivery_failure_started",
            EventType = SessionEventType.TurnStarted,
            ThreadId = thread.Id,
            TurnId = turn.Id,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = turn
        });
        harness.Service.PublishThreadEvent(new SessionEvent
        {
            EventId = "evt_delivery_failure_completed",
            EventType = SessionEventType.TurnCompleted,
            ThreadId = thread.Id,
            TurnId = turn.Id,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = completed
        });

        await WaitForAppBindingAuditContainsAsync(
            "binding.socialDelivery.failed",
            "deliveryObserverFailed:InvalidOperationException");
    }

    [Fact]
    public async Task RefreshBindings_MarksActiveSocialBindingOfflineWhenRuntimeUnavailable()
    {
        const string qqAppId = "com.dotharness.channel.qq";
        var registry = new ChannelRuntimeRegistry();
        registry.Register(new RecordingChannelRuntime("qq"));
        var service = new AppBindingService([
            new SocialChannelAppBindingRuntime(
                "qq",
                "QQ",
                "Continue this thread in QQ.",
                registry)
        ]);
        using var harness = CreateHarness(service, channelRuntimeRegistry: registry);
        await InitializeChannelAdapterAsync(harness, "qq");
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        var bindingId = await CreateAcceptedQqSocialBindingAsync(harness, qqAppId, thread.Id);

        Assert.True(registry.TryRemove("qq"));

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
        Assert.Equal("notConnected", binding.GetProperty("connectionState").GetString());

        registry.Register(new RecordingChannelRuntime("qq"));

        using var reattachResponse = await ExecuteAndReadResponseAsync(
            harness,
            ThreadAppBindingsRefresh,
            new { threadId = thread.Id, bindingId },
            expectedNotificationMethod: "thread/appBindings/changed");
        AppServerTestHarness.AssertIsSuccessResponse(reattachResponse);
        var reattached = Assert.Single(reattachResponse.RootElement.GetProperty("result").GetProperty("bindings").EnumerateArray());
        Assert.Equal("active", reattached.GetProperty("state").GetString());
    }

    [Fact]
    public async Task ThreadInputEnqueue_FromSocialBindingRejectsOfflineRuntime()
    {
        const string qqAppId = "com.dotharness.channel.qq";
        var registry = new ChannelRuntimeRegistry();
        registry.Register(new RecordingChannelRuntime("qq"));
        var service = new AppBindingService([
            new SocialChannelAppBindingRuntime(
                "qq",
                "QQ",
                "Continue this thread in QQ.",
                registry)
        ]);
        using var harness = CreateHarness(service, channelRuntimeRegistry: registry);
        await InitializeChannelAdapterAsync(harness, "qq");
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        var bindingId = await CreateAcceptedQqSocialBindingAsync(harness, qqAppId, thread.Id);

        Assert.True(registry.TryRemove("qq"));

        using var enqueueResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppThreadInputEnqueue,
            new
            {
                bindingId,
                appId = qqAppId,
                grantId = "social-grant-1",
                input = new[] { new { type = "text", text = "hello from offline qq" } },
                startPolicy = "queueOnly"
            });
        AppServerTestHarness.AssertIsErrorResponse(enqueueResponse, AppServerErrors.InvalidParamsCode);
        Assert.Contains(
            "not connected",
            enqueueResponse.RootElement.GetProperty("error").GetProperty("data").GetProperty("detail").GetString());
    }

    [Fact]
    public async Task SocialBindingResolve_ReturnsNullWhenRuntimeUnavailable()
    {
        const string qqAppId = "com.dotharness.channel.qq";
        var registry = new ChannelRuntimeRegistry();
        registry.Register(new RecordingChannelRuntime("qq"));
        var service = new AppBindingService([
            new SocialChannelAppBindingRuntime(
                "qq",
                "QQ",
                "Continue this thread in QQ.",
                registry)
        ]);
        using var harness = CreateHarness(service, channelRuntimeRegistry: registry);
        await InitializeChannelAdapterAsync(harness, "qq");
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        await CreateAcceptedQqSocialBindingAsync(harness, qqAppId, thread.Id);

        Assert.True(registry.TryRemove("qq"));

        using var resolveResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppSocialBindingResolve,
            new
            {
                appId = qqAppId,
                channelName = "qq",
                conversationKind = "group",
                conversationId = "123456"
            });
        AppServerTestHarness.AssertIsSuccessResponse(resolveResponse);
        Assert.Equal(JsonValueKind.Null, resolveResponse.RootElement.GetProperty("result").GetProperty("binding").ValueKind);
    }

    [Fact]
    public async Task OfflineSocialBinding_KeepsToolStubAndFailsBeforeDispatch()
    {
        const string qqAppId = "com.dotharness.channel.qq";
        var registry = new ChannelRuntimeRegistry();
        registry.Register(new RecordingChannelRuntime("qq"));
        var service = new AppBindingService([
            new SocialChannelAppBindingRuntime(
                "qq",
                "QQ",
                "Continue this thread in QQ.",
                registry)
        ]);
        using var harness = CreateHarness(service, channelRuntimeRegistry: registry);
        await InitializeChannelAdapterAsync(harness, "qq");
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        var bindingId = await CreateAcceptedQqSocialBindingAsync(harness, qqAppId, thread.Id);

        Assert.True(registry.TryRemove("qq"));
        using var refreshResponse = await ExecuteAndReadResponseAsync(
            harness,
            ThreadAppBindingsRefresh,
            new { threadId = thread.Id, bindingId },
            expectedNotificationMethod: "thread/appBindings/changed");
        AppServerTestHarness.AssertIsSuccessResponse(refreshResponse);

        var tool = Assert.Single(
            service.CreateRuntimeToolsForThread(thread, new HashSet<string>(StringComparer.Ordinal))
                .OfType<AIFunction>(),
            candidate => candidate.Name == "SendMessageToBoundConversation");
        var turn = AppServerTestHarness.MakeTurn(thread.Id);
        var seq = 0;
        using var scope = PluginFunctionExecutionScope.Set(new PluginFunctionExecutionContext
        {
            ThreadId = thread.Id,
            TurnId = turn.Id,
            OriginChannel = "desktop",
            WorkspacePath = thread.WorkspacePath,
            RequireApprovalOutsideWorkspace = false,
            ApprovalService = new AutoApproveApprovalService(),
            Turn = turn,
            SessionService = harness.Service,
            NextItemSequence = () => ++seq,
            EmitItemStarted = _ => { },
            EmitItemCompleted = _ => { }
        });

        await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["text"] = "hello while offline"
        }));

        var payload = Assert.IsType<DynamicToolCallPayload>(Assert.Single(turn.Items).Payload);
        Assert.False(payload.Success);
        Assert.Equal(AppBindingErrorCodes.Offline, payload.ErrorCode);
    }

    [Fact]
    public async Task SocialChannelAppBoundTool_UsesBindingTargetForNativeChannelTools()
    {
        const string qqAppId = "com.dotharness.channel.qq";
        var registry = new ChannelRuntimeRegistry();
        var runtime = new RecordingChannelRuntime(
            "qq",
            [
                new ChannelToolDescriptor
                {
                    Name = "QQSendImageToCurrentChat",
                    Description = "Send an image to the current QQ chat.",
                    RequiresChatContext = true,
                    InputSchema = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["fileName"] = new JsonObject { ["type"] = "string" }
                        },
                        ["required"] = new JsonArray("fileName")
                    }
                }
            ]);
        registry.Register(runtime);
        var service = new AppBindingService([
            new SocialChannelAppBindingRuntime(
                "qq",
                "QQ",
                "Continue this thread in QQ.",
                registry)
        ]);
        using var harness = CreateHarness(service, channelRuntimeRegistry: registry);
        await InitializeChannelAdapterAsync(harness, "qq");
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        await CreateAcceptedQqSocialBindingAsync(harness, qqAppId, thread.Id);

        var tools = service.CreateRuntimeToolsForThread(thread, new HashSet<string>(StringComparer.Ordinal))
            .OfType<AIFunction>()
            .ToList();
        Assert.Contains(tools, tool => tool.Name == "SendMessageToBoundConversation");
        var nativeTool = Assert.Single(tools, tool => tool.Name == "QQSendImageToCurrentChat");

        var turn = AppServerTestHarness.MakeTurn(thread.Id);
        var seq = 0;
        using var scope = PluginFunctionExecutionScope.Set(new PluginFunctionExecutionContext
        {
            ThreadId = thread.Id,
            TurnId = turn.Id,
            OriginChannel = "desktop",
            ChannelContext = "desktop-context-must-not-be-used",
            SenderId = "desktop-user",
            GroupId = "desktop-group",
            WorkspacePath = thread.WorkspacePath,
            RequireApprovalOutsideWorkspace = false,
            ApprovalService = new AutoApproveApprovalService(),
            Turn = turn,
            SessionService = harness.Service,
            NextItemSequence = () => ++seq,
            EmitItemStarted = _ => { },
            EmitItemCompleted = _ => { }
        });

        await nativeTool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["fileName"] = "photo.png"
        }));

        var call = await runtime.WaitForToolCallAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("QQSendImageToCurrentChat", call.Tool);
        Assert.Equal("photo.png", call.Arguments["fileName"]?.GetValue<string>());
        Assert.Equal("qq", call.Context.ChannelName);
        Assert.Equal("group:123456", call.Context.ChannelContext);
        Assert.Equal("group:123456", call.Context.GroupId);
        Assert.Equal("9988", call.Context.SenderId);

        var payload = Assert.IsType<DynamicToolCallPayload>(Assert.Single(turn.Items).Payload);
        Assert.True(payload.Success);
        Assert.Equal("native tool ok", Assert.Single(payload.ContentItems!).Text);
    }

    [Fact]
    public async Task SocialChannelAppBoundTool_RejectsNativeToolTargetOverride()
    {
        const string qqAppId = "com.dotharness.channel.qq";
        var registry = new ChannelRuntimeRegistry();
        var runtime = new RecordingChannelRuntime(
            "qq",
            [
                new ChannelToolDescriptor
                {
                    Name = "QQSendImageToCurrentChat",
                    Description = "Send an image to the current QQ chat.",
                    RequiresChatContext = true,
                    InputSchema = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["fileName"] = new JsonObject { ["type"] = "string" },
                            ["chatId"] = new JsonObject { ["type"] = "string" }
                        },
                        ["required"] = new JsonArray("fileName")
                    }
                }
            ]);
        registry.Register(runtime);
        var service = new AppBindingService([
            new SocialChannelAppBindingRuntime(
                "qq",
                "QQ",
                "Continue this thread in QQ.",
                registry)
        ]);
        using var harness = CreateHarness(service, channelRuntimeRegistry: registry);
        await InitializeChannelAdapterAsync(harness, "qq");
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        await CreateAcceptedQqSocialBindingAsync(harness, qqAppId, thread.Id);

        var nativeTool = Assert.Single(
            service.CreateRuntimeToolsForThread(thread, new HashSet<string>(StringComparer.Ordinal))
                .OfType<AIFunction>(),
            tool => tool.Name == "QQSendImageToCurrentChat");

        var turn = AppServerTestHarness.MakeTurn(thread.Id);
        var seq = 0;
        using var scope = PluginFunctionExecutionScope.Set(new PluginFunctionExecutionContext
        {
            ThreadId = thread.Id,
            TurnId = turn.Id,
            OriginChannel = "desktop",
            WorkspacePath = thread.WorkspacePath,
            RequireApprovalOutsideWorkspace = false,
            ApprovalService = new AutoApproveApprovalService(),
            Turn = turn,
            SessionService = harness.Service,
            NextItemSequence = () => ++seq,
            EmitItemStarted = _ => { },
            EmitItemCompleted = _ => { }
        });

        await nativeTool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["fileName"] = "photo.png",
            ["chatId"] = "group:999999"
        }));

        Assert.Null(runtime.LastToolCall);
        var payload = Assert.IsType<DynamicToolCallPayload>(Assert.Single(turn.Items).Payload);
        Assert.False(payload.Success);
        Assert.Equal(AppBindingErrorCodes.ProtocolViolation, payload.ErrorCode);
        Assert.Contains("cannot override the bound social target", payload.ErrorMessage);
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
    public async Task UiHostMethod_RejectedWhenClientDidNotNegotiateInteractiveToolUi()
    {
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync(); // interactiveToolUi defaults to false

        using var response = await ExecuteAndReadResponseAsync(
            harness,
            "ui/open-link",
            new { threadId = "thread-1", url = "https://example.com" });

        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.MethodNotFoundCode);
    }

    [Fact]
    public async Task UiHostMethod_PassesNegotiationGateWhenInteractiveToolUiDeclared()
    {
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync(interactiveToolUi: true);
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());

        using var response = await ExecuteAndReadResponseAsync(
            harness,
            "ui/open-link",
            new { threadId = thread.Id, url = "https://example.com" });

        // The negotiation gate opens for a declaring client: the method is recognized and
        // proceeds to the host scheme policy — it is not rejected as MethodNotFound.
        if (response.RootElement.TryGetProperty("error", out var error))
            Assert.NotEqual(AppServerErrors.MethodNotFoundCode, error.GetProperty("code").GetInt32());
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

    // Reproduces the oratorio-bridge worktree case: the declaring app is a bundled built-in
    // (installable, not workspace-installed in that thread's workspace). Origin branding is cosmetic and
    // must still resolve, otherwise such threads fall back to the generic channel icon.
    [Fact]
    public void ResolveOriginApp_AttributesBundledBuiltInApp_NotJustWorkspaceInstalled()
    {
        var builtInRoot = Path.Combine(_tempRoot, "builtin");
        WriteOratorioPlugin(originChannel: "oratorio", containerRoot: builtInRoot);
        var service = new AppBindingService();
        var catalog = AppBindingCatalog.Discover(
            new AppConfig(), _tempRoot, _workspaceCraftPath, builtInPluginSourceRoots: [builtInRoot]);

        var entry = catalog.Entries.FirstOrDefault(e => e.Descriptor.AppId == "com.dotharness.oratorio");
        Assert.NotNull(entry);
        Assert.False(entry!.Plugin.Installed);
        Assert.True(entry.Plugin.Installable);

        var origin = service.ResolveOriginApp(catalog, "oratorio");

        Assert.NotNull(origin);
        Assert.Equal("com.dotharness.oratorio", origin!.AppId);
        Assert.Equal("Oratorio", origin.DisplayName);
        Assert.NotNull(origin.Icon);
        Assert.StartsWith("data:image/svg+xml;base64,", origin.Icon!);
    }

    // Reproduces the live Teams setup: managed TeamsService runtime + agent-teams plugin assets,
    // mission member threads stamped channelContext="{missionId}:{memberId}".
    [Theory]
    [InlineData("leader", "Team Leader")]
    [InlineData("explorer", "Explorer")]
    [InlineData("builder", "Builder")]
    [InlineData("reviewer", "Reviewer")]
    [InlineData("operator", "Operator")]
    public void ResolveOriginApp_ManagedTeamsRuntime_BrandsEveryMemberRole(string memberId, string expectedDisplayName)
    {
        WriteAgentTeamsPlugin();
        var service = new AppBindingService(new IManagedAppBindingRuntime[] { new DotCraft.Teams.TeamsService() });
        var catalog = service.DiscoverCatalog(new AppConfig(), _tempRoot, _workspaceCraftPath);

        var origin = service.ResolveOriginApp(
            catalog,
            "teams",
            $"mission_76e5319b02454d37a6cd5c9cf8435133:{memberId}");

        Assert.NotNull(origin);
        Assert.Equal(memberId, origin!.MemberId);
        Assert.Equal(expectedDisplayName, origin.DisplayName);
        Assert.NotNull(origin.Icon);
        Assert.StartsWith("data:image/svg+xml;base64,", origin.Icon!);
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
        IContextPageManager? contextPageManager = null,
        IChannelRuntimeRegistry? channelRuntimeRegistry = null)
    {
        config ??= new AppConfig();
        var monitor = new AppConfigMonitor(config);
        return new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            protocolExtensions:
            [
                new AppBindingProtocolExtension(
                    service,
                    monitor,
                    builtInPluginSourceRoots: [BundledPluginSourceRoot()],
                    channelRuntimeRegistry: channelRuntimeRegistry)
            ],
            appConfigMonitor: monitor,
            appBindingService: service,
            contextPageManager: contextPageManager,
            builtInPluginSourceRoots: [BundledPluginSourceRoot()]);
    }

    private static async Task InitializeChannelAdapterAsync(AppServerTestHarness harness, string channelName)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.Initialize,
            new
            {
                clientInfo = new { name = $"{channelName}-adapter", version = "0.0.1" },
                capabilities = new
                {
                    approvalSupport = true,
                    streamingSupport = true,
                    channelAdapter = new
                    {
                        channelName,
                        deliverySupport = true
                    }
                }
            }));
        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        harness.Handler.HandleInitializedNotification();
    }

    private static async Task<string> CreateAcceptedQqSocialBindingAsync(
        AppServerTestHarness harness,
        string qqAppId,
        string threadId)
    {
        using var createResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingRequestCreate,
            new
            {
                threadId,
                appId = qqAppId,
                requestedScopes = new[] { "conversation.receive", "message.send" },
                source = "threadMenu",
                bindingKind = "socialChannel",
                socialIntent = new { channelName = "qq" }
            });
        AppServerTestHarness.AssertIsSuccessResponse(createResponse);
        var bindingRequestId = createResponse.RootElement.GetProperty("result").GetProperty("bindingRequestId").GetString()!;
        var bindCode = createResponse.RootElement.GetProperty("result").GetProperty("handoff").GetProperty("bindCode").GetString()!;

        using var acceptResponse = await ExecuteAndReadResponseAsync(
            harness,
            AppBindingAccept,
            new
            {
                bindingRequestId,
                requestToken = bindCode,
                grantId = "social-grant-1",
                grantedScopes = new[] { "conversation.receive", "message.send" },
                approvalMode = "channelBindCode",
                approvedBy = "9988",
                socialTarget = QqSocialTarget()
            },
            expectedNotificationMethod: "thread/appBindings/changed");
        AppServerTestHarness.AssertIsSuccessResponse(acceptResponse);
        return acceptResponse.RootElement.GetProperty("result").GetProperty("binding").GetProperty("bindingId").GetString()!;
    }

    private static object QqSocialTarget() => new
    {
        channelName = "qq",
        conversationKind = "group",
        conversationId = "123456",
        deliveryTarget = "group:123456",
        displayName = "QQ group 123456",
        boundBy = new
        {
            platformUserId = "9988",
            displayName = "Ada"
        }
    };

    private sealed class RecordingChannelRuntime(
        string name,
        IReadOnlyList<ChannelToolDescriptor>? channelTools = null,
        bool isReady = true,
        bool throwOnDeliver = false) : IChannelRuntime
    {
        private readonly TaskCompletionSource<RecordedDelivery> _delivery =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<ExtChannelToolCallParams> _toolCall =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name { get; } = name;

        public bool IsReady { get; } = isReady;

        public ExtChannelToolCallParams? LastToolCall { get; private set; }

        public IReadOnlyList<ChannelToolDescriptor> GetChannelTools() => channelTools ?? [];

        public Task<ExtChannelSendResult> DeliverAsync(
            string target,
            ChannelOutboundMessage message,
            object? metadata = null,
            CancellationToken cancellationToken = default)
        {
            if (throwOnDeliver)
                throw new InvalidOperationException("Synthetic delivery failure.");

            _delivery.TrySetResult(new RecordedDelivery(target, message, metadata));
            return Task.FromResult(new ExtChannelSendResult
            {
                Delivered = true,
                RemoteMessageId = "remote-1"
            });
        }

        public Task<ExtChannelToolCallResult> ExecuteToolAsync(
            ExtChannelToolCallParams request,
            CancellationToken cancellationToken = default)
        {
            LastToolCall = request;
            _toolCall.TrySetResult(request);
            return Task.FromResult(new ExtChannelToolCallResult
            {
                Success = true,
                ContentItems = [new ExtChannelToolContentItem { Type = "text", Text = "native tool ok" }]
            });
        }

        public async Task<RecordedDelivery> WaitForDeliveryAsync(TimeSpan timeout)
        {
            var completed = await Task.WhenAny(_delivery.Task, Task.Delay(timeout));
            if (completed != _delivery.Task)
                throw new TimeoutException("Timed out waiting for channel delivery.");
            return await _delivery.Task;
        }

        public async Task<ExtChannelToolCallParams> WaitForToolCallAsync(TimeSpan timeout)
        {
            var completed = await Task.WhenAny(_toolCall.Task, Task.Delay(timeout));
            if (completed != _toolCall.Task)
                throw new TimeoutException("Timed out waiting for channel tool call.");
            return await _toolCall.Task;
        }
    }

    private sealed record RecordedDelivery(
        string Target,
        ChannelOutboundMessage Message,
        object? Metadata);

    [Fact]
    public async Task UiResourceRead_BrokersDeclaredResourceToAppAndRejectsUndeclaredUri()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        await ConnectAppAsync(harness);
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        await CreateAcceptAndAttachUiToolAsync(harness, thread.Id);

        harness.Transport.DrainSent();
        harness.Transport.ApprovalHandler = (method, _) =>
        {
            Assert.Equal(AppServerMethods.ItemResourceRead, method);
            return InMemoryTransport.BuildClientResponse(1, new UiResourceReadResult
            {
                Contents =
                [
                    new UiResourceContent
                    {
                        Uri = "ui://oratorio/board",
                        MimeType = "text/html;profile=mcp-app",
                        Text = "<!doctype html><body>board</body>"
                    }
                ]
            });
        };

        var result = await service.ReadUiResourceAsync(_workspaceCraftPath, thread.Id, "oratorio", "ui://oratorio/board", default);
        var content = Assert.Single(result.Contents);
        Assert.Equal("ui://oratorio/board", content.Uri);
        Assert.Equal("text/html;profile=mcp-app", content.MimeType);
        Assert.Contains("board", content.Text);

        await Assert.ThrowsAsync<AppServerException>(() =>
            service.ReadUiResourceAsync(_workspaceCraftPath, thread.Id, "oratorio", "ui://oratorio/missing", default).AsTask());
    }

    [Fact]
    public async Task UiToolCall_DispatchesAppVisibleToolDecoupledFromConversation()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        await ConnectAppAsync(harness);
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        await CreateAcceptAndAttachUiToolAsync(harness, thread.Id);

        harness.Transport.DrainSent();
        harness.Transport.ApprovalHandler = (method, _) =>
        {
            Assert.Equal(AppServerMethods.ItemToolCall, method);
            return InMemoryTransport.BuildClientResponse(1, new DynamicToolCallResult
            {
                Success = true,
                StructuredResult = JsonNode.Parse("""{"cardId":"card-9"}"""),
                Meta = JsonNode.Parse("""{"ui":{"open":true}}""")
            });
        };

        var result = await service.InvokeUiToolAsync(
            _workspaceCraftPath,
            thread.Id,
            "oratorio",
            "CreateCard",
            new JsonObject { ["title"] = "From UI" },
            sourceCallId: "dyntool_1",
            userId: "test_user",
            sessionService: harness.Service,
            approvalGate: null,
            default);

        Assert.True(result.Success);
        Assert.Equal("card-9", result.StructuredResult?["cardId"]?.GetValue<string>());
        Assert.True(result.Meta?["ui"]?["open"]?.GetValue<bool>());

        // Decoupled: no conversation turn/item is created for a UI-initiated tool call.
        var reread = await harness.Service.GetThreadAsync(thread.Id, default);
        Assert.Empty(reread.Turns);

        // …but it is recorded on the audit trail.
        AssertAppBindingAuditContains("binding.uiToolCall");
    }

    [Fact]
    public async Task UiOpenLink_RecordedOnAuditTrail()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync(interactiveToolUi: true);
        await ConnectAppAsync(harness);
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        await CreateAcceptAndAttachUiToolAsync(harness, thread.Id);

        using var response = await ExecuteAndReadResponseAsync(
            harness,
            "ui/open-link",
            new { threadId = thread.Id, @namespace = "oratorio", url = "https://example.com", sourceCallId = "dyntool_1" });

        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.Equal("https://example.com", response.RootElement.GetProperty("result").GetProperty("url").GetString());
        // Every UI-initiated link open is recorded on the App Binding audit trail.
        AssertAppBindingAuditContains("binding.uiOpenLink");
    }

    [Fact]
    public async Task UiToolCall_RejectsToolNotExposedToUi()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        await ConnectAppAsync(harness);
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        await CreateAcceptAndAttachAsync(harness, thread.Id); // CreateCard attached without _meta.ui

        var result = await service.InvokeUiToolAsync(
            _workspaceCraftPath,
            thread.Id,
            "oratorio",
            "CreateCard",
            new JsonObject { ["title"] = "From UI" },
            sourceCallId: null,
            userId: "test_user",
            sessionService: harness.Service,
            approvalGate: null,
            default);

        Assert.False(result.Success);
        Assert.Equal(AppBindingErrorCodes.ToolUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task UiToolCall_RejectsMutatingToolWithoutApprovalGate()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        await ConnectAppAsync(harness);
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        await CreateAcceptAndAttachUiToolAsync(harness, thread.Id, withApproval: true);

        // No approval gate (e.g. a non-Desktop client that cannot prompt) → reject the mutating call.
        var result = await service.InvokeUiToolAsync(
            _workspaceCraftPath,
            thread.Id,
            "oratorio",
            "CreateCard",
            new JsonObject { ["title"] = "From UI" },
            sourceCallId: "dyntool_1",
            userId: "test_user",
            sessionService: harness.Service,
            approvalGate: null,
            default);

        Assert.False(result.Success);
        Assert.Equal(AppBindingErrorCodes.ApprovalRequired, result.ErrorCode);
        Assert.Empty((await harness.Service.GetThreadAsync(thread.Id, default)).Turns);
    }

    [Fact]
    public async Task UiToolCall_MutatingToolDispatchesWhenApproved()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        await ConnectAppAsync(harness);
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        await CreateAcceptAndAttachUiToolAsync(harness, thread.Id, withApproval: true);

        harness.Transport.DrainSent();
        harness.Transport.ApprovalHandler = (method, _) =>
        {
            Assert.Equal(AppServerMethods.ItemToolCall, method);
            return InMemoryTransport.BuildClientResponse(1, new DynamicToolCallResult
            {
                Success = true,
                StructuredResult = JsonNode.Parse("""{"queued":true}""")
            });
        };

        UiToolApprovalInfo? seen = null;
        UiToolApprovalGate gate = (info, _) =>
        {
            seen = info;
            return new ValueTask<bool>(true);
        };

        var result = await service.InvokeUiToolAsync(
            _workspaceCraftPath,
            thread.Id,
            "oratorio",
            "CreateCard",
            new JsonObject { ["title"] = "From UI" },
            sourceCallId: "dyntool_1",
            userId: "test_user",
            sessionService: harness.Service,
            approvalGate: gate,
            default);

        Assert.True(result.Success);
        Assert.True(result.StructuredResult?["queued"]?.GetValue<bool>());
        // The approval prompt derived operation/target from the descriptor + arguments.
        Assert.NotNull(seen);
        Assert.Equal("create", seen!.Operation);
        Assert.Equal("From UI", seen.Target);
        Assert.Empty((await harness.Service.GetThreadAsync(thread.Id, default)).Turns);
        AssertAppBindingAuditContains("binding.uiToolApproval.accepted");
    }

    [Fact]
    public async Task UiToolCall_MutatingToolRejectedWhenDeclined()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        await ConnectAppAsync(harness);
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        await CreateAcceptAndAttachUiToolAsync(harness, thread.Id, withApproval: true);

        UiToolApprovalGate decline = (_, _) => new ValueTask<bool>(false);

        var result = await service.InvokeUiToolAsync(
            _workspaceCraftPath,
            thread.Id,
            "oratorio",
            "CreateCard",
            new JsonObject { ["title"] = "From UI" },
            sourceCallId: "dyntool_1",
            userId: "test_user",
            sessionService: harness.Service,
            approvalGate: decline,
            default);

        Assert.False(result.Success);
        Assert.Equal(AppBindingErrorCodes.ApprovalDeclined, result.ErrorCode);
        Assert.Empty((await harness.Service.GetThreadAsync(thread.Id, default)).Turns);
        AssertAppBindingAuditContains("binding.uiToolApproval.declined");
    }

    [Fact]
    public async Task UiOpenLink_AllowsHttpsMailtoAndBoundAppProtocolRejectsOtherSchemes()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        await ConnectAppAsync(harness);
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        await CreateAcceptAndAttachUiToolAsync(harness, thread.Id);
        var catalog = AppBindingCatalog.Discover(new AppConfig(), _tempRoot, _workspaceCraftPath);

        var https = service.OpenLink(
            catalog, _workspaceCraftPath, thread.Id, "oratorio", "https://oratorio.example/board/1", "dyntool_1", "test_user");
        Assert.Equal("https://oratorio.example/board/1", https.Url);

        var mailto = service.OpenLink(
            catalog, _workspaceCraftPath, thread.Id, "oratorio", "mailto:team@example.com", null, "test_user");
        Assert.Equal("mailto:team@example.com", mailto.Url);

        // The bound app's own declared nativeApplication.protocol is an allowed deep-link scheme (M-v).
        var deepLink = service.OpenLink(
            catalog, _workspaceCraftPath, thread.Id, "oratorio", "oratorio://open/task/t1", "dyntool_1", "test_user");
        Assert.Equal("oratorio://open/task/t1", deepLink.Url);

        Assert.Throws<AppServerException>(() =>
            service.OpenLink(catalog, _workspaceCraftPath, thread.Id, "oratorio", "javascript:alert(1)", null, "test_user"));
        Assert.Throws<AppServerException>(() =>
            service.OpenLink(catalog, _workspaceCraftPath, thread.Id, "oratorio", "file:///etc/passwd", null, "test_user"));
        // A custom scheme the bound app did NOT declare stays rejected.
        Assert.Throws<AppServerException>(() =>
            service.OpenLink(catalog, _workspaceCraftPath, thread.Id, "oratorio", "vscode://open?file=x", null, "test_user"));

        AssertAppBindingAuditContains("binding.uiOpenLink");
        AssertAppBindingAuditContains("binding.uiOpenLink.blocked");
        Assert.Empty((await harness.Service.GetThreadAsync(thread.Id, default)).Turns);
    }

    [Fact]
    public async Task UiUpdateModelContext_UpsertsModelVisibleBlockThenClearsOnEmpty()
    {
        WriteOratorioPlugin();
        var service = new AppBindingService();
        using var harness = CreateHarness(service);
        await harness.InitializeAsync();
        await ConnectAppAsync(harness);
        var thread = await harness.Service.CreateThreadAsync(CreateIdentity());
        await CreateAcceptAndAttachUiToolAsync(harness, thread.Id);

        var upsert = service.UpdateModelContext(
            _workspaceCraftPath, thread.Id, "oratorio", "dyntool_1", "Selected card", "The user selected card-7.", "test_user");
        Assert.False(upsert.Cleared);
        Assert.Equal("ui:dyntool_1", upsert.BlockId);

        var block = Assert.Single(service.ListThreadContextBlocks(_workspaceCraftPath, thread.Id, includeInactive: false).Blocks);
        Assert.Equal("ui:dyntool_1", block.BlockId);
        Assert.Equal("uiModelContext", block.Kind);
        Assert.Equal("model", block.Visibility);
        Assert.Equal("The user selected card-7.", block.Content);

        var section = service.BuildAppContextPromptSection(_workspaceCraftPath, thread.Id);
        Assert.NotNull(section);
        Assert.Contains("The user selected card-7.", section!, StringComparison.Ordinal);

        // Decoupled: no conversation turn/item.
        Assert.Empty((await harness.Service.GetThreadAsync(thread.Id, default)).Turns);

        // Last-write-wins clear (e.g. on teardown): empty content removes the block.
        var cleared = service.UpdateModelContext(
            _workspaceCraftPath, thread.Id, "oratorio", "dyntool_1", null, "", "test_user");
        Assert.True(cleared.Cleared);
        Assert.Empty(service.ListThreadContextBlocks(_workspaceCraftPath, thread.Id, includeInactive: true).Blocks);

        AssertAppBindingAuditContains("binding.uiModelContext.upsert");
        AssertAppBindingAuditContains("binding.uiModelContext.clear");
    }

    private async Task<string> CreateAcceptAndAttachUiToolAsync(
        AppServerTestHarness harness,
        string threadId,
        bool withApproval = false)
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
                        InputSchema = CreateCardSchema(),
                        // A tool that declares an approval descriptor is mutating; the M-iii UI path
                        // must reject it (read-only).
                        Approval = withApproval
                            ? new ChannelToolApprovalDescriptor
                            {
                                Kind = "remoteResource",
                                TargetArgument = "title",
                                Operation = "create"
                            }
                            : null,
                        Meta = new DynamicToolMeta
                        {
                            Ui = new UiToolMeta
                            {
                                ResourceUri = "ui://oratorio/board",
                                Visibility = ["model", "app"]
                            }
                        }
                    }
                }
            },
            expectedNotificationMethod: "thread/appBindings/changed");
        AppServerTestHarness.AssertIsSuccessResponse(attachResponse);
        Assert.Equal(1, attachResponse.RootElement.GetProperty("result").GetProperty("acceptedToolCount").GetInt32());
        return bindingId;
    }

    private void AssertAppBindingAuditContains(string eventName, string? detailContains = null)
    {
        var statePath = Path.Combine(_workspaceCraftPath, "app-bindings", "state.json");
        using var document = JsonDocument.Parse(ReadAppBindingState(statePath));
        Assert.Contains(
            document.RootElement.GetProperty("audit").EnumerateArray(),
            audit => AuditMatches(audit, eventName, detailContains));
    }

    private async Task WaitForAppBindingAuditContainsAsync(string eventName, string? detailContains = null)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var statePath = Path.Combine(_workspaceCraftPath, "app-bindings", "state.json");
            if (File.Exists(statePath))
            {
                try
                {
                    using var document = JsonDocument.Parse(ReadAppBindingState(statePath));
                    if (document.RootElement.GetProperty("audit").EnumerateArray()
                        .Any(audit => AuditMatches(audit, eventName, detailContains)))
                    {
                        return;
                    }
                }
                catch (IOException)
                {
                    // The social delivery observer writes audit asynchronously; retry while the file is locked.
                }
                catch (JsonException)
                {
                    // The writer may have replaced the state file between Exists and read; retry.
                }
            }

            await Task.Delay(10);
        }

        AssertAppBindingAuditContains(eventName, detailContains);
    }

    private static bool AuditMatches(JsonElement audit, string eventName, string? detailContains)
    {
        if (audit.GetProperty("event").GetString() != eventName)
            return false;
        if (string.IsNullOrWhiteSpace(detailContains))
            return true;

        return audit.TryGetProperty("detail", out var detail)
               && detail.GetString()?.Contains(detailContains, StringComparison.Ordinal) == true;
    }

    private static string ReadAppBindingState(string statePath)
    {
        using var stream = new FileStream(statePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
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
        bool withOriginMembers = false,
        string? containerRoot = null)
    {
        var originChannelJson = string.IsNullOrEmpty(originChannel)
            ? ""
            : $"      \"originChannel\": \"{originChannel}\",\n";
        var originMembersJson = withOriginMembers
            ? "      \"originMembers\": ["
              + "{ \"match\": \"alpha\", \"displayName\": \"Alpha\", \"icon\": \"./member-alpha.svg\" },"
              + "{ \"match\": \"beta\", \"displayName\": \"Beta\", \"icon\": \"./member-beta.svg\" }],\n"
            : "";
        var pluginRoot = Path.Combine(containerRoot ?? Path.Combine(_workspaceCraftPath, "plugins"), rootName);
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

    private void WriteAgentTeamsPlugin()
    {
        var pluginRoot = Path.Combine(_workspaceCraftPath, "plugins", "agent-teams");
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "assets"));
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            """
{
  "schemaVersion": 1,
  "id": "agent-teams",
  "version": "0.1.0",
  "displayName": "Agent Teams",
  "description": "Unlock the DotCraft Team card board with robot teammates.",
  "capabilities": ["metadata", "team"],
  "interface": {
    "displayName": "Agent Teams",
    "shortDescription": "Run missions with a small robot team",
    "developerName": "DotHarness",
    "category": "Productivity",
    "composerIcon": "./assets/agent-teams.svg",
    "logo": "./assets/agent-teams.svg"
  }
}
""");
        foreach (var role in new[] { "leader", "explorer", "builder", "reviewer", "operator", "agent-teams" })
        {
            File.WriteAllText(
                Path.Combine(pluginRoot, "assets", $"team-{role}.svg".Replace("team-agent-teams", "agent-teams")),
                """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" fill="#4F7CF6"/></svg>""");
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
