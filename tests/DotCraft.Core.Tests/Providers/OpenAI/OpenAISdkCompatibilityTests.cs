using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Xunit;

#pragma warning disable OPENAI001, MEAI001, SCME0001

namespace DotCraft.Core.Tests.Agents;

public sealed class OpenAISdkCompatibilityTests
{
    [Theory]
    [InlineData(ReasoningEffort.None, "none")]
    [InlineData(ReasoningEffort.Low, "low")]
    [InlineData(ReasoningEffort.Medium, "medium")]
    [InlineData(ReasoningEffort.High, "high")]
    [InlineData(ReasoningEffort.ExtraHigh, "xhigh")]
    public void ReasoningEffort_PreservesWireAndDiagnosticValue(ReasoningEffort effort, string expected)
    {
        var request = ResponsesToolSearchMapper.CreateResponseRequest(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions { Reasoning = new ReasoningOptions { Effort = effort } });

        var standardBody = ModelReaderWriter.Write(request.Options);
        using var standard = JsonDocument.Parse(standardBody);
        using var lite = JsonDocument.Parse(OpenAIResponsesLiteRequestMapper.BuildWireBody(standardBody, "install-1"));

        Assert.Equal(expected, standard.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal(expected, lite.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal("all_turns", lite.RootElement.GetProperty("reasoning").GetProperty("context").GetString());
        Assert.Equal(expected, request.Shape.ReasoningEffort);
    }

    [Fact]
    public void Ultra_StillUsesExtraHighOnTheWire()
    {
        var request = ResponsesToolSearchMapper.CreateResponseRequest(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions
            {
                Reasoning = new ReasoningOptions { Effort = ModelReasoningEffort.Ultra.ToProviderEffort() }
            });

        using var body = JsonDocument.Parse(ModelReaderWriter.Write(request.Options));
        Assert.Equal("xhigh", body.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal("xhigh", request.Shape.ReasoningEffort);
    }

    [Fact]
    public void ReasoningEffort_PreservesCallerNativeOptions()
    {
        using var inner = new NoopChatClient();
        var request = ResponsesToolSearchMapper.CreateResponseRequest(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions
            {
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.ExtraHigh },
                RawRepresentationFactory = _ => new CreateResponseOptions
                {
                    ReasoningOptions = new ResponseReasoningOptions
                    {
                        ReasoningEffortLevel = ResponseReasoningEffortLevel.Max
                    }
                }
            },
            rawRepresentationClient: inner);

        using var body = JsonDocument.Parse(ModelReaderWriter.Write(request.Options));
        Assert.Equal("max", body.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fast_OverridesCallerTierPatchAcrossSubscriptionRequestShapes(bool removedTier)
    {
        using var inner = new NoopChatClient();
        using var fast = new OpenAIFastModeChatClient(inner);
        var runtime = new EffectiveModelRuntime(
            "test", "gpt-test", ModelProviderProtocols.OpenAIResponses, "test", "test-key",
            "https://api.openai.com/v1", 30, null,
            ModelProviderCapabilities.ForProtocol(ModelProviderProtocols.OpenAIResponses));
        using var scope = ProviderPipelineOptionsScope.Push(
            new ProviderPipelineOptions(runtime, null, null, false, "Fast", false, null));
        var options = fast.PrepareOptions(new ChatOptions
        {
            MaxOutputTokens = 123,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ResponsesToolSearchMapper.PromptCacheKeyAdditionalProperty] = "cache-key"
            },
            RawRepresentationFactory = _ =>
            {
                var raw = new CreateResponseOptions { ServiceTier = ResponseServiceTier.Flex };
                if (removedTier)
                    raw.Patch.Remove("$.service_tier"u8);
                else
                    raw.Patch.Set("$.service_tier"u8, BinaryData.FromString("\"default\""));
                raw.Patch.Set("$.client_metadata"u8, BinaryData.FromString("""{"caller":"retained"}"""));
                return raw;
            }
        });
        ChatMessage[] messages = [new(ChatRole.User, "hello")];
        var request = ResponsesToolSearchMapper.CreateResponseRequest(
            "gpt-test", messages, options, rawRepresentationClient: inner);
        var standardBody = ModelReaderWriter.Write(request.Options);
        using var standard = JsonDocument.Parse(standardBody);
        AssertPriorityAndCache(standard.RootElement);

        var oauthBody = OpenAIResponsesClientMetadataPipelinePolicy.RemoveUnsupportedOAuthResponsesFields(standardBody.ToString())!;
        oauthBody = OpenAIResponsesClientMetadataPipelinePolicy.AddInstallationIdMetadata(oauthBody, "install-1")!;
        using var oauth = JsonDocument.Parse(oauthBody);
        AssertPriorityAndCache(oauth.RootElement);
        Assert.False(oauth.RootElement.TryGetProperty("max_output_tokens", out _));
        Assert.Equal("retained", oauth.RootElement.GetProperty("client_metadata").GetProperty("caller").GetString());

        using var lite = JsonDocument.Parse(OpenAIResponsesLiteRequestMapper.BuildWireBody(standardBody, "install-1"));
        AssertPriorityAndCache(lite.RootElement);
        Assert.Equal("retained", lite.RootElement.GetProperty("client_metadata").GetProperty("caller").GetString());
        Assert.Equal("all_turns", lite.RootElement.GetProperty("reasoning").GetProperty("context").GetString());

        var input = new ProviderNativeCompactionInput(
            [new ProviderHistoryItem("input:0", standard.RootElement.GetProperty("input")[0].Clone())],
            CoveredMessageCount: 1,
            CoveredThroughTurnId: "turn-1");
        foreach (var useLite in new[] { false, true })
        {
            var compact = ChatGptResponsesCompactRequestBuilder.Build(
                "gpt-test", input, messages, options, useLite, inner, "install-1");
            var compactBody = JsonSerializer.SerializeToElement(compact, ChatGptResponsesCompactJson.Options);
            AssertPriorityAndCache(compactBody);
            Assert.Equal("compaction_trigger", compactBody.GetProperty("input").EnumerateArray().Last().GetProperty("type").GetString());
        }
    }

    private static void AssertPriorityAndCache(JsonElement body)
    {
        Assert.Single(body.EnumerateObject(), property => property.Name == "service_tier");
        Assert.Equal("priority", body.GetProperty("service_tier").GetString());
        Assert.Equal("cache-key", body.GetProperty("prompt_cache_key").GetString());
    }

    private sealed class NoopChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => Task.FromResult(new ChatResponse([]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
