using System.ClientModel;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Core.Tests.Agents;

public sealed class ChatGptCompactionLiveTests
{
    [ChatGptCompactionLiveFact]
    public async Task SessionServiceCompactsInstructionsAndContinuesAfterColdResume()
    {
        var userData = Environment.GetEnvironmentVariable("DOTCRAFT_CHATGPT_SMOKE_USER_DATA")!;
        var model = Environment.GetEnvironmentVariable("DOTCRAFT_CHATGPT_SMOKE_MODEL")!;
        var workspace = Path.Combine(Path.GetTempPath(), "ChatGptCompact_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        await File.WriteAllTextAsync(Path.Combine(workspace, "AGENTS.md"), "Answer briefly. The project color is blue.");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            var config = AppConfigTestFactory.CreateOpenAI(model);
            config.Providers["openai"].Protocol = ModelProviderProtocols.OpenAIResponses;
            config.Providers["openai"].AuthMethod = ModelProviderAuthMethods.ChatGptOAuth;
            config.Providers["openai"].EndPoint = ModelProviderDefaults.ChatGptBackendEndpoint;
            var auth = new OpenAIAuthManager(new OpenAITokenStore(userData));
            Assert.True(auth.IsAuthenticated);
            var registry = new ChatClientRegistry(new OpenAIClientProvider(auth, new OpenAIInstallationIdProvider(userData)));
            const string threadId = "live-native-compaction";
            for (var pass = 0; pass < 2; pass++)
            {
                await using var factory = new AgentFactory(workspace, workspace, config,
                    new MemoryStore(workspace), new SkillsLoader(workspace), new AutoApproveApprovalService(),
                    blacklist: null, toolSources: [], chatClientRegistry: registry);
                var persistence = new SessionPersistenceService(new ThreadStore(workspace));
                var service = new SessionService(factory, factory.CreateDefaultAgent(), persistence, new SessionGate());
                if (pass == 0)
                    await service.CreateThreadAsync(new SessionIdentity
                    {
                        WorkspacePath = workspace, ChannelName = "smoke", UserId = "smoke"
                    }, threadId: threadId, ct: cancellation.Token);
                else
                    await service.GetThreadAsync(threadId, cancellation.Token);

                await foreach (var _ in service.SubmitInputAsync(threadId,
                                   [new TextContent("The project color is blue. Confirm with one word.")], ct: cancellation.Token)) { }
                var compact = await service.CompactThreadAsync(threadId, cancellation.Token);
                Assert.True(compact.Outcome == "partial", compact.Message ?? compact.Outcome);
                await foreach (var _ in service.SubmitInputAsync(threadId,
                                   [new TextContent("What is the project color? Reply with one word.")], ct: cancellation.Token)) { }
                var history = await persistence.LoadModelHistoryAsync(threadId, cancellation.Token);
                Assert.Single(history, AgentInstructionsHistory.IsInstructions);
                Assert.Contains("blue", history.Last(message => message.Role == ChatRole.Assistant).Text,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(workspace, recursive: true);
        }
    }

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
