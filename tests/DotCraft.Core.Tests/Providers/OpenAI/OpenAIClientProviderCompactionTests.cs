using System.ClientModel;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Auth.OpenAI;
using Microsoft.Extensions.AI;
using Xunit;

#pragma warning disable OPENAI001

namespace DotCraft.Tests.Agents;

public sealed partial class OpenAIClientProviderTests
{
    [Fact]
    public async Task AuxiliaryRequestDoesNotReplayOrCaptureLegacyParentTurnState()
    {
        using var parentAttemptScope = ModelStreamAttemptRuntimeScope.Begin(7);
        var parentAttempt = ModelStreamAttemptRuntimeScope.Current!;
        parentAttempt.CaptureTransportResponse(202, "parent-request", null, null, null);
        await using var server = RecordingHttpServer.Start(JsonResponse(SuccessfulResponseJson,
            headers: new Dictionary<string, string> { [OpenAIAuthConstants.TurnStateHeader] = "aux-state" }));
        var provider = CreateOAuthProvider("11111111-2222-4333-8444-555555555555", "account-test");
        var client = provider.GetOpenAIClient(OAuthRuntime($"{server.Endpoint}/backend-api/codex")).GetResponsesClient();
        var legacy = CreateCodexRuntimeContext("main", "main-turn", "main-window");
        legacy.TryCaptureTurnState("main-state");
        using var legacyScope = OpenAIResponsesCodexRuntimeScope.Set(legacy);
        var identity = new ProviderConversationIdentity("aux", "root", null, null, "aux-turn", "aux-window",
            ProviderRequestKind.Compaction, 0, "user", null);
        using var auxiliary = new AuxiliaryProviderRequestScope(identity);
        await client.CreateResponseAsync(CreateNonStreamingResponseOptions("gpt-test", "maintenance"));
        var request = Assert.Single(server.Requests);
        Assert.False(request.Headers.ContainsKey(OpenAIAuthConstants.TurnStateHeader));
        Assert.Equal("aux", request.Headers[OpenAIAuthConstants.ThreadIdHeader]);
        Assert.Equal("aux-window", request.Headers[OpenAIAuthConstants.WindowIdHeader]);
        Assert.Equal("main-state", legacy.TurnState);
        Assert.Equal(202, parentAttempt.StatusCode);
        Assert.Equal("parent-request", parentAttempt.RequestId);
        Assert.Equal("aux-state", ProviderRequestContextScope.Current!.ConversationState!.ContinuationState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompactorUsesV2WireShapeAndPreservesRawReplacement(bool lite)
    {
        const string installation = "11111111-2222-4333-8444-555555555555";
        await using var server = RecordingHttpServer.Start(CompactSseResponse());
        var provider = CreateOAuthProvider(installation, "account-test");
        var runtime = OAuthRuntime($"{server.Endpoint}/backend-api/codex", "account-test", useResponsesLite: lite);
        using var scope = OpenAIResponsesCodexRuntimeScope.Set(CreateCodexRuntimeContext(
            "thread-v2", "turn-v2", "window-v2", requestKind: ProviderRequestKind.Compaction));
        var input = new ProviderNativeCompactionInput(
            [new ProviderHistoryItem("user", ReadObject("""{"type":"message","role":"user","content":[{"type":"input_text","text":"keep this"}]}"""))],
            1, "turn-v2");
        var replacement = await ((IProviderNativeCompactorFactory)provider).CreateCompactor(runtime)
            .CompactAsync(input, [new ChatMessage(ChatRole.User, "keep this")],
                new ChatOptions { Instructions = "stable instruction" }, CancellationToken.None);

        var request = Assert.Single(server.Requests);
        Assert.Equal("/backend-api/codex/responses", request.Path);
        using var body = JsonDocument.Parse(request.Body);
        var items = body.RootElement.GetProperty("input").EnumerateArray().ToArray();
        Assert.Equal("compaction_trigger", items[^1].GetProperty("type").GetString());
        Assert.Single(items, item => item.GetProperty("type").GetString() == "compaction_trigger");
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.True(body.RootElement.TryGetProperty("client_metadata", out _));
        Assert.Equal(lite, items[0].GetProperty("type").GetString() == "additional_tools");
        Assert.Equal(2, replacement.Items.Count);
        Assert.True(replacement.Items[1].Payload.GetProperty("future_field").GetBoolean());
        Assert.Equal("YWJj", replacement.Items[1].Payload.GetProperty("encrypted_content").GetString());
        Assert.Single(input.Items);
    }

    [Theory]
    [InlineData("data: {\"type\":\"response.failed\"}\n\n")]
    [InlineData("data: {\"type\":\"response.incomplete\"}\n\n")]
    [InlineData("data: {\"type\":\"error\"}\n\n")]
    [InlineData("data: [DONE]\n\n")]
    [InlineData("data: {\"type\":\"response.completed\",\"response\":{\"id\":\"x\"}}\n\n")]
    [InlineData("data: {\"type\":\"response.output_item.done\",\"item\":{\"type\":\"compaction\"}}\n\n")]
    [InlineData("data: {invalid}\n\n")]
    public async Task CompactTransportRejectsUnsuccessfulStreams(string stream)
    {
        await using var server = RecordingHttpServer.Start(new RecordingHttpServer.ResponseSpec(
            HttpStatusCode.OK, "text/event-stream", stream));
        var provider = CreateOAuthProvider("11111111-2222-4333-8444-555555555555", "account-test");
        var runtime = OAuthRuntime($"{server.Endpoint}/backend-api/codex", "account-test");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => provider.GetChatGptResponsesCompactTransport(runtime)
            .CompactAsync(new ChatGptResponsesCompactRequest { Model = "gpt-test", Input = [] }, CancellationToken.None));
        Assert.StartsWith("provider_compaction_invalid_response:", error.Message);
    }

    [Fact]
    public async Task CompactTransportIgnoresExtraOutputButRejectsDuplicateCompaction()
    {
        var extra = "data: {\"type\":\"response.output_item.done\",\"item\":{\"type\":\"function_call\",\"name\":\"unexpected_tool\"}}\n\n";
        using var valid = new MemoryStream(Encoding.UTF8.GetBytes(extra + CompactSseBody));
        var result = await SdkChatGptResponsesCompactTransport.ReadResponseAsync(valid, CancellationToken.None);
        Assert.Single(result.Output!);
        var duplicate = "data: {\"type\":\"response.output_item.done\",\"item\":{\"type\":\"compaction\"}}\n\n";
        using var invalid = new MemoryStream(Encoding.UTF8.GetBytes(duplicate + CompactSseBody));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            SdkChatGptResponsesCompactTransport.ReadResponseAsync(invalid, CancellationToken.None));
    }

    [Fact]
    public async Task CompactStreamCancellationAfterOutputDoesNotAcceptPartialResult()
    {
        var pipe = new Pipe();
        using var cancellation = new CancellationTokenSource();
        try
        {
            await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(
                "data: {\"type\":\"response.output_item.done\",\"item\":{\"type\":\"compaction\"}}\n\n"));
            var result = SdkChatGptResponsesCompactTransport.ReadResponseAsync(
                pipe.Reader.AsStream(leaveOpen: true), cancellation.Token);
            Assert.False(result.IsCompleted);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result);
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task CompactTransportRecoversAuthenticationWithoutChangingInput()
    {
        await using var server = RecordingHttpServer.Start(JsonResponse("{}", HttpStatusCode.Unauthorized), CompactSseResponse());
        var provider = CreateOAuthProvider("11111111-2222-4333-8444-555555555555", "account-test");
        var runtime = OAuthRuntime($"{server.Endpoint}/backend-api/codex", "account-test");
        await provider.GetChatGptResponsesCompactTransport(runtime).CompactAsync(
            new ChatGptResponsesCompactRequest { Model = "gpt-test", Input = [] }, CancellationToken.None);
        Assert.Equal(2, server.Requests.Count);
        Assert.Equal(server.Requests[0].Body, server.Requests[1].Body);
    }

    [Fact]
    public async Task CompactTransportPreservesHttpFailureWithoutLegacyRetry()
    {
        await using var server = RecordingHttpServer.Start(JsonResponse("{}", HttpStatusCode.NotFound));
        var provider = CreateOAuthProvider("11111111-2222-4333-8444-555555555555", "account-test");
        var runtime = OAuthRuntime($"{server.Endpoint}/backend-api/codex", "account-test");
        var error = await Assert.ThrowsAsync<ClientResultException>(() => provider.GetChatGptResponsesCompactTransport(runtime)
            .CompactAsync(new ChatGptResponsesCompactRequest { Model = "gpt-test", Input = [] }, CancellationToken.None));
        Assert.Equal(404, error.Status);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task CompactTransport_UsesResponsesV2OAuthAndStreamingBody()
    {
        const string installationId = "11111111-2222-4333-8444-555555555555";
        await using var server = RecordingHttpServer.Start(
            CompactSseResponse(
                headers: new Dictionary<string, string>
                {
                    [OpenAIAuthConstants.TurnStateHeader] = "next-turn-state"
                }));
        var provider = CreateOAuthProvider(installationId, "account-test");
        var runtime = OAuthRuntime($"{server.Endpoint}/backend-api/codex", "account-test");
        var context = CreateCodexRuntimeContext(
            "thread-test",
            "turn-test",
            "window-test",
            requestKind: ProviderRequestKind.Compaction);
        using var scope = OpenAIResponsesCodexRuntimeScope.Set(context);
        var compactRequest = new ChatGptResponsesCompactRequest
        {
            Model = "gpt-test",
            Input = [ReadObject("""{"type":"message","role":"user","content":[]}""")],
            Reasoning = ReadObject("{}")
        };

        var response = await provider
            .GetChatGptResponsesCompactTransport(runtime)
            .CompactAsync(compactRequest, CancellationToken.None);

        Assert.Equal("compaction", Assert.Single(response.Output!).GetProperty("type").GetString());
        var request = Assert.Single(server.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/backend-api/codex/responses", request.Path);
        Assert.Equal("application/json", request.Headers["Content-Type"]);
        Assert.Equal("Bearer access-token", request.Headers["Authorization"]);
        Assert.Equal("account-test", request.Headers[OpenAIAuthConstants.AccountIdHeader]);
        Assert.Equal(installationId, request.Headers[OpenAIAuthConstants.InstallationIdHeader]);
        Assert.Equal("thread-test", request.Headers[OpenAIAuthConstants.ThreadIdHeader]);
        Assert.Equal("window-test", request.Headers[OpenAIAuthConstants.WindowIdHeader]);
        Assert.Equal(
            OpenAIOAuthPipelinePolicy.BetaFeaturesValue,
            request.Headers[OpenAIOAuthPipelinePolicy.BetaFeaturesHeader]);
        AssertNoSdkPlatformMetadataHeaders(request);
        Assert.False(request.Headers.ContainsKey(
            OpenAIResponsesLiteHeadersPipelinePolicy.ResponsesLiteHeader));
        Assert.Equal("zstd", request.Headers["Content-Encoding"]);
        using var sent = JsonDocument.Parse(request.Body);
        Assert.True(sent.RootElement.TryGetProperty("client_metadata", out _));
        Assert.True(sent.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal("next-turn-state", context.TurnState);
    }

    [Fact]
    public async Task CompactTransport_UsesLiteContractWhenRuntimeMetadataEnablesIt()
    {
        await using var server = RecordingHttpServer.Start(CompactSseResponse());
        var provider = CreateOAuthProvider(
            "11111111-2222-4333-8444-555555555555",
            "account-test");
        var runtime = OAuthRuntime(
            $"{server.Endpoint}/backend-api/codex",
            "account-test",
            useResponsesLite: true);
        using var scope = OpenAIResponsesCodexRuntimeScope.Set(CreateCodexRuntimeContext(
            "thread-lite",
            "turn-lite",
            "window-lite",
            requestKind: ProviderRequestKind.Compaction));

        await provider.GetChatGptResponsesCompactTransport(runtime).CompactAsync(
            new ChatGptResponsesCompactRequest
            {
                Model = "gpt-test",
                Input = [],
                ParallelToolCalls = false
            },
            CancellationToken.None);

        var request = Assert.Single(server.Requests);
        Assert.Equal("true", request.Headers[OpenAIResponsesLiteHeadersPipelinePolicy.ResponsesLiteHeader]);
        Assert.Equal(
            OpenAIOAuthPipelinePolicy.BetaFeaturesValue,
            request.Headers[OpenAIOAuthPipelinePolicy.BetaFeaturesHeader]);
        Assert.Equal("zstd", request.Headers["Content-Encoding"]);
        using var body = JsonDocument.Parse(request.Body);
        Assert.False(body.RootElement.GetProperty("parallel_tool_calls").GetBoolean());
    }

    [Theory]
    [InlineData("<html>gateway failure</html>")]
    [InlineData("{\"output\":[{\"type\":\"compaction\"}]}")]
    public async Task CompactTransport_InvalidResponseUsesStableErrorCode(string responseBody)
    {
        await using var server = RecordingHttpServer.Start(JsonResponse(responseBody));
        var provider = CreateOAuthProvider(
            "11111111-2222-4333-8444-555555555555",
            "account-test");
        var runtime = OAuthRuntime($"{server.Endpoint}/backend-api/codex", "account-test");
        using var scope = OpenAIResponsesCodexRuntimeScope.Set(CreateCodexRuntimeContext(
            "thread-test",
            "turn-test",
            "window-test",
            requestKind: ProviderRequestKind.Compaction));
        var compactRequest = new ChatGptResponsesCompactRequest
        {
            Model = "gpt-test",
            Input = []
        };

        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await provider
                .GetChatGptResponsesCompactTransport(runtime)
                .CompactAsync(compactRequest, CancellationToken.None));

        Assert.StartsWith("provider_compaction_invalid_response:", error.Message);
    }

    private const string CompactSseBody = "data: {\"type\":\"response.output_item.done\",\"item\":{\"type\":\"compaction\",\"encrypted_content\":\"YWJj\",\"future_field\":true}}\n\n"
        + "data: {\"type\":\"response.completed\",\"response\":{\"id\":\"compact_1\",\"status\":\"completed\",\"usage\":{\"input_tokens\":100,\"output_tokens\":10}}}\n\n";

    private static RecordingHttpServer.ResponseSpec CompactSseResponse(
        IReadOnlyDictionary<string, string>? headers = null) =>
        new(HttpStatusCode.OK, "text/event-stream", CompactSseBody, headers);
}
