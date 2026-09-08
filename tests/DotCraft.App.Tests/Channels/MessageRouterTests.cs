using DotCraft.Channels;
using DotCraft.Security;
using Xunit;

namespace DotCraft.Tests.Channels;

public sealed class MessageRouterTests
{
    [Fact]
    public async Task DeliverRequiredAsync_RejectsUnavailableChannels()
    {
        var router = new MessageRouter(new ChannelRuntimeRegistry());
        await Assert.ThrowsAsync<InvalidOperationException>(() => router.DeliverRequiredAsync(
            "missing", "group:42", new ChannelDeliveryMessage { Kind = "text", Text = "result" }, CancellationToken.None));
    }

    [Fact]
    public async Task DeliverRequiredAsync_PropagatesChannelRejection()
    {
        var router = new MessageRouter(new ChannelRuntimeRegistry());
        router.RegisterChannel(new StubChannel("telegram", [], new ChannelDeliveryResult
        {
            Delivered = false, ErrorCode = "Forbidden", ErrorMessage = "Bot removed from group"
        }));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => router.DeliverRequiredAsync(
            "telegram", "group:42", new ChannelDeliveryMessage { Kind = "text", Text = "result" }, CancellationToken.None));
        Assert.Equal("Bot removed from group", error.Message);
    }

    [Fact]
    public async Task DeliverRequiredAsync_CompletesWhenChannelConfirmsDelivery()
    {
        var router = new MessageRouter(new ChannelRuntimeRegistry());
        router.RegisterChannel(new StubChannel("telegram", [], new ChannelDeliveryResult { Delivered = true }));
        await router.DeliverRequiredAsync(
            "telegram", "group:42", new ChannelDeliveryMessage { Kind = "text", Text = "result" }, CancellationToken.None);
    }

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

        var broadcastTask = router.BroadcastToAdminsAsync("scheduled result");
        await first.WaitUntilFirstDeliveryAsync();
        router.RegisterChannel(second);
        first.Release();
        await broadcastTask;

        Assert.Equal(1, first.DeliverCount);
        Assert.Equal(0, second.DeliverCount);

        await router.BroadcastToAdminsAsync("scheduled result 2");
        Assert.Equal(1, second.DeliverCount);
    }

    private sealed class StubChannel(
        string name,
        IReadOnlyList<string> adminTargets,
        ChannelDeliveryResult result) : IChannelService
    {
        public string Name => name;
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
