using DotCraft.Context;
using DotCraft.Memory;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Context;

public sealed class MemoryConsolidatorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "MemoryLegacy_" + Guid.NewGuid().ToString("N")[..8]);

    public MemoryConsolidatorTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { }
    }

    [Fact]
    public async Task ConsolidateAsync_ProviderTimeoutReturnsFailedProviderTimeout()
    {
        var consolidator = new MemoryConsolidator(
            new ThrowingChatClient(new OperationCanceledException("provider timeout")),
            new MemoryStore(_tempDir));

        var result = await consolidator.ConsolidateAsync(
            [new ChatMessage(ChatRole.User, "remember blue")]);

        Assert.Equal(MemoryConsolidationOutcome.Failed, result.Outcome);
        Assert.Equal("provider_timeout", result.Message);
    }

    [Fact]
    public async Task ConsolidateAsync_UserCancellationRethrows()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var consolidator = new MemoryConsolidator(
            new ThrowingChatClient(new OperationCanceledException(cts.Token)),
            new MemoryStore(_tempDir));

        await Assert.ThrowsAsync<OperationCanceledException>(() => consolidator.ConsolidateAsync(
            [new ChatMessage(ChatRole.User, "remember blue")],
            cts.Token));
    }

    private sealed class ThrowingChatClient(Exception exception) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw exception;

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw exception;

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
