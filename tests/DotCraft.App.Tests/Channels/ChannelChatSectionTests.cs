using DotCraft.AppBinding;
using DotCraft.Channels;
using DotCraft.Context.WorldState;
using DotCraft.ExternalChannel.Messaging;
using DotCraft.Sessions;
using DotCraft.Workspaces;
using Xunit;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.Tests.Channels;

public sealed class ChannelChatSectionTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"channel-section-{Guid.NewGuid():N}");
    private readonly ChannelRuntimeRegistry _registry = new();
    private readonly FakeChannel _qq = new();
    private readonly SessionThread _inbound;
    private WorldStateSnapshot? _baseline;

    public ChannelChatSectionTests()
    {
        Directory.CreateDirectory(_workspace);
        _registry.Register(_qq);
        _inbound = new SessionThread { Id = "thread-1", OriginChannel = "qq", ChannelContext = "group:42", WorkspacePath = _workspace };
        _qq.Chat = new ChannelChatDescriptor(ChannelChatKinds.Group, "Release team", ChannelChatMemberships.Shared);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void UnavailableFacts_AreNotSentAgainWhenTheyReturnUnchanged()
    {
        var world = World(new ChatBindingResolver(_registry));

        Assert.True(Sent(world, _inbound));

        _qq.IsReady = false;
        Assert.False(Sent(world, _inbound));
        _qq.IsReady = true;
        Assert.False(Sent(world, _inbound));

        var chat = _qq.Chat;
        _qq.Chat = null;
        Assert.False(Sent(world, _inbound));
        _qq.Chat = chat;
        Assert.False(Sent(world, _inbound));
    }

    [Fact]
    public void ChangedLabel_IsSentAgain()
    {
        var world = World(new ChatBindingResolver(_registry));
        Assert.True(Sent(world, _inbound));

        _qq.Chat = new ChannelChatDescriptor(ChannelChatKinds.Group, "Launch team", ChannelChatMemberships.Shared);

        Assert.True(Sent(world, _inbound));
        Assert.False(Sent(world, _inbound));
    }

    [Fact]
    public void ThreadThatWasNeverBound_HearsNothing()
    {
        var world = World(new ChatBindingResolver(_registry));
        var desktop = new SessionThread { Id = "thread-2", OriginChannel = "desktop", ChannelContext = "workspace:/w", WorkspacePath = _workspace };

        Assert.False(Sent(world, desktop));
        Assert.False(Sent(world, desktop));
    }

    [Fact]
    public void RemovedBinding_IsRetiredOnce_AndAnOfflineBindingChangesNothing()
    {
        var (bindings, dataPath, bindingId, resolver) = AppBinding();
        var world = World(resolver);
        var thread = new SessionThread { Id = "desktop-thread", OriginChannel = "desktop", WorkspacePath = _workspace };
        Assert.True(Sent(world, thread));

        bindings.MarkUnavailable(dataPath, bindingId, "channelDisconnected");
        Assert.False(Sent(world, thread));

        bindings.RevokeBinding(dataPath, thread.Id, bindingId, "local");
        Assert.True(Sent(world, thread));
        Assert.False(Sent(world, thread));
    }

    [Fact]
    public async Task OfflineBinding_KeepsTheTool_AndFailsTheCall_WhileRevocationRemovesIt()
    {
        var (bindings, dataPath, bindingId, resolver) = AppBinding();
        var provider = new SendChannelMessageToolProvider(resolver);
        var thread = new SessionThread { Id = "desktop-thread", OriginChannel = "desktop", WorkspacePath = _workspace };

        bindings.MarkUnavailable(dataPath, bindingId, "channelDisconnected");
        var source = Assert.Single(provider.CreateToolSourcesForThread(thread));
        var planning = new DotCraft.Tools.ToolPlanningContext(thread.Id, null, _workspace, Path.Combine(_workspace, ".craft"), "agent", null, null, 1);
        var runtime = Assert.Single(await source.GetRegistrationsAsync(planning)).Binding.Runtime;
        var result = await runtime.InvokeAsync(Context(thread.Id), System.Text.Json.Nodes.JsonNode.Parse("""{ "text": "hi" }""")!.AsObject());

        Assert.Equal(SendChannelMessageErrorCodes.ChannelUnavailable, result.Error?.Code);
        Assert.Empty(_qq.Deliveries);

        bindings.RevokeBinding(dataPath, thread.Id, bindingId, "local");
        Assert.Empty(provider.CreateToolSourcesForThread(thread));
    }

    private (AppBindingService Bindings, string DataPath, string BindingId, ChatBindingResolver Resolver) AppBinding()
    {
        var paths = DotCraftPaths.CreateForExecutionHost(_workspace, Path.Combine(_workspace, ".craft"));
        var bindings = new AppBindingService();
        var dataPath = paths.Data.RootPath;
        var request = bindings.CreateChannelRequest(dataPath, "desktop-thread", "qq", "local");
        var binding = bindings.AcceptChannel(dataPath, "qq", new ChannelBindingAcceptCommand
        {
            Code = request.Code,
            Target = new ChannelTarget
            {
                ChannelName = "qq",
                ConversationKind = "group",
                ConversationId = "42",
                DeliveryTarget = "group:42",
                DisplayName = "Release team"
            }
        });
        return (bindings, dataPath, binding.BindingId, new ChatBindingResolver(_registry, paths, bindings));
    }

    private static WorldState World(ChatBindingResolver resolver)
    {
        var world = new WorldState();
        world.AddSection(new ChannelChatSection(resolver));
        return world;
    }

    private bool Sent(WorldState world, SessionThread thread)
    {
        var render = _baseline == null
            ? world.RenderFull(new WorldStateContext { Thread = thread })
            : world.Render(new WorldStateContext { Thread = thread }, _baseline, sectionIdsInHistory: null);
        _baseline = render.Snapshot;
        return render.Fragments.Count > 0;
    }

    private DotCraft.Tools.ToolInvocationContext Context(string threadId) => new(
        threadId,
        "turn-1",
        "call-1",
        DotCraft.Tools.ToolInvocationAudience.Model,
        new DotCraft.Tools.ToolName(null, SendChannelMessageToolSource.ToolName),
        new DotCraft.Tools.ToolDefinitionId(
            DotCraft.Tools.ToolSourceKind.CoreNative,
            "channel-message",
            new DotCraft.Tools.SourceToolId(SendChannelMessageToolSource.ToolName)),
        new DotCraft.Tools.RuntimeBindingId("binding"),
        1,
        DateTimeOffset.UtcNow,
        WorkspacePath: _workspace);

    private sealed class FakeChannel : IChannelRuntime
    {
        public string Name => "qq";

        public string DisplayName => "QQ";

        public bool IsReady { get; set; } = true;

        public ChannelChatDescriptor? Chat { get; set; }

        public List<string> Deliveries { get; } = [];

        public ChannelDeliveryCapabilitySnapshot? GetDeliveryCapabilities() => new()
        {
            Text = new ChannelTextCapabilitySnapshot { Format = ChannelTextFormats.Plain },
            Targets = new ChannelTargetCapabilitySnapshot { Group = true, Direct = ChannelDirectReach.KnownOnly },
            Media = new ChannelMediaCapabilitySnapshot { Image = new ChannelMediaConstraintSnapshot { SupportsHostPath = true } }
        };

        public ChannelChatDescriptor? GetThreadChat(string threadId) => Chat;

        public Task<ChannelDeliveryResult> DeliverAsync(
            string target,
            ChannelDeliveryMessage message,
            object? metadata = null,
            string? deliveryId = null,
            CancellationToken cancellationToken = default)
        {
            Deliveries.Add(target);
            return Task.FromResult(new ChannelDeliveryResult { Delivered = true });
        }
    }
}
