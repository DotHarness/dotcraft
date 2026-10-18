using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.AppBinding;
using DotCraft.Automations;
using DotCraft.Channels;
using DotCraft.ExternalChannel.Messaging;
using DotCraft.Plugins;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Tools;
using DotCraft.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;

namespace DotCraft.Tests.Channels;

public sealed class SendChannelMessageTests : IDisposable
{
    private const string ThreadId = "thread-1";
    private const string TurnId = "turn-1";
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"send-channel-{Guid.NewGuid():N}");
    private readonly ChannelRuntimeRegistry _registry = new();
    private readonly FakeChannel _telegram = new("telegram");

    public SendChannelMessageTests()
    {
        Directory.CreateDirectory(_workspace);
        _registry.Register(_telegram);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Tool_IsPublishedOnlyOnBoundRootThreads_WithOneSchema()
    {
        var provider = new SendChannelMessageToolProvider(new ChatBindingResolver(_registry));
        var first = InboundThread("thread-a", "chat-a");
        var second = InboundThread("thread-b", "chat-b");
        var child = InboundThread("thread-c", "chat-a");
        child.Source = ThreadSource.ForSubAgent(new SubAgentThreadSource { ParentThreadId = "thread-a" });
        var unbound = new SessionThread { Id = "thread-d", OriginChannel = "desktop", WorkspacePath = _workspace };

        Assert.Empty(provider.CreateToolSourcesForThread(child));
        Assert.Empty(provider.CreateToolSourcesForThread(unbound));

        var a = await RegistrationAsync(provider, first);
        var b = await RegistrationAsync(provider, second);
        Assert.Equal(ToolExposure.DirectModelOnly, a.Exposure);
        Assert.Equal(ToolPolicyScope.RuntimeManaged, a.Definition.PolicyScope);
        Assert.Equal(a.Definition.InputSchema.GetRawText(), b.Definition.InputSchema.GetRawText());
    }

    [Fact]
    public async Task AppBindingThread_ResolvesTheBindingAtCallTime()
    {
        var paths = DotCraftPaths.CreateForExecutionHost(_workspace, Path.Combine(_workspace, ".craft"));
        var bindings = new AppBindingService();
        var dataPath = paths.Data.RootPath;
        var request = bindings.CreateChannelRequest(dataPath, "desktop-thread", "telegram", "local");
        var binding = bindings.AcceptChannel(dataPath, "telegram", new ChannelBindingAcceptCommand
        {
            Code = request.Code,
            Target = new ChannelTarget
            {
                ChannelName = "telegram",
                ConversationKind = "group",
                ConversationId = "-100",
                DeliveryTarget = "-100",
                DisplayName = "Release team"
            }
        });
        var resolver = new ChatBindingResolver(_registry, paths, bindings);
        var provider = new SendChannelMessageToolProvider(resolver);
        var thread = new SessionThread { Id = "desktop-thread", OriginChannel = "desktop", WorkspacePath = _workspace };
        var runtime = (await RegistrationAsync(provider, thread)).Binding.Runtime;

        var sent = await runtime.InvokeAsync(Context("call-1", "desktop-thread"), Args("""{ "text": "hi" }"""));
        bindings.RevokeBinding(dataPath, "desktop-thread", binding.BindingId, "local");
        var revoked = await runtime.InvokeAsync(Context("call-2", "desktop-thread"), Args("""{ "text": "again" }"""));

        Assert.True(sent.Success);
        Assert.Equal("-100", Assert.Single(_telegram.Deliveries).Target);
        Assert.False(revoked.Success);
        Assert.Equal(SendChannelMessageErrorCodes.ChannelUnavailable, revoked.Error?.Code);
    }

    [Fact]
    public async Task ChannelOriginRunThread_IsBoundToTheOriginChat()
    {
        var automations = Automations();
        var definition = await CreateAutomationAsync(automations, "independent", "workspaceScope");
        var provider = new SendChannelMessageToolProvider(new ChatBindingResolver(_registry, automations: automations));
        var runThread = new SessionThread
        {
            Id = "run-thread", OriginChannel = AutomationService.RunChannel, ChannelContext = definition.Id, WorkspacePath = _workspace
        };

        var runtime = (await RegistrationAsync(provider, runThread)).Binding.Runtime;
        var result = await runtime.InvokeAsync(Context("call-1", runThread.Id), Args("""{ "text": "nothing new" }"""));

        Assert.True(result.Success);
        Assert.Equal("chat-9", Assert.Single(_telegram.Deliveries).Target);
        Assert.Equal("Ops", result.StructuredContent!.Value.GetProperty("label").GetString());
    }

    [Fact]
    public async Task ThreadModeRun_SpeaksToTheTargetThreadsOwnChat()
    {
        var automations = Automations();
        await CreateAutomationAsync(automations, "thread", "fullAuto");
        var resolver = new ChatBindingResolver(_registry, automations: automations);
        var provider = new SendChannelMessageToolProvider(resolver);

        var runtime = (await RegistrationAsync(provider, InboundThread(ThreadId, "chat-1"))).Binding.Runtime;
        await runtime.InvokeAsync(Context("call-1"), Args("""{ "text": "update" }"""));

        Assert.Equal("chat-1", Assert.Single(_telegram.Deliveries).Target);
    }

    [Theory]
    [InlineData("workspaceScope", false)]
    [InlineData("fullAuto", true)]
    public async Task AutomationRun_SendsToOtherChatsOnlyUnderFullAuto(string approvalPolicy, bool sent)
    {
        var automations = Automations();
        var definition = await CreateAutomationAsync(automations, "independent", approvalPolicy);
        var runtime = new SendChannelMessageRuntime(
            new ChatBindingResolver(_registry, automations: automations),
            ThreadId, AutomationService.RunChannel, definition.Id, _workspace, null);
        using var scope = EnterTurn(new AutoApproveApprovalService());

        var result = await runtime.InvokeAsync(Context("call-1"), Args("""{ "text": "hi", "to": "telegram:chat-2" }"""));

        Assert.Equal(sent, result.Success);
        Assert.Equal(sent ? null : SendChannelMessageErrorCodes.Declined, result.Error?.Code);
        Assert.Equal(sent ? new[] { "chat-2" } : [], _telegram.Deliveries.Select(delivery => delivery.Target));
    }

    [Theory]
    [InlineData("""{ }""", "invalidArguments")]
    [InlineData("""{ "text": "" }""", "invalidArguments")]
    [InlineData("""{ "attachments": [{ "path": "missing.png" }] }""", "invalidArguments")]
    [InlineData("""{ "text": "hi", "to": "no-separator" }""", "invalidArguments")]
    [InlineData("""{ "text": "hi", "to": "slack:general" }""", "channelUnavailable")]
    public async Task InvalidCalls_FailWithoutDelivering(string arguments, string code)
    {
        var result = await Runtime().InvokeAsync(Context("call-1"), Args(arguments));

        Assert.False(result.Success);
        Assert.Equal(code, result.Error?.Code);
        Assert.Empty(_telegram.Deliveries);
    }

    [Fact]
    public async Task WorkspaceScopedTurn_RefusesAnAttachmentOutsideTheWorkspace()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"send-channel-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            var file = Path.Combine(outside, "report.txt");
            File.WriteAllText(file, "numbers");
            _telegram.Capabilities.Media = new ChannelMediaCapabilitySnapshot { File = HostPath() };
            var approvals = new AutoApproveApprovalService();
            using var host = EnterTurn(approvals);
            using var turn = PluginFunctionExecutionScope.Set(new PluginFunctionExecutionContext
            {
                ThreadId = ThreadId,
                TurnId = TurnId,
                OriginChannel = "telegram",
                WorkspacePath = _workspace,
                RequireApprovalOutsideWorkspace = false,
                ApprovalService = approvals,
                Turn = new SessionTurn { Id = TurnId, ThreadId = ThreadId },
                NextItemSequence = () => 1,
                EmitItemStarted = _ => { },
                EmitItemCompleted = _ => { }
            });

            var result = await Runtime().InvokeAsync(Context("call-1"),
                Args($$"""{ "attachments": [{ "path": {{JsonSerializer.Serialize(file)}} }] }"""));

            Assert.Equal(SendChannelMessageErrorCodes.InvalidArguments, result.Error?.Code);
            Assert.Empty(_telegram.Deliveries);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task MissingAttachmentOnARemoteRoute_PointsToTransfer()
    {
        _telegram.Capabilities.Media = new ChannelMediaCapabilitySnapshot { File = HostPath() };
        var runtime = new SendChannelMessageRuntime(
            new ChatBindingResolver(_registry), ThreadId, "telegram", "chat-1", _workspace, null,
            DispatchProxy.Create<IRemoteToolHostClient, RoutedRemoteToolHost>());

        var result = await runtime.InvokeAsync(Context("call-1"), Args("""{ "attachments": [{ "path": "build/output.log" }] }"""));

        Assert.Equal(SendChannelMessageErrorCodes.InvalidArguments, result.Error?.Code);
        Assert.Contains("Transfer", result.Error?.Message, StringComparison.Ordinal);
        Assert.Empty(_telegram.Deliveries);
    }

    [Fact]
    public async Task UnsupportedAttachment_FailsTheWholeCallBeforeSendingText()
    {
        WriteFile("clip.mp4", 4);
        _telegram.Capabilities.Media = new ChannelMediaCapabilitySnapshot { Image = HostPath() };

        var result = await Runtime().InvokeAsync(Context("call-1"),
            Args("""{ "text": "watch", "attachments": [{ "path": "clip.mp4", "kind": "video" }] }"""));

        Assert.Equal(SendChannelMessageErrorCodes.UnsupportedKind, result.Error?.Code);
        Assert.Empty(_telegram.Deliveries);
    }

    [Fact]
    public async Task OversizedAttachment_IsTooLarge()
    {
        WriteFile("big.png", 10);
        _telegram.Capabilities.Media = new ChannelMediaCapabilitySnapshot
        {
            Image = new ChannelMediaConstraintSnapshot { SupportsHostPath = true, MaxBytes = 5 }
        };

        var result = await Runtime().InvokeAsync(Context("call-1"),
            Args("""{ "attachments": [{ "path": "big.png" }] }"""));

        Assert.Equal(SendChannelMessageErrorCodes.TooLarge, result.Error?.Code);
        Assert.Empty(_telegram.Deliveries);
    }

    [Fact]
    public async Task TextIsSentFirst_ThenAttachmentsInOrder()
    {
        WriteFile("a.png", 3);
        WriteFile("b.pdf", 3);
        _telegram.Capabilities.Media = new ChannelMediaCapabilitySnapshot
        {
            Image = HostPath(caption: true),
            File = HostPath(caption: true)
        };

        var result = await Runtime().InvokeAsync(Context("call-1"),
            Args("""{ "text": "report", "attachments": [{ "path": "a.png" }, { "path": "b.pdf", "name": "Report.pdf" }] }"""));

        Assert.True(result.Success);
        Assert.Collection(_telegram.Deliveries,
            text => Assert.Equal(("text", "report"), (text.Message.Kind, text.Message.Text)),
            image => Assert.Equal(("image", (string?)null), (image.Message.Kind, image.Message.Caption)),
            file => Assert.Equal(("file", "Report.pdf"), (file.Message.Kind, file.Message.FileName)));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public async Task SingleAttachment_CarriesTheTextAsCaptionWhenSupported(bool supportsCaption, int deliveries)
    {
        WriteFile("photo.jpg", 3);
        _telegram.Capabilities.Media = new ChannelMediaCapabilitySnapshot { Image = HostPath(supportsCaption) };

        await Runtime().InvokeAsync(Context("call-1"),
            Args("""{ "text": "look", "attachments": [{ "path": "photo.jpg" }] }"""));

        Assert.Equal(deliveries, _telegram.Deliveries.Count);
        var image = _telegram.Deliveries[^1].Message;
        Assert.Equal("image", image.Kind);
        Assert.Equal(supportsCaption ? "look" : null, image.Caption);
    }

    [Fact]
    public async Task UnsupportedAudio_IsSentAsFileAndReported()
    {
        WriteFile("voice.ogg", 3);
        _telegram.Capabilities.Media = new ChannelMediaCapabilitySnapshot { File = HostPath() };

        var result = await Runtime().InvokeAsync(Context("call-1"),
            Args("""{ "attachments": [{ "path": "voice.ogg", "kind": "audio" }] }"""));

        Assert.True(result.Success);
        Assert.Equal("file", Assert.Single(_telegram.Deliveries).Message.Kind);
        var part = result.StructuredContent!.Value.GetProperty("parts")[0];
        Assert.Equal("audio", part.GetProperty("downgradedFrom").GetString());
    }

    [Theory]
    [InlineData("voice.mp3", "audio", "audio")]
    [InlineData("anim.gif", null, null)]
    public async Task AttachmentTheKindCannotTake_IsSentAsFile(string name, string? kind, string? downgradedFrom)
    {
        WriteFile(name, 3);
        _telegram.Capabilities.Media = new ChannelMediaCapabilitySnapshot
        {
            Image = new ChannelMediaConstraintSnapshot { SupportsHostPath = true, AllowedExtensions = [".png"] },
            Audio = new ChannelMediaConstraintSnapshot { SupportsHostPath = true, AllowedExtensions = [".amr"] },
            File = HostPath()
        };
        var attachment = new JsonObject { ["path"] = name };
        if (kind != null)
            attachment["kind"] = kind;

        var result = await Runtime().InvokeAsync(Context("call-1"),
            new JsonObject { ["attachments"] = new JsonArray(attachment) });

        Assert.True(result.Success);
        Assert.Equal("file", Assert.Single(_telegram.Deliveries).Message.Kind);
        var part = result.StructuredContent!.Value.GetProperty("parts")[0];
        Assert.Equal(downgradedFrom, part.TryGetProperty("downgradedFrom", out var value) ? value.GetString() : null);
    }

    [Fact]
    public async Task PlatformFailure_ReportsThePartsDeliveredBeforeIt()
    {
        WriteFile("a.png", 3);
        _telegram.Capabilities.Media = new ChannelMediaCapabilitySnapshot { Image = HostPath() };
        _telegram.Respond = message => message.Kind == "image"
            ? new ChannelDeliveryResult { Delivered = false, ErrorCode = "AdapterDeliveryFailed", ErrorMessage = "rejected" }
            : new ChannelDeliveryResult { Delivered = true, RemoteMessageId = "m1" };

        var result = await Runtime().InvokeAsync(Context("call-1"),
            Args("""{ "text": "hi", "attachments": [{ "path": "a.png" }] }"""));

        Assert.Equal(SendChannelMessageErrorCodes.DeliveryFailed, result.Error?.Code);
        var parts = result.StructuredContent!.Value.GetProperty("parts");
        Assert.Equal("m1", Assert.Single(parts.EnumerateArray()).GetProperty("messageId").GetString());
    }

    [Fact]
    public async Task DeliveryIds_DependOnlyOnTurnCallAndPartIndex()
    {
        WriteFile("a.png", 3);
        _telegram.Capabilities.Media = new ChannelMediaCapabilitySnapshot { Image = HostPath() };
        var arguments = """{ "text": "hi", "attachments": [{ "path": "a.png" }] }""";

        await Runtime().InvokeAsync(Context("call-1"), Args(arguments));
        await Runtime().InvokeAsync(Context("call-1"), Args(arguments));
        await Runtime().InvokeAsync(Context("call-2"), Args(arguments));

        var ids = _telegram.Deliveries.Select(delivery => delivery.DeliveryId).ToArray();
        Assert.Equal(ids[..2], ids[2..4]);
        Assert.NotEqual(ids[0], ids[1]);
        Assert.Empty(ids[..2].Intersect(ids[4..]));
        Assert.Equal(SendChannelMessageRuntime.DeliveryId(TurnId, "call-1", 0), ids[0]);
    }

    [Fact]
    public async Task BoundChat_NeedsNoApproval_EvenWhenNamedExplicitly()
    {
        var approvals = new RecordingApprovalService(approve: false);
        using var scope = EnterTurn(approvals);

        var implicitTarget = await Runtime().InvokeAsync(Context("call-1"), Args("""{ "text": "hi" }"""));
        var explicitTarget = await Runtime().InvokeAsync(Context("call-2"), Args("""{ "text": "hi", "to": "telegram:chat-1" }"""));

        Assert.True(implicitTarget.Success);
        Assert.True(explicitTarget.Success);
        Assert.Empty(approvals.Requests);
    }

    [Fact]
    public async Task OtherChat_RequestsChannelMessageApproval_AndDeclineSendsNothing()
    {
        var approvals = new RecordingApprovalService(approve: false);
        using var scope = EnterTurn(approvals);

        var result = await Runtime().InvokeAsync(Context("call-1"), Args("""{ "text": "hi", "to": "telegram:chat-2" }"""));

        Assert.Equal(SendChannelMessageErrorCodes.Declined, result.Error?.Code);
        var request = Assert.Single(approvals.Requests);
        Assert.Equal(("channelMessage", "send", "telegram:chat-2"), (request.Kind, request.Operation, request.Target));
        Assert.Empty(_telegram.Deliveries);
    }

    [Fact]
    public async Task AcceptForSession_CoversLaterSendsToTheSameAddressOnly()
    {
        var turn = new SessionTurn { Id = TurnId, ThreadId = ThreadId, Status = TurnStatus.Running, StartedAt = DateTimeOffset.UtcNow };
        var sequence = 1;
        var approvals = new SessionApprovalService(
            new SessionEventChannel(ThreadId, TurnId), turn, () => sequence++, CancellationToken.None, () => { });
        using var scope = EnterTurn(approvals);

        var first = Runtime().InvokeAsync(Context("call-1"), Args("""{ "text": "a", "to": "telegram:chat-2" }""")).AsTask();
        approvals.TryResolve(await PendingRequestIdAsync(approvals, turn), SessionApprovalDecision.AcceptForSession);
        Assert.True((await first).Success);

        var second = await Runtime().InvokeAsync(Context("call-2"), Args("""{ "text": "b", "to": "telegram:chat-2" }"""));
        var third = Runtime().InvokeAsync(Context("call-3"), Args("""{ "text": "c", "to": "telegram:chat-3" }""")).AsTask();
        await PendingRequestIdAsync(approvals, turn);

        Assert.True(second.Success);
        Assert.False(third.IsCompleted);
        Assert.Equal(["chat-2", "chat-2"], _telegram.Deliveries.Select(delivery => delivery.Target));
        approvals.Close();
        await third;
    }

    [Fact]
    public async Task OtherChat_OnAChannelThatReachesOnlyTheBoundChat_IsUnsupportedTarget()
    {
        var approvals = new RecordingApprovalService(approve: true);
        using var scope = EnterTurn(approvals);
        _telegram.Capabilities.Targets = new ChannelTargetCapabilitySnapshot { Group = false, Direct = ChannelDirectReach.None };

        var result = await Runtime().InvokeAsync(Context("call-1"), Args("""{ "text": "hi", "to": "telegram:chat-2" }"""));

        Assert.Equal(SendChannelMessageErrorCodes.UnsupportedTarget, result.Error?.Code);
        Assert.Empty(approvals.Requests);
        Assert.Empty(_telegram.Deliveries);
    }

    [Fact]
    public async Task DisconnectedChannel_IsChannelUnavailable()
    {
        _telegram.IsReady = false;

        var result = await Runtime().InvokeAsync(Context("call-1"), Args("""{ "text": "hi" }"""));

        Assert.Equal(SendChannelMessageErrorCodes.ChannelUnavailable, result.Error?.Code);
    }

    private AutomationService Automations() => new(
        new AutomationsConfig(),
        new DotCraftPaths(_workspace, Path.Combine(_workspace, ".craft"), null),
        NullLogger<AutomationService>.Instance);

    private static Task<AutomationDefinition> CreateAutomationAsync(AutomationService automations, string executionMode, string approvalPolicy) =>
        automations.CreateAsync(
            new AutomationInput
            {
                Name = "Daily report", Prompt = "Report changes", Status = "paused", ExecutionMode = executionMode,
                TargetThreadId = executionMode == "thread" ? ThreadId : null, WorkspaceMode = "project", ApprovalPolicy = approvalPolicy,
                Schedule = new AutomationSchedule { Kind = "every", EveryMs = 60000 }
            },
            new AutomationOrigin
            {
                Channel = "telegram", UserId = "u", DeliveryTarget = "chat-9", ChatKind = ChannelChatKinds.Group, ChatLabel = "Ops"
            });

    private SendChannelMessageRuntime Runtime() =>
        new(new ChatBindingResolver(_registry), ThreadId, "telegram", "chat-1", _workspace, null);

    private SessionThread InboundThread(string id, string channelContext) =>
        new() { Id = id, OriginChannel = "telegram", ChannelContext = channelContext, WorkspacePath = _workspace };

    private async Task<ToolRegistration> RegistrationAsync(SendChannelMessageToolProvider provider, SessionThread thread)
    {
        var source = Assert.Single(provider.CreateToolSourcesForThread(thread));
        var planning = new ToolPlanningContext(thread.Id, null, _workspace, Path.Combine(_workspace, ".craft"), "agent", null, null, 1);
        return Assert.Single(await source.GetRegistrationsAsync(planning));
    }

    private ToolInvocationContext Context(string callId, string threadId = ThreadId) => new(
        threadId,
        TurnId,
        callId,
        ToolInvocationAudience.Model,
        new ToolName(null, SendChannelMessageToolSource.ToolName),
        new ToolDefinitionId(ToolSourceKind.CoreNative, "channel-message", new SourceToolId(SendChannelMessageToolSource.ToolName)),
        new RuntimeBindingId("binding"),
        1,
        DateTimeOffset.UtcNow,
        WorkspacePath: _workspace);

    private IDisposable EnterTurn(IApprovalService approvals) =>
        ToolHostExecutionScope.Set(new ToolHostExecutionContext(
            ThreadId, TurnId, _workspace, approvals, DispatchProxy.Create<ISessionService, UnusedSessionService>()));

    private void WriteFile(string name, int bytes) =>
        File.WriteAllBytes(Path.Combine(_workspace, name), new byte[bytes]);

    private static JsonObject Args(string json) => JsonNode.Parse(json)!.AsObject();

    private static ChannelMediaConstraintSnapshot HostPath(bool caption = false) =>
        new() { SupportsHostPath = true, SupportsCaption = caption };

    private static async Task<string> PendingRequestIdAsync(SessionApprovalService approvals, SessionTurn turn)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (approvals.HasPendingApproval)
            {
                var payload = turn.Items.Where(item => item.Type == ItemType.ApprovalRequest).Select(item => item.Payload).Last();
                return JsonSerializer.SerializeToElement(payload, payload!.GetType()).GetProperty("RequestId").GetString()!;
            }
            await Task.Delay(10);
        }

        throw new TimeoutException("No approval became pending.");
    }

    private sealed class FakeChannel(string name) : IChannelRuntime
    {
        public string Name => name;

        public string DisplayName => "Telegram";

        public bool IsReady { get; set; } = true;

        public ChannelDeliveryCapabilitySnapshot Capabilities { get; } = new()
        {
            Text = new ChannelTextCapabilitySnapshot { Format = ChannelTextFormats.Markdown },
            Targets = new ChannelTargetCapabilitySnapshot { Group = true, Direct = ChannelDirectReach.Any }
        };

        public Func<ChannelDeliveryMessage, ChannelDeliveryResult>? Respond { get; set; }

        public List<(string Target, ChannelDeliveryMessage Message, string? DeliveryId)> Deliveries { get; } = [];

        public ChannelDeliveryCapabilitySnapshot? GetDeliveryCapabilities() => Capabilities;

        public Task<ChannelDeliveryResult> DeliverAsync(
            string target,
            ChannelDeliveryMessage message,
            object? metadata = null,
            string? deliveryId = null,
            CancellationToken cancellationToken = default)
        {
            Deliveries.Add((target, message, deliveryId));
            return Task.FromResult(Respond?.Invoke(message)
                ?? new ChannelDeliveryResult { Delivered = true, RemoteMessageId = $"m{Deliveries.Count}" });
        }
    }

    private sealed class RecordingApprovalService(bool approve) : IApprovalService
    {
        public List<ResourceApprovalRequest> Requests { get; } = [];

        public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null) =>
            Task.FromResult(approve);

        public Task<bool> RequestShellApprovalAsync(DotCraft.Security.ShellCommands.ShellApprovalRequest request, ApprovalContext? context = null) =>
            Task.FromResult(approve);

        public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null) =>
            RequestResourceApprovalAsync(new ResourceApprovalRequest(kind, operation, target), context);

        public Task<bool> RequestResourceApprovalAsync(ResourceApprovalRequest request, ApprovalContext? context = null)
        {
            Requests.Add(request);
            return Task.FromResult(approve);
        }
    }

    public class RoutedRemoteToolHost : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IRemoteToolHostClient.TryGetRoute))
                throw new NotSupportedException();
            args![1] = new RemoteToolRoute("host", "workspace", "lease", "instance", "session");
            return true;
        }
    }

    public class UnusedSessionService : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException();
    }
}
