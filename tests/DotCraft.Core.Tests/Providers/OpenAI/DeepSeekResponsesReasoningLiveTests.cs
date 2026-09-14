using System.ClientModel;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context.Compaction;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Core.Tests.Agents;

public sealed class DeepSeekResponsesReasoningLiveTests
{
    [DeepSeekReasoningLiveFact]
    public async Task PlainReasoningSurvivesLocalCompactionAndToolContinuation()
    {
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(
            Environment.GetEnvironmentVariable("DOTCRAFT_DEEPSEEK_SMOKE_CONFIG")!));
        var settings = config.RootElement.GetProperty("Providers").GetProperty("deepseek");
        var key = settings.EnumerateObject().Single(property =>
            property.Name.Equals("ApiKey", StringComparison.OrdinalIgnoreCase)).Value.GetString()!;
        var endpoint = settings.GetProperty("EndPoint").GetString()!;
        Assert.Equal("api.deepseek.com", new Uri(endpoint).Host);
        var model = Environment.GetEnvironmentVariable("DOTCRAFT_DEEPSEEK_SMOKE_MODEL") ?? "deepseek-flash";
        await using var provider = new OpenAIClientProvider();
        var runtime = new EffectiveModelRuntime("deepseek", model, ModelProviderProtocols.OpenAIResponses,
            "DeepSeek isolated smoke", key, endpoint, 60, 2048,
            ModelProviderCapabilities.ForProtocol(ModelProviderProtocols.OpenAIResponses));
        using var client = provider.CreateChatClient(runtime);
        using var summaryClient = client.AsBuilder().ConfigureOptions(summaryOptions =>
            summaryOptions.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None }).Build();
        var tool = AIFunctionFactory.Create(() => "731", name: "lookup_code",
            description: "Returns the synthetic test code.");
        var options = new ChatOptions
        {
            ModelId = model, MaxOutputTokens = 2048,
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Low, Output = ReasoningOutput.Full },
            Tools = [tool], ToolMode = ChatToolMode.Auto
        };
        var identity = new ProviderConversationIdentity("deepseek-smoke", "deepseek-smoke", null, null,
            "turn-smoke", "window-smoke", ProviderRequestKind.Turn, 0, "user", null);
        var history = new OpenAIResponsesProviderHistoryContext(identity, "deepseek",
            ProviderHistorySnapshot.Empty(identity.ContextWindowId), [], null, null, null);
        using var scope = OpenAIResponsesProviderHistoryRuntimeScope.Set(history);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "The synthetic project color is blue."),
            new(ChatRole.Assistant, "The project color is blue."),
            new(ChatRole.User, "Call lookup_code once, then answer with only the returned code.")
        };
        var stage = "tool request";
        var shape = "";
        try
        {
            var response = await client.GetResponseAsync(messages, options, timeout.Token);
            var reasoning = response.Messages.SelectMany(message => message.Contents).OfType<TextReasoningContent>().ToArray();
            Assert.NotEmpty(reasoning);
            Assert.All(reasoning, content =>
            {
                Assert.True(ResponsesReasoningMetadata.TryRead(content, out var native));
                Assert.True(native["content"] is System.Text.Json.Nodes.JsonArray { Count: > 0 });
            });
            var call = Assert.Single(response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>());
            messages.AddRange(response.Messages);
            history.MarkProjectionCovered(messages);
            messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, "731")]));
            options.ToolMode = ChatToolMode.None;

            stage = "ordinary tool continuation";
            var ordinary = await client.GetResponseAsync(messages, options, timeout.Token);
            Assert.Contains("731", ordinary.Text);

            stage = "local partial compaction";
            PartialCompactAttempt compacted;
            using (new AuxiliaryProviderRequestScope(identity with { RequestKind = ProviderRequestKind.Compaction }))
            {
                compacted = await new PartialCompactor(summaryClient, new CompactionConfig
                {
                    KeepRecentMinTokens = 1, KeepRecentMinGroups = 1, KeepRecentMaxTokens = 100_000,
                    SummaryMaxOutputTokens = 1024
                }).CompactAsync(messages, timeout.Token);
            }
            Assert.NotNull(compacted.Result);
            var replacement = new List<ChatMessage> { CompactionSummaryMessage.Create(compacted.Result!.FormattedSummary) };
            replacement.AddRange(compacted.Result.PreservedTail);
            var codec = new ModelHistoryCodec();
            var restored = replacement.Select(message => codec.Decode(codec.Encode(message, "turn-smoke"))).ToArray();
            await history.ReplaceAsync(restored, options, "compaction", timeout.Token);
            var prepared = await history.PrepareInputAsync(restored, options, timeout.Token);
            shape = string.Join("; ", prepared.Input.OfType<System.Text.Json.Nodes.JsonObject>().Select(item =>
                $"{item["type"]}/{item["role"]}:" + (item["content"] is System.Text.Json.Nodes.JsonArray parts
                    ? string.Join(",", parts.OfType<System.Text.Json.Nodes.JsonObject>().Select(part =>
                        $"{part["type"]}({part["text"]?.GetValue<string>().Length ?? 0})")) : "")));

            stage = "compacted tool continuation";
            var continued = await client.GetResponseAsync(restored, options, timeout.Token);
            Assert.Contains("731", continued.Text);
        }
        catch (ClientResultException ex)
        {
            var error = ex.Message.Contains("reasoning_text", StringComparison.Ordinal) ? "reasoning_text rejected"
                : ex.Message.Contains("tool_choice", StringComparison.Ordinal) ? "tool_choice rejected" : "request rejected";
            throw new InvalidOperationException($"DeepSeek reasoning smoke failed at {stage}: HTTP {ex.Status} ({error}). Shape: {shape}");
        }
    }

    private sealed class DeepSeekReasoningLiveFactAttribute : FactAttribute
    {
        public DeepSeekReasoningLiveFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTCRAFT_DEEPSEEK_SMOKE_CONFIG")))
                Skip = "Set DOTCRAFT_DEEPSEEK_SMOKE_CONFIG to opt in to the isolated official DeepSeek test.";
        }
    }
}
