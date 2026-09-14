using System.ClientModel;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Core.Tests.Agents;

public sealed class ChatGptCompactionLiveTests
{
    [ChatGptCompactionLiveFact]
    public async Task V2CompactionReplacementIsAcceptedByNextResponsesRequest()
    {
        var userData = Environment.GetEnvironmentVariable("DOTCRAFT_CHATGPT_SMOKE_USER_DATA")!;
        var model = Environment.GetEnvironmentVariable("DOTCRAFT_CHATGPT_SMOKE_MODEL")!;
        var auth = new OpenAIAuthManager(new OpenAITokenStore(userData));
        Assert.True(auth.IsAuthenticated);
        var provider = new OpenAIClientProvider(auth, new OpenAIInstallationIdProvider(userData));
        var runtime = new EffectiveModelRuntime("openai", model, ModelProviderProtocols.OpenAIResponses,
            "ChatGPT smoke", "", ModelProviderDefaults.ChatGptBackendEndpoint, 120, null,
            ModelProviderCapabilities.ForProtocol(ModelProviderProtocols.OpenAIResponses),
            AuthMethod: ModelProviderAuthMethods.ChatGptOAuth, ProviderStateDirectory: userData);
        runtime = runtime with
        {
            UseResponsesLite = ((IProviderRuntimeMetadataResolver)provider).Resolve(runtime).UseLightweightResponses
        };
        var threadId = $"compaction-smoke-{Guid.NewGuid():N}";
        var identity = new ProviderConversationIdentity(threadId, threadId, null, null, "turn-smoke",
            Guid.NewGuid().ToString(), ProviderRequestKind.Compaction, 0, "user", null);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "This synthetic test's project color is blue."),
            new(ChatRole.Assistant, "The project color is blue.")
        };
        var input = new ProviderNativeCompactionInput(
            messages.Select((message, index) => new ProviderHistoryItem(index.ToString(), JsonSerializer.SerializeToElement(new
            {
                type = "message", role = message.Role.Value,
                content = new[] { new { type = message.Role == ChatRole.User ? "input_text" : "output_text", text = message.Text } }
            }))).ToArray(), messages.Count, identity.TurnId);
        var options = new ChatOptions { Instructions = "Answer concisely.", ModelId = model };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var stage = "compact";
        try
        {
            ProviderNativeCompactionReplacement replacement;
            using (ProviderRequestContextScope.Push(new ProviderRequestContext(identity,
                       ConversationState: new ProviderConversationState(identity))))
            {
                replacement = await ((IProviderNativeCompactorFactory)provider).CreateCompactor(runtime)
                    .CompactAsync(input, messages, options, cancellation.Token);
            }
            Assert.Single(replacement.Items, item => item.Payload.GetProperty("type").GetString() == "compaction");
            var turnIdentity = identity with { RequestKind = ProviderRequestKind.Turn };
            var history = new OpenAIResponsesProviderHistoryContext(turnIdentity, "openai",
                ProviderHistorySnapshot.Empty(identity.ContextWindowId), messages, null, null, null);
            using var scope = ProviderRequestContextScope.Push(new ProviderRequestContext(turnIdentity,
                history, history, ConversationState: new ProviderConversationState(turnIdentity)));
            await history.ReplaceAsync(replacement, cancellation.Token);
            messages.Add(new ChatMessage(ChatRole.User, "What is the project color? Reply with one word."));
            stage = "next response";
            var result = await provider.GetOpenAIResponsesChatClient(runtime)
                .GetResponseAsync(messages, options, cancellation.Token);
            Assert.Contains("blue", result.Text, StringComparison.OrdinalIgnoreCase);
        }
        catch (ClientResultException ex)
        {
            throw new InvalidOperationException($"ChatGPT compaction smoke ({stage}, lite={runtime.UseResponsesLite}) failed with HTTP {ex.Status}.");
        }
    }

    private sealed class ChatGptCompactionLiveFactAttribute : FactAttribute
    {
        public ChatGptCompactionLiveFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTCRAFT_CHATGPT_SMOKE_USER_DATA"))
                || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTCRAFT_CHATGPT_SMOKE_MODEL")))
                Skip = "Set DOTCRAFT_CHATGPT_SMOKE_USER_DATA and DOTCRAFT_CHATGPT_SMOKE_MODEL to run the isolated OAuth smoke test.";
        }
    }
}
