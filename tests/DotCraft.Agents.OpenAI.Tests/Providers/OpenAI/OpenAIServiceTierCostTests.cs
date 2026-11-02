using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Xunit;

#pragma warning disable OPENAI001, MAAI001, MEAI001

namespace DotCraft.Tests.Agents;

public sealed class OpenAIServiceTierCostTests
{
    [Theory]
    [InlineData(false, false, "gpt-5.5", "priority", "priority", 2.5)]
    [InlineData(false, false, "gpt-5.5", "priority", "fast", 2.5)]
    [InlineData(false, false, "gpt-5.4", "priority", "default", 1)]
    [InlineData(false, true, "gpt-5.4", "priority", "default", 2)]
    [InlineData(true, true, "gpt-5.5", "priority", "default", 2.5)]
    [InlineData(true, true, "gpt-5.5", "flex", "default", 0.5)]
    [InlineData(true, true, "gpt-5.4", "priority", "fast", 1)]
    [InlineData(false, false, "gpt-5.5", "priority", null, 2.5)]
    [InlineData(true, true, "gpt-5.4", "priority", null, 2)]
    public async Task Terminal_usage_uses_the_actual_request_and_provider_tier_rules(
        bool lite, bool codex, string model, string requestTier, string? responseTier, double expectedMultiplier)
    {
        await AssertMappedCostAsync(lite, codex, model, requestTier, responseTier,
            incomplete: false, (decimal)expectedMultiplier);
    }

    [Fact]
    public async Task Incomplete_response_still_prices_usage_at_the_returned_tier()
    {
        await AssertMappedCostAsync(lite: false, codex: false, "gpt-5.5", "priority", "flex",
            incomplete: true, expectedMultiplier: 0.5m);
    }

    private static async Task AssertMappedCostAsync(
        bool lite, bool codex, string model, string requestTier, string? responseTier,
        bool incomplete, decimal expectedMultiplier)
    {
        var runtime = new EffectiveModelRuntime(
            "test", model, ModelProviderProtocols.OpenAIResponses, "test", "test-key",
            codex ? ModelProviderDefaults.ChatGptBackendEndpoint : ModelProviderDefaults.DefaultOpenAIEndpoint,
            30, null, ModelProviderCapabilities.ForProtocol(ModelProviderProtocols.OpenAIResponses),
            AuthMethod: codex ? ModelProviderAuthMethods.ChatGptOAuth : ModelProviderAuthMethods.ApiKey);
        using var optionsScope = ProviderPipelineOptionsScope.Push(
            new ProviderPipelineOptions(runtime, null, null, false, "Standard", false, null));
        using var outerTier = ProviderRequestServiceTierScope.Push("outer-tier");
        var transport = new UsageTransport(TerminalEvent(model, responseTier, incomplete));
        var sdk = new ResponsesClient("sk-test");
        using IChatClient client = lite
            ? new OpenAIResponsesLiteChatClient(sdk, model, new EmptyChatClient(), transport, "install-test")
            : new OpenAIResponsesToolSearchChatClient(sdk, model, new EmptyChatClient(), transport);
        var options = new ChatOptions
        {
            RawRepresentationFactory = _ =>
            {
                var native = new CreateResponseOptions();
                ResponsesToolSearchMapper.PatchResponseServiceTier(native, requestTier);
                return native;
            }
        };

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "hello")], options))
            updates.Add(update);

        Assert.Equal(requestTier, transport.WireTier);
        Assert.Equal(requestTier, transport.ScopedTier);
        Assert.Equal("outer-tier", ProviderRequestServiceTierScope.Current);
        var usageUpdate = Assert.Single(updates, update => update.Contents.OfType<UsageContent>().Any());
        var usage = Assert.Single(usageUpdate.Contents.OfType<UsageContent>()).Details;
        Assert.Equal(1_000_000, usage.InputTokenCount);
        Assert.Equal(100_000, usage.OutputTokenCount);
        Assert.Equal(400_000, usage.CachedInputTokenCount);
        var cacheWrite = usage.AdditionalCounts!["CacheWriteInputTokenCount"];
        Assert.Equal(100_000, cacheWrite);
        var multiplier = Assert.IsType<decimal>(usageUpdate.AdditionalProperties!["dotcraft.costMultiplier"]);
        Assert.Equal(expectedMultiplier, multiplier);
        var normalized = new ProviderHttpUsage(
            usage.InputTokenCount!.Value, usage.OutputTokenCount!.Value,
            usage.CachedInputTokenCount!.Value, cacheWrite);

        var cost = ModelCostCalculator.Resolve(new ModelPricing(2m, 10m, 0.2m, 2.5m), normalized, multiplier);

        Assert.Equal(2.33m * expectedMultiplier, cost);
    }

    private static StreamingResponseUpdate TerminalEvent(string model, string? tier, bool incomplete)
    {
        var response = new Dictionary<string, object?>
        {
            ["id"] = "resp-cost", ["object"] = "response", ["created_at"] = 0,
            ["status"] = incomplete ? "incomplete" : "completed", ["model"] = model,
            ["output"] = Array.Empty<object>(),
            ["usage"] = new
            {
                input_tokens = 1_000_000, output_tokens = 100_000, total_tokens = 1_100_000,
                input_tokens_details = new { cached_tokens = 400_000, cache_write_tokens = 100_000 },
                output_tokens_details = new { reasoning_tokens = 20_000 }
            }
        };
        if (tier is not null)
            response["service_tier"] = tier;
        if (incomplete)
            response["incomplete_details"] = new { reason = "max_output_tokens" };
        var json = JsonSerializer.Serialize(new
        {
            type = incomplete ? "response.incomplete" : "response.completed",
            sequence_number = 1, response
        });
        return ModelReaderWriter.Read<StreamingResponseUpdate>(BinaryData.FromString(json), ModelReaderWriterOptions.Json)!;
    }

    private sealed class UsageTransport(StreamingResponseUpdate terminal) : IResponsesToolSearchTransport, IResponsesLiteTransport
    {
        public string? WireTier { get; private set; }
        public string? ScopedTier { get; private set; }

        public IAsyncEnumerable<StreamingResponseUpdate> CreateResponseStreamingAsync(
            CreateResponseOptions options, CancellationToken cancellationToken) =>
            ReadAsync(ModelReaderWriter.Write(options), cancellationToken);

        public IAsyncEnumerable<StreamingResponseUpdate> CreateResponseStreamingAsync(
            BinaryData wireBody, CancellationToken cancellationToken) => ReadAsync(wireBody, cancellationToken);

        private async IAsyncEnumerable<StreamingResponseUpdate> ReadAsync(
            BinaryData body, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(body);
            WireTier = document.RootElement.GetProperty("service_tier").GetString();
            ScopedTier = ProviderRequestServiceTierScope.Current;
            yield return terminal;
        }
    }

    private sealed class EmptyChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
