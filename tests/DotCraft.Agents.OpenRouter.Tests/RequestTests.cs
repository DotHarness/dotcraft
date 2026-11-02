using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Anthropic.Models.Beta.Messages;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using OpenAI.Responses;
using Xunit;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

#pragma warning disable OPENAI001

namespace DotCraft.Tests.OpenRouter;

public sealed class RequestTests
{
    [Theory]
    [InlineData("anthropic", "/api/v1/messages", ProviderReasoningEffort.Medium, "medium")]
    [InlineData("anthropic", "/api/v1/messages", ProviderReasoningEffort.ExtraHigh, "xhigh")]
    [InlineData("anthropic", "/api/v1/messages", ProviderReasoningEffort.Max, "max")]
    [InlineData("openai-chat-completions", "/api/v1/chat/completions", ProviderReasoningEffort.Medium, "medium")]
    [InlineData("openai-chat-completions", "/api/v1/chat/completions", ProviderReasoningEffort.ExtraHigh, "xhigh")]
    [InlineData("openai-chat-completions", "/api/v1/chat/completions", ProviderReasoningEffort.Max, "max")]
    [InlineData("openai-responses", "/api/v1/responses", ProviderReasoningEffort.Medium, "medium")]
    [InlineData("openai-responses", "/api/v1/responses", ProviderReasoningEffort.ExtraHigh, "xhigh")]
    [InlineData("openai-responses", "/api/v1/responses", ProviderReasoningEffort.Max, "max")]
    public async Task NativeSdkRequestsUseBearerAndPreserveIdAndPreferences(string protocol, string path, ProviderReasoningEffort effort, string expectedEffort)
    {
        using var server = new LocalServer();
        var runtime = RoutingTests.Runtime() with { Protocol = protocol, ConfiguredProtocol = protocol,
            EndPoint = server.Root, ConfiguredEndPoint = server.Root, Model = "anthropic/claude-opus-5.5",
            BackendSettingsJson = """{"order":["anthropic","amazon-bedrock"],"only":["anthropic"],"ignore":["other"],"sort":"latency","allow_fallbacks":false,"require_parameters":true}""" };
        var provider = RoutingTests.Create(runtime);
        var captured = server.Reply(protocol);
        using var client = provider.CreateChatClient(runtime);
        var options = new ChatOptions
        {
            RawRepresentationFactory = _ => protocol switch
            {
                "anthropic" => new MessageCreateParams { Model = runtime.Model, MaxTokens = 128, Messages = [] },
                "openai-responses" => new CreateResponseOptions { MaxOutputTokenCount = 128 },
                _ => new ChatCompletionOptions { MaxOutputTokenCount = 128 }
            }
        };
        ProviderReasoningOptions.Apply(options, true, effort, ReasoningOutput.Full);
        using var pipeline = ProviderPipelineOptionsScope.Push(new(runtime, null, null, true, "standard", true, "5m"));
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], options);
        Assert.Equal("ok", response.Text);
        Assert.False(response.AdditionalProperties?.ContainsKey(ProviderReportedCost.Key) == true);
        var request = await captured;
        Assert.Equal(path, request.Path);
        Assert.Equal("Bearer test-key", request.Authorization);
        Assert.Null(request.ApiKey);
        Assert.Equal("anthropic/claude-opus-5.5", request.Body["model"]!.GetValue<string>());
        Assert.Equal(128, request.Body[protocol == "anthropic" ? "max_tokens"
            : protocol == "openai-responses" ? "max_output_tokens" : "max_completion_tokens"]!.GetValue<int>());
        Assert.Equal("anthropic", request.Body["provider"]!["only"]![0]!.GetValue<string>());
        Assert.Equal(["anthropic", "amazon-bedrock"], request.Body["provider"]!["order"]!.AsArray().Select(item => item!.GetValue<string>()));
        Assert.Equal("other", request.Body["provider"]!["ignore"]![0]!.GetValue<string>());
        Assert.Equal("latency", request.Body["provider"]!["sort"]!.GetValue<string>());
        Assert.False(request.Body["provider"]!["allow_fallbacks"]!.GetValue<bool>());
        Assert.True(request.Body["provider"]!["require_parameters"]!.GetValue<bool>());
        if (protocol == "anthropic")
        {
            Assert.Equal("adaptive", request.Body["thinking"]!["type"]!.GetValue<string>());
            Assert.Equal(expectedEffort, request.Body["output_config"]!["effort"]!.GetValue<string>());
        }
        else if (protocol == "openai-chat-completions")
            Assert.Equal(expectedEffort, request.Body["reasoning"]!["effort"]!.GetValue<string>());
        else
        {
            Assert.False(request.Body["store"]!.GetValue<bool>());
            Assert.Equal(expectedEffort, request.Body["reasoning"]!["effort"]!.GetValue<string>());
        }
        if (protocol != "anthropic") Assert.Equal("ephemeral", request.Body["cache_control"]!["type"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("anthropic", true)]
    [InlineData("openai-chat-completions", true)]
    [InlineData("openai-responses", true)]
    [InlineData("anthropic", false)]
    [InlineData("openai-chat-completions", false)]
    [InlineData("openai-responses", false)]
    public async Task ThinkingUsesNativeDisableAndOmitsUnsupportedEffort(string protocol, bool supportsEffort)
    {
        using var server = new RequestTests.LocalServer();
        var catalog = JsonNode.Parse(CatalogTests.Catalog)!;
        var entry = catalog["data"]![0]!;
        entry["id"] = "anthropic/test";
        entry["endpoints"] = new JsonArray("/api/v1/messages", "/api/v1/chat/completions", "/api/v1/responses");
        entry["reasoning"]!["mandatory"] = false;
        entry["reasoning"]!["supported_efforts"] = supportsEffort ? new JsonArray("medium", "high") : new JsonArray();
        var runtime = RoutingTests.Runtime() with { Protocol = protocol, ConfiguredProtocol = protocol,
            EndPoint = server.Root, ConfiguredEndPoint = server.Root, Model = "anthropic/test" };
        using var http = new HttpClient(new CatalogTests.Replies((_, _) => CatalogTests.Reply(catalog.ToJsonString())));
        var provider = RoutingTests.Create(runtime, http);
        Assert.True((await provider.FetchModelsAsync(runtime, default)).Success);
        using var client = provider.CreateChatClient(runtime);
        var captured = server.Reply(protocol);
        var options = new ChatOptions();
        ProviderReasoningOptions.Apply(options, true, supportsEffort ? ProviderReasoningEffort.None : ProviderReasoningEffort.High, ReasoningOutput.Full);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], options);
        var body = (await captured).Body;
        if (protocol == "anthropic")
        {
            Assert.Equal(supportsEffort ? "disabled" : "adaptive", body["thinking"]!["type"]!.GetValue<string>());
            Assert.Null(body["output_config"]?["effort"]);
        }
        else if (protocol == "openai-chat-completions")
        {
            Assert.Equal(!supportsEffort, body["reasoning"]!["enabled"]!.GetValue<bool>());
            Assert.Null(body["reasoning"]!["effort"]);
            Assert.Null(body["reasoning_effort"]);
        }
        else if (supportsEffort) Assert.Equal("none", body["reasoning"]!["effort"]!.GetValue<string>());
        else Assert.Null(body["reasoning"]?["effort"]);
    }

    [Theory]
    [InlineData("anthropic")]
    [InlineData("openai-chat-completions")]
    [InlineData("openai-responses")]
    public async Task CancellationStopsAnInFlightSdkRequest(string protocol)
    {
        using var server = new RequestTests.LocalServer();
        var runtime = RoutingTests.Runtime() with { Protocol = protocol, ConfiguredProtocol = protocol,
            EndPoint = server.Root, ConfiguredEndPoint = server.Root, Model = "anthropic/claude-opus-5.5" };
        using var client = RoutingTests.Create(runtime).CreateChatClient(runtime);
        using var cancellation = new CancellationTokenSource();
        var pending = client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: cancellation.Token)
            .ToAgentResponseAsync(cancellation.Token);
        var request = await server.AcceptAsync();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        request.Response.Abort();
    }

    internal sealed class LocalServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        public string Root { get; }
        internal LocalServer()
        {
            var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start();
            var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
            Root = $"http://127.0.0.1:{port}/api/v1";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/"); _listener.Start();
        }
        internal async Task<(string Path, string? Authorization, string? ApiKey, JsonObject Body)> Reply(string protocol, string? payload = null, bool streaming = false)
        {
            var context = await AcceptAsync();
            using var reader = new StreamReader(context.Request.InputStream);
            var body = JsonNode.Parse(await reader.ReadToEndAsync())!.AsObject();
            var json = payload ?? (protocol switch
            {
                "anthropic" => """{"id":"msg_test","type":"message","role":"assistant","model":"anthropic/claude-opus-5.5","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":5,"output_tokens":1,"cost":null}}""",
                "openai-chat-completions" => """{"id":"chatcmpl_test","object":"chat.completion","created":1,"model":"anthropic/claude-opus-5.5","choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":5,"completion_tokens":1,"total_tokens":6,"cost":null}}""",
                _ => """{"id":"resp_test","object":"response","created_at":1,"status":"completed","model":"anthropic/claude-opus-5.5","output":[{"id":"msg_test","type":"message","role":"assistant","status":"completed","content":[{"type":"output_text","text":"ok","annotations":[]}]}],"usage":{"input_tokens":5,"output_tokens":1,"total_tokens":6,"cost":null}}"""
            });
            if (protocol == "openai-responses" && !streaming)
            { json = ResponseEvents.Build(JsonNode.Parse(json)!.AsObject()); streaming = true; }
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.ContentType = streaming ? "text/event-stream" : "application/json";
            context.Response.ContentLength64 = bytes.Length;
            for (var offset = 0; offset < bytes.Length; offset += 17)
                await context.Response.OutputStream.WriteAsync(bytes.AsMemory(offset, Math.Min(17, bytes.Length - offset)));
            context.Response.Close();
            return (context.Request.Url!.AbsolutePath, context.Request.Headers["Authorization"], context.Request.Headers["x-api-key"], body);
        }
        internal Task<HttpListenerContext> AcceptAsync() => _listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(15));
        public void Dispose() => _listener.Close();
    }
}
