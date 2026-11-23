using System.Net;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
using DotCraft.Tests.Agents.TestSupport;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed partial class ResponsesLiteWireTests
{
    [Fact]
    public async Task LiteCanonicalSteer_PersistsNewUserIdAndKeepsTheWirePrefixAcrossRetryAndResume()
    {
        await using var server = RawRecordingHttpServer.Start(
            SuccessfulSseResponse(),
            RawHttpResponse.Json("""{"error":{"message":"retryable","type":"server_error"}}""",
                HttpStatusCode.InternalServerError),
            SuccessfulSseResponse(),
            SuccessfulSseResponse());
        var provider = CreateProvider(new RecordingAuthService("acct-token"));
        var factory = (IProviderHistorySessionFactory)provider;
        var runtime = OAuthRuntime(server.Endpoint + "/backend-api/codex");
        var identity = CreateContext("thread-prefix", "turn-prefix", "window-prefix").ConversationIdentity;
        using var archived = JsonDocument.Parse("""
            [
              {"type":"message","role":"user","id":"msg_saved_user","content":[{"type":"input_text","text":"saved prompt"}]},
              {"type":"message","role":"assistant","id":"msg_provider_output","status":"completed","content":[{"type":"output_text","text":"saved reply","annotations":[]}]}
            ]
            """);
        var saved = HistoryBaseline(runtime, identity, archived.RootElement.EnumerateArray()
            .Select((item, index) => new ProviderHistoryItem($"saved-{index}", item.Clone())).ToArray());
        var sink = new WireHistorySink();
        var beforeSteer = new[] { new ChatMessage(ChatRole.User, "saved prompt"), new ChatMessage(ChatRole.Assistant, "saved reply") };
        var steer = new ChatMessage(ChatRole.User, "steer message");
        var options = new ChatOptions
        {
            Instructions = "Keep the base instruction prefix stable.",
            Tools = [AIFunctionFactory.Create(() => "unused", name: "lookup")]
        };
        var session = factory.CreateSession(runtime, identity, saved, beforeSteer, sink);
        using var client = new StreamRetryingChatClient(provider.CreateChatClient(runtime),
            new StreamRetryOptions(1, TimeSpan.FromSeconds(5)));
        using (UseHistory(identity, session))
        {
            await ConsumeAsync(client.GetStreamingResponseAsync(beforeSteer, options));
            await session.AppendLocalInputAsync([steer], CancellationToken.None);
            await ConsumeAsync(client.GetStreamingResponseAsync([.. beforeSteer, steer], options));
        }

        var persisted = Assert.Single(sink.LocalInput);
        var newId = UserItemId(persisted.Payload);
        AssertVersion7UserId(newId);

        var reloaded = JsonSerializer.Deserialize<OpaqueProviderHistorySnapshot>(JsonSerializer.SerializeToUtf8Bytes(
            saved with { Items = [.. saved.Items, persisted], CoveredThroughTurnId = identity.TurnId }))!;
        var resumedMessages = new[]
        {
            new ChatMessage(ChatRole.User, "saved prompt"),
            new ChatMessage(ChatRole.Assistant, "saved reply"),
            new ChatMessage(ChatRole.User, "steer message")
        };
        var resumedProvider = CreateProvider(new RecordingAuthService("acct-token"));
        var resumed = ((IProviderHistorySessionFactory)resumedProvider).CreateSession(
            runtime, identity, reloaded, resumedMessages, sink);
        using (UseHistory(identity, resumed))
        using (var resumedClient = resumedProvider.CreateChatClient(runtime))
            await ConsumeAsync(resumedClient.GetStreamingResponseAsync(resumedMessages, options));

        Assert.Single(sink.LocalInput);
        var requests = server.Requests;
        Assert.Equal(4, requests.Count);
        using var baseline = JsonDocument.Parse(Decompress(requests[0].Body));
        using var first = JsonDocument.Parse(Decompress(requests[1].Body));
        using var retry = JsonDocument.Parse(Decompress(requests[2].Body));
        using var cold = JsonDocument.Parse(Decompress(requests[3].Body));
        var baselineItems = baseline.RootElement.GetProperty("input").EnumerateArray().ToArray();
        var firstItems = first.RootElement.GetProperty("input").EnumerateArray().ToArray();
        Assert.Equal(baselineItems.Length + 1, firstItems.Length);
        Assert.Equal("msg_saved_user", UserItemId(baselineItems[^2]));
        Assert.Equal("msg_provider_output", baselineItems[^1].GetProperty("id").GetString());
        Assert.Equal(baselineItems.Select(item => item.GetRawText()),
            firstItems.Take(baselineItems.Length).Select(item => item.GetRawText()));
        Assert.Equal(newId, UserItemId(firstItems[^1]));
        Assert.Equal(first.RootElement.GetRawText(), retry.RootElement.GetRawText());
        Assert.Equal(first.RootElement.GetProperty("input").GetRawText(), cold.RootElement.GetProperty("input").GetRawText());
        foreach (var body in new[] { baseline, first, retry, cold })
            Assert.Equal(identity.RootThreadId, body.RootElement.GetProperty("prompt_cache_key").GetString());
    }

    [Fact]
    public async Task LiteCanonicalUserId_DiffersForIdenticalInputInSeparateThreads()
    {
        var factory = (IProviderHistorySessionFactory)CreateProvider(new RecordingAuthService("acct-token"));
        var runtime = OAuthRuntime("https://example.test/backend-api/codex");
        var first = await AppendSameInputAsync("thread-first");
        var second = await AppendSameInputAsync("thread-second");

        Assert.NotEqual(first, second);

        async Task<string> AppendSameInputAsync(string threadId)
        {
            var identity = CreateContext(threadId, "turn_001", threadId).ConversationIdentity;
            var session = factory.CreateSession(runtime, identity, HistoryBaseline(runtime, identity, []), [], sink: null);
            await session.AppendLocalInputAsync([new ChatMessage(ChatRole.User, "same content")], CancellationToken.None);
            return UserItemId(Assert.Single(session.CaptureOpaqueSnapshot().Items).Payload);
        }
    }

    private static OpaqueProviderHistorySnapshot HistoryBaseline(
        EffectiveModelRuntime runtime, ProviderConversationIdentity identity, IReadOnlyList<ProviderHistoryItem> items) =>
        new(new ProviderHistoryIdentity(runtime.ProviderId, runtime.Protocol, ProviderHistorySchema.CurrentSchemaVersion,
                identity.CurrentThreadId, identity.TurnId, identity.ContextWindowId, identity.ContextWindowId)
            { Model = runtime.Model }, items, CoveredThroughTurnId: null);

    private static IDisposable UseHistory(ProviderConversationIdentity identity, IProviderConversationHistory history) =>
        ProviderRequestContextScope.Push(new ProviderRequestContext(identity, History: history,
            ConversationState: new ProviderConversationState(identity)));

    private static string UserItemId(JsonElement item)
    {
        Assert.Equal("user", item.GetProperty("role").GetString());
        return item.GetProperty("id").GetString()!;
    }

    private static void AssertVersion7UserId(string id)
    {
        Assert.StartsWith("msg_", id);
        Assert.True(Guid.TryParseExact(id[4..], "D", out var guid));
        Assert.Equal(7, guid.Version);
    }

    private sealed class WireHistorySink : IProviderHistorySink
    {
        public List<ProviderHistoryItem> LocalInput { get; } = [];

        public ValueTask AppendAsync(ProviderHistoryIdentity identity, ProviderHistoryEntrySource source,
            string? attemptId, IReadOnlyList<ProviderHistoryItem> items, CancellationToken cancellationToken)
        {
            if (source == ProviderHistoryEntrySource.LocalInput)
                LocalInput.AddRange(items);
            return ValueTask.CompletedTask;
        }

        public ValueTask ReplaceAsync(ProviderHistoryIdentity identity, string? coveredThroughTurnId, string reason,
            IReadOnlyList<ProviderHistoryItem> items, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask AbortAttemptAsync(ProviderHistoryIdentity identity, string attemptId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
