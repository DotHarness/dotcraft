using System.Runtime.CompilerServices;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class AnthropicDeferredToolCatalogChatClientTests
{
    [Fact]
    public async Task RequestsContainStableSortedInventoryWithoutChangingHistory()
    {
        var registry = CreateRegistry();
        var capture = new CaptureChatClient();
        using var client = new AnthropicDeferredToolCatalogChatClient(capture, registry);
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "Look up records.")
        };

        await client.GetResponseAsync(history);
        var firstCatalog = AssertCatalog(capture.Calls[0]);

        registry.ActivateByName(["fixture__LookupRecords"]);
        await foreach (var _ in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.Assistant, "Compacted conversation summary.")]))
        {
        }
        var replacementCatalog = AssertCatalog(capture.Calls[1]);

        Assert.Equal(firstCatalog, replacementCatalog);
        Assert.Single(history);
        Assert.Equal("Look up records.", history[0].Text);
    }

    [Fact]
    public async Task InventoryParticipatesInPromptCachePrefix()
    {
        var registry = CreateRegistry();
        var capture = new CaptureChatClient();
        var promptCache = new PromptCachingChatClient(
            capture,
            new AppConfig.PromptCachingConfig(),
            "claude-opus-4-7",
            sessionKeyAccessor: () => "anthropic-catalog-cache-test");
        using var client = new AnthropicDeferredToolCatalogChatClient(promptCache, registry);
        var user = new ChatMessage(ChatRole.User, "Look up records.");

        await client.GetResponseAsync([user]);

        var catalog = capture.Calls[0][0];
        var samePrefix = promptCache.Prepare([catalog, user], null);
        var missingCatalog = promptCache.Prepare([user], null);
        Assert.True(samePrefix.PendingCachePoints.Single(static point => point.Trace.Latest).Trace.Remembered);
        Assert.False(missingCatalog.PendingCachePoints.Single(static point => point.Trace.Latest).Trace.Remembered);
    }

    private static DeferredToolActivationIndex CreateRegistry()
    {
        var lookupRecords = AIFunctionFactory.Create(
            (int limit) => $"records {limit}",
            name: "LookupRecords",
            description: "Look up records.");
        var readRecord = AIFunctionFactory.Create(
            (string recordId) => recordId,
            name: "ReadRecord",
            description: "Read a record.");
        return new DeferredToolActivationIndex(
            [
                new DeferredToolEntry(lookupRecords, "fixture", "fixture"),
                new DeferredToolEntry(readRecord, "fixture", "fixture")
            ],
            DeferredToolLoadingMode.Native);
    }

    private static string AssertCatalog(IReadOnlyList<ChatMessage> messages)
    {
        Assert.Equal(2, messages.Count);
        var catalog = messages[0];
        Assert.Equal(ChatRole.User, catalog.Role);
        Assert.Equal(
            """
            <available-deferred-tools>
            fixture__LookupRecords
            fixture__ReadRecord
            </available-deferred-tools>
            """.ReplaceLineEndings("\n"),
            catalog.Text);
        return catalog.Text;
    }

    private sealed class CaptureChatClient : IChatClient
    {
        public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(messages.ToList());
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(messages.ToList());
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
