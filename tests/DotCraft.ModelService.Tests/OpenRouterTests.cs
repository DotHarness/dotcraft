using System.Net;
using System.Text;
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

public sealed class OpenRouterTests
{
    [Theory]
    [InlineData("anthropic/claude-opus-5.5", "anthropic", "/api/v1/messages")]
    [InlineData("openai/gpt-test", "openai-responses", "/api/v1/responses")]
    [InlineData("deepseek/test", "openai-chat-completions", "/api/v1/chat/completions")]
    public async Task OneRemoteProviderRetainsModelRoutingAndServerOwnedCredentials(string model, string protocol, string path)
    {
        var seen = new List<(string Path, string Auth, string Body)>();
        var upstreamBuilder = WebApplication.CreateSlimBuilder(); upstreamBuilder.Logging.ClearProviders();
        await using var upstream = upstreamBuilder.Build(); upstream.Urls.Add("http://127.0.0.1:0");
        upstream.MapPost("/{**operation}", async context =>
        {
            seen.Add((context.Request.Path, context.Request.Headers.Authorization.ToString(), await new StreamReader(context.Request.Body).ReadToEndAsync()));
            Assert.False(context.Request.Headers.ContainsKey("x-api-key"));
            context.Response.ContentType = protocol == "openai-responses" ? "text/event-stream" : "application/json";
            await context.Response.WriteAsync(Response(protocol));
        });
        await upstream.StartAsync();
        var root = upstream.Urls.Single() + "/api/v1";
        var runtime = new EffectiveModelRuntime("router", "", "auto", "OpenRouter", "server-secret", root, 20, null, new())
        { BackendId = "openrouter", ConfiguredProtocol = "auto", ConfiguredEndPoint = root };
        var backend = new OpenRouterProvider(new(runtime, new Credentials(), default), catalogClient: new HttpClient(new Catalog()));
        var access = new Access(runtime, backend);
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IModelServiceAccess>(access); builder.Services.AddDotCraftModelService();
        await using var service = builder.Build(); service.MapDotCraftModelService(); await service.StartAsync();
        using var transport = new RemoteProviderTransport(new(new Uri("http://localhost/model-service/"), "worker-secret"), service.GetTestClient());
        var catalog = await transport.GetCatalogAsync();
        var description = Assert.Single(catalog.Providers);
        Assert.Equal("openrouter", description.BackendId);
        Assert.DoesNotContain("server-secret", JsonSerializer.Serialize(description));
        var workerRuntime = runtime with { ApiKey = "", IsRemote = true, RemoteModels = description.Models, RemoteCatalogState = description.CatalogState, Model = model };
        var worker = new OpenRouterProvider(new(workerRuntime, new Credentials(), default), transport);
        var resolved = worker.ResolveRuntime(workerRuntime);
        Assert.Equal(protocol, resolved.Protocol);
        using var pipeline = ProviderPipelineOptionsScope.Push(new(resolved, "Medium", "Full", true, "standard", true, "5m"));
        using var client = worker.CreateChatClient(resolved);
        var reply = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);
        Assert.Equal("ok", reply.Text);
        var request = Assert.Single(seen);
        Assert.Equal(path, request.Path);
        Assert.Equal("Bearer server-secret", request.Auth);
        Assert.Equal(model, JsonNode.Parse(request.Body)!["model"]!.GetValue<string>());

        using var rejected = new HttpRequestMessage(HttpMethod.Post, "http://localhost/model-service/providers/router/http/responses/compact")
        { Content = new StringContent(JsonSerializer.Serialize(new { model })) };
        rejected.Headers.Authorization = new("Bearer", "worker-secret");
        rejected.Headers.Add(ModelServiceProtocol.ContextHeader, Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new ModelServiceRequestContext(model, null))));
        using var invalid = await service.GetTestClient().SendAsync(rejected);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Single(seen);
    }

    [Theory]
    [InlineData("auto", 3)]
    [InlineData("anthropic", 2)]
    [InlineData("openai-responses", 2)]
    [InlineData("openai-chat-completions", 3)]
    public async Task CatalogFiltersModelsBeforeResolvingAFixedProtocol(string protocol, int expectedCount)
    {
        var runtime = new EffectiveModelRuntime("router", "", protocol, "OpenRouter", "test-key",
            OpenRouterModelCatalog.DefaultEndpoint, 20, null, new())
        { BackendId = "openrouter", ConfiguredProtocol = protocol };
        using var http = new HttpClient(new Catalog());
        var backend = new OpenRouterProvider(new(runtime, new Credentials(), default), catalogClient: http);
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IModelServiceAccess>(new Access(runtime, backend));
        builder.Services.AddDotCraftModelService();
        await using var service = builder.Build(); service.MapDotCraftModelService(); await service.StartAsync();
        using var transport = new RemoteProviderTransport(new(new Uri("http://localhost/model-service/"), "worker-secret"), service.GetTestClient());
        var provider = Assert.Single((await transport.GetCatalogAsync()).Providers);
        Assert.NotNull(provider.Models);
        Assert.Equal(expectedCount, provider.Models.Count);
        Assert.Contains("anthropic/claude-opus-5.5", provider.Models.Keys);
        Assert.Equal(protocol is "auto" or "openai-chat-completions", provider.Models.ContainsKey("deepseek/test"));
    }

    private static string Response(string protocol)
    {
        if (protocol == "anthropic") return """{"id":"msg_1","type":"message","role":"assistant","model":"test","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}""";
        if (protocol == "openai-chat-completions") return """{"id":"chat_1","object":"chat.completion","created":1,"model":"test","choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}""";
        object item = new { type = "message", id = "msg_1", role = "assistant", status = "completed", content = new[] { new { type = "output_text", text = "ok", annotations = Array.Empty<object>() } } };
        object[] events = [new { type = "response.created", response = new { id = "resp_1", created_at = 1, status = "in_progress", model = "test", output = Array.Empty<object>() } },
            new { type = "response.output_item.added", output_index = 0, item },
            new { type = "response.output_text.delta", item_id = "msg_1", output_index = 0, content_index = 0, delta = "ok" },
            new { type = "response.output_item.done", output_index = 0, item },
            new { type = "response.completed", response = new { id = "resp_1", created_at = 1, status = "completed", model = "test", output = new[] { item }, usage = new { input_tokens = 1, output_tokens = 1, total_tokens = 2 } } }];
        return string.Concat(events.Select(value => "data: " + JsonSerializer.Serialize(value) + "\n\n"));
    }

    private sealed class Catalog : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var models = new[] { "anthropic/claude-opus-5.5", "openai/gpt-test", "deepseek/test" }.Select(id => new
            { id, name = id, context_length = 1000000, top_provider = new { max_completion_tokens = 4096 },
                architecture = new { input_modalities = new[] { "text" }, output_modalities = new[] { "text" } },
                supported_parameters = new[] { "tools", "reasoning" }, reasoning = new { mandatory = true, supported_efforts = new[] { "medium" }, default_effort = "medium" },
                endpoints = id == "deepseek/test" ? new[] { "/api/v1/chat/completions" }
                    : new[] { "/api/v1/messages", "/api/v1/responses", "/api/v1/chat/completions" } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("/key")
                ? """{"data":{"usage":1,"limit_remaining":9,"limit":10}}""" : JsonSerializer.Serialize(new { data = models }), Encoding.UTF8, "application/json") });
        }
    }
    private sealed class Credentials : IProviderCredentialStore
    {
        public Task<JsonElement?> ReadAsync(CancellationToken ct) => Task.FromResult<JsonElement?>(null);
        public Task<JsonElement?> ModifyAsync(Func<JsonElement?, CancellationToken, Task<JsonElement?>> update, CancellationToken ct) => update(null, ct);
        public Task DeleteAsync(CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class Access(EffectiveModelRuntime runtime, OpenRouterProvider backend) : IModelServiceAccess
    {
        public ValueTask<ModelServiceCaller?> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken) => ValueTask.FromResult<ModelServiceCaller?>(new("worker"));
        public Task<IReadOnlyList<ModelServiceProvider>> GetProvidersAsync(ModelServiceCaller caller, CancellationToken ct) => Task.FromResult<IReadOnlyList<ModelServiceProvider>>([
            new(new("router", "OpenRouter", "auto", runtime.EndPoint, "apiKey", false, false, new(true), BackendId: "openrouter"), runtime, Backend: backend)]);
        public ValueTask<IDisposable?> BeginRequestAsync(ModelServiceCaller caller, ModelServiceRequestContext request, CancellationToken ct) => ValueTask.FromResult<IDisposable?>(null);
    }
}
