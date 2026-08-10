using DotCraft.AppServer;
using DotCraft.Channels;
using DotCraft.Cron;
using DotCraft.Heartbeat;
using DotCraft.Security;
using Xunit;

namespace DotCraft.Tests.Channels;

public sealed class MessageRouterTests
{
    [Fact]
    public void UnregisterChannel_RemovesRuntimeRegistryEntry()
    {
        var registry = new ChannelRuntimeRegistry();
        var router = new MessageRouter(registry);
        var channel = new StubChannel(
            "qq",
            ["admin-user"],
            new ChannelDeliveryResult { Delivered = true });

        router.RegisterChannel(channel);
        Assert.True(registry.TryGet("qq", out _));

        var removed = router.UnregisterChannel("qq");

        Assert.True(removed);
        Assert.False(registry.TryGet("qq", out _));
    }

    [Fact]
    public async Task BroadcastToAdminsAsync_UsesSnapshot_WhenChannelsChangeDuringBroadcast()
    {
        var router = new MessageRouter(new ChannelRuntimeRegistry());
        var first = new BlockingAdminChannel("qq");
        var second = new CountingAdminChannel("wecom");
        router.RegisterChannel(first);

        var broadcastTask = router.BroadcastToAdminsAsync("heartbeat");
        await first.WaitUntilFirstDeliveryAsync();
        router.RegisterChannel(second);
        first.Release();
        await broadcastTask;

        Assert.Equal(1, first.DeliverCount);
        Assert.Equal(0, second.DeliverCount);

        await router.BroadcastToAdminsAsync("heartbeat-2");
        Assert.Equal(1, second.DeliverCount);
    }

    private sealed class StubChannel(
        string name,
        IReadOnlyList<string> adminTargets,
        ChannelDeliveryResult result) : IChannelService
    {
        public string Name => name;
        public HeartbeatService? HeartbeatService { get; set; }
        public CronService? CronService { get; set; }
        public IApprovalService? ApprovalService => null;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public IReadOnlyList<string> GetAdminTargets() => adminTargets;

        public Task<ChannelDeliveryResult> DeliverAsync(
            string target,
            ChannelDeliveryMessage message,
            object? metadata = null,
            CancellationToken cancellationToken = default) => Task.FromResult(result);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingAdminChannel(string name) : IChannelService
    {
        private readonly TaskCompletionSource<bool> _deliveryStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _deliverCount;

        public string Name => name;
        public int DeliverCount => _deliverCount;
        public HeartbeatService? HeartbeatService { get; set; }
        public CronService? CronService { get; set; }
        public IApprovalService? ApprovalService => null;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public IReadOnlyList<string> GetAdminTargets() => ["admin"];

        public async Task<ChannelDeliveryResult> DeliverAsync(
            string target,
            ChannelDeliveryMessage message,
            object? metadata = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _deliverCount);
            _deliveryStarted.TrySetResult(true);
            await _release.Task;
            return new ChannelDeliveryResult { Delivered = true };
        }

        public async Task WaitUntilFirstDeliveryAsync() =>
            await _deliveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        public void Release() => _release.TrySetResult(true);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CountingAdminChannel(string name) : IChannelService
    {
        private int _deliverCount;

        public string Name => name;
        public int DeliverCount => _deliverCount;
        public HeartbeatService? HeartbeatService { get; set; }
        public CronService? CronService { get; set; }
        public IApprovalService? ApprovalService => null;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public IReadOnlyList<string> GetAdminTargets() => ["admin"];

        public Task<ChannelDeliveryResult> DeliverAsync(
            string target,
            ChannelDeliveryMessage message,
            object? metadata = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _deliverCount);
            return Task.FromResult(new ChannelDeliveryResult { Delivered = true });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
