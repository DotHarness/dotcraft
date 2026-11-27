using DotCraft.Channels;
using DotCraft.Context.WorldState;
using DotCraft.Channels.Messaging;
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
