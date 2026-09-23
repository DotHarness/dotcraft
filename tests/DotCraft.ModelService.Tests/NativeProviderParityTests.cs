using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Agents.Remote;
using DotCraft.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DotCraft.ModelService.Tests;

public sealed class NativeProviderParityTests
{
    [Theory]
    [InlineData(ModelProviderProtocols.OpenAIChatCompletions)]
    [InlineData(ModelProviderProtocols.OpenAIResponses)]
    [InlineData(ModelProviderProtocols.Anthropic)]
    public async Task NativeClientsShapeTheSameRequestsAndParseTheSameToolStreams(string protocol)
    {
        var requests = new List<JsonNode>();
        var headers = new List<string>();
        var upstreamBuilder = WebApplication.CreateSlimBuilder();
        upstreamBuilder.Logging.ClearProviders();
        await using var upstream = upstreamBuilder.Build();
        upstream.Urls.Add("http://127.0.0.1:0");
        upstream.MapPost("/{**operation}", async context =>
        {
            requests.Add(JsonNode.Parse(await new StreamReader(context.Request.Body).ReadToEndAsync())!);
            headers.Add(protocol == ModelProviderProtocols.Anthropic
                ? context.Request.Headers["x-api-key"].ToString() : context.Request.Headers.Authorization.ToString());
            context.Response.ContentType = "text/event-stream";
            await context.Response.WriteAsync(protocol switch
            {
                ModelProviderProtocols.Anthropic => AnthropicStream,
                ModelProviderProtocols.OpenAIResponses => ResponsesStream,
                _ => OpenAIStream
            });
        });
        await upstream.StartAsync();
        var endpoint = upstream.Urls.Single();
        var runtime = new EffectiveModelRuntime("primary", "test-model", protocol, "Primary", "upstream-secret",
            endpoint, 30, 100, ModelProviderCapabilities.ForProtocol(protocol));
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        var access = new Access(runtime);
        builder.Services.AddSingleton<IModelServiceAccess>(access);
        builder.Services.AddSingleton<IModelServiceObserver>(access);
        builder.Services.AddDotCraftModelService();
        await using var service = builder.Build();
        service.MapDotCraftModelService();
        await service.StartAsync();
        using var serviceClient = service.GetTestClient();
        using var transport = new RemoteProviderTransport(new(new Uri("http://localhost/model-service/"), "caller"), serviceClient);
        var direct = await ExecuteAsync(runtime, null);
        var remote = await ExecuteAsync(runtime with { ApiKey = "", IsRemote = true }, transport);
        Assert.True(JsonNode.DeepEquals(requests[0], requests[1]), $"{requests[0]}\n{requests[1]}");
        Assert.Equal(headers[0], headers[1]);
        Assert.Equal(protocol == ModelProviderProtocols.Anthropic ? "upstream-secret" : "Bearer upstream-secret", headers[1]);
        Assert.Equal(direct, remote);
        Assert.Contains("lookup", remote);
        Assert.Contains("value", remote);
        Assert.Equal("child", access.Call!.Context.Conversation!.CurrentThreadId);
        Assert.Equal("root", access.Call.Context.Conversation.RootThreadId);
        Assert.Equal("turn", access.Call.Context.Conversation.TurnId);
        Assert.Equal("test-model", access.Call.Context.Model);
        Assert.Equal(new ProviderHttpUsage(12, 8), access.Call.Usage);
        if (protocol == ModelProviderProtocols.OpenAIResponses)
        {
            foreach (var caller in new[] { "environment-a", "environment-b", "environment-a" })
                await ExecuteAsync(runtime with { ApiKey = "", IsRemote = true, CallerNamespace = caller }, transport, "root");
            Assert.NotEqual(requests[2]["prompt_cache_key"]!.GetValue<string>(), requests[3]["prompt_cache_key"]!.GetValue<string>());
            Assert.Equal(requests[2]["prompt_cache_key"]!.GetValue<string>(), requests[4]["prompt_cache_key"]!.GetValue<string>());
            Assert.Equal("root", access.Call.Context.Conversation!.RootThreadId);
        }
    }

    [Fact]
    public void CatalogRoundTripPreservesResolvedRequestAdapters()
    {
        var thinking = new ModelThinkingAdapterCatalog.AnthropicThinkingAdapterData { ThinkingType = "adaptive" };
        thinking.OutputConfigEffortMap["extraHigh"] = "max";
        var model = new ProviderRequestAdaptation(true, true, thinking,
            new ModelThinkingAdapterCatalog.AnthropicMessageContentAdapterData { ReasoningHistoryBlockType = "reasoning" });
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var roundtrip = JsonSerializer.Deserialize<ProviderRequestAdaptation>(JsonSerializer.Serialize(model, json), json)!;
        Assert.Equal("max", roundtrip.AnthropicThinking!.OutputConfigEffortMap["extraHigh"]);
        Assert.Equal("reasoning", roundtrip.AnthropicMessageContent!.ReasoningHistoryBlockType);
        Assert.True(roundtrip.UseResponsesLite);
    }

    private static async Task<string> ExecuteAsync(EffectiveModelRuntime runtime, IProviderHttpTransport? transport, string? cacheKey = null)
    {
        IModelProvider provider = runtime.Protocol == ModelProviderProtocols.Anthropic
            ? new AnthropicClientProvider(transport) : new OpenAIClientProvider(httpTransport: transport);
        using var client = provider.CreateChatClient(runtime);
        using var pipeline = ProviderPipelineOptionsScope.Push(new ProviderPipelineOptions(runtime, null, null, false, "standard", true, "5m"));
        using var context = ProviderRequestContextScope.Push(new ProviderRequestContext(
            new ProviderConversationIdentity("child", "root", "root", null, "turn", "window", ProviderRequestKind.Turn, 0, "test", null)));
        var updates = new List<ChatResponseUpdate>();
        var options = new ChatOptions { Tools = [AIFunctionFactory.Create((string value) => value, "lookup")] };
        if (cacheKey is not null)
            ProviderPromptCacheMetadata.ApplyKey(options, cacheKey);
        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.System, "Use the lookup tool."), new ChatMessage(ChatRole.User, "Find the value.")],
            options))
            updates.Add(update);
        var tool = Assert.Single(updates.SelectMany(update => update.Contents).OfType<FunctionCallContent>());
        return tool.Name + ":" + JsonSerializer.Serialize(tool.Arguments);
    }

    private sealed class Access(EffectiveModelRuntime runtime) : IModelServiceAccess, IModelServiceObserver
    {
        public ModelServiceCall? Call { get; private set; }
        public ValueTask<ModelServiceCaller?> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ModelServiceCaller?>(new("test"));
        public Task<IReadOnlyList<ModelServiceProvider>> GetProvidersAsync(ModelServiceCaller caller, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelServiceProvider>>([new(new("primary", "Primary", runtime.Protocol,
                runtime.EndPoint, "apiKey", true, new(true)), runtime)]);
        public ValueTask<IDisposable?> BeginRequestAsync(ModelServiceCaller caller, ModelServiceRequestContext request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IDisposable?>(null);
        public ValueTask OnCompletedAsync(ModelServiceCall call, CancellationToken cancellationToken)
        {
            Call = call;
            return ValueTask.CompletedTask;
        }
    }

    private static readonly string ResponsesStream = BuildResponsesStream();

    private static string BuildResponsesStream()
    {
        var item = new { type = "function_call", id = "fc_1", call_id = "call_1", name = "lookup", arguments = "{\"value\":\"answer\"}", status = "completed" };
        object[] events =
        [
            new { type = "response.created", sequence_number = 0, response = new { id = "resp_1", object_type = "response", created_at = 1, status = "in_progress", model = "test-model", output = Array.Empty<object>() } },
            new { type = "response.output_item.added", sequence_number = 1, output_index = 0, item = new { type = "function_call", id = "fc_1", call_id = "call_1", name = "lookup", arguments = "", status = "in_progress" } },
            new { type = "response.function_call_arguments.delta", sequence_number = 2, item_id = "fc_1", output_index = 0, delta = item.arguments },
            new { type = "response.function_call_arguments.done", sequence_number = 3, item_id = "fc_1", output_index = 0, arguments = item.arguments },
            new { type = "response.output_item.done", sequence_number = 4, output_index = 0, item },
            new { type = "response.completed", sequence_number = 5, response = new { id = "resp_1", created_at = 1, status = "completed", model = "test-model", output = new[] { item }, usage = new { input_tokens = 12, output_tokens = 8, total_tokens = 20 } } }
        ];
        return string.Concat(events.Select(value => "data: " + JsonSerializer.Serialize(value) + "\n\n"));
    }

    private const string OpenAIStream = """
        data: {"id":"chat_1","object":"chat.completion.chunk","created":1,"model":"test-model","choices":[{"index":0,"delta":{"role":"assistant","tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"lookup","arguments":""}}]}}]}

        data: {"id":"chat_1","object":"chat.completion.chunk","created":1,"model":"test-model","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"value\":\"answer\"}"}}]},"finish_reason":"tool_calls"}]}

        data: {"id":"chat_1","object":"chat.completion.chunk","created":1,"model":"test-model","choices":[],"usage":{"prompt_tokens":12,"completion_tokens":8,"total_tokens":20}}

        data: [DONE]


        """;
    private const string AnthropicStream = """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"test-model","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":12,"output_tokens":0}}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"call_1","name":"lookup","input":{}}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"value\":\"answer\"}"}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":0}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"tool_use","stop_sequence":null},"usage":{"output_tokens":8}}

        event: message_stop
        data: {"type":"message_stop"}


        """;
}
