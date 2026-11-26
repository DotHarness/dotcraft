using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Agents.Remote;
using DotCraft.Configuration;
using DotCraft.Contributions;
using DotCraft.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DotCraft.ModelService.Tests;

public sealed class RoutedBackendTests : IDisposable
{
    private readonly string _state = Directory.CreateTempSubdirectory().FullName;
    private const int ResolvedOutputLimit = 123;

    [Fact]
    public async Task OrdinaryRegistrationRoutesNativeModelsWithoutBackendOnWorker()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ct = deadline.Token;
        File.WriteAllText(Path.Combine(_state, "config.json"), """
            {
              "providers": {
                "gateway": {
                  "backendId": "routed-fixture",
                  "protocol": "auto",
                  "backendSettings": { "privateOption": "server-only" }
                }
              }
            }
            """);
        var client = new ModelServiceClientStore(_state).Create("worker", ["gateway"]);
        var credentials = new ProviderCredentialStore(_state, "routed-fixture", "gateway");
        await credentials.ModifyAsync((_, _) => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement("upstream-key")), ct);
        using var backendResolver = new ModelBackendResolver(new ModelProviderRegistry([]), new ContributionRegistry(), _state, [new FixtureBackend()]);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IModelServiceAccess>(_ => new FileModelServiceAccess(_state, backends: backendResolver));
        builder.Services.AddDotCraftModelService();
        var upstream = new GatewayHandler();
        builder.Services.AddHttpClient("DotCraft.ModelService.Upstream").ConfigurePrimaryHttpMessageHandler(() => upstream);
        await using var service = builder.Build();
        service.MapDotCraftModelService();
        await service.StartAsync(ct);
        using var serviceClient = service.GetTestClient();
        using var transport = new RemoteProviderTransport(new(new Uri("http://localhost/model-service/"), client.Token), serviceClient);
        var catalog = await transport.GetCatalogAsync(ct);
        var provider = Assert.Single(catalog.Providers);
        Assert.Null(provider.Authentication);
        var serialized = JsonSerializer.Serialize(provider);
        Assert.DoesNotContain("upstream-key", serialized);
        Assert.DoesNotContain("gateway.invalid", serialized);
        Assert.DoesNotContain("server-only", serialized);

        var config = new AppConfig();
        var workerServices = new ServiceCollection();
        workerServices.AddDotCraftHarness(config, options =>
        {
            options.WorkspacePath = _state;
            options.DataPath = "worker";
            options.ModelService = new(new Uri("http://localhost/model-service/"), client.Token);
            options.InitialModelServiceCatalog = catalog;
            options.RefreshModelServiceCatalog = false;
        });
        workerServices.AddSingleton<IProviderHttpTransport>(transport);
        await using var worker = workerServices.BuildServiceProvider();
        var resolver = worker.GetRequiredService<IModelRuntimeResolver>();
        Assert.True(ModelProviderResolver.IsProviderUsable(config, "gateway"));
        foreach (var (model, protocol, path) in GatewayHandler.Models)
        {
            var runtime = ModelProviderResolver.ResolveMain(config, "gateway", model);
            using var scope = ProviderPipelineOptionsScope.Push(new(runtime, null, null, false, "standard", false, null));
            using var chat = resolver.Resolve(runtime).CreateChatClient(runtime);
            var result = await chat.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: ct);
            Assert.Equal((model, path), upstream.Requests.Last());
            Assert.True(result.Text == "ok", $"Expected native {protocol} content, found '{result.Text}'.");
        }

        using var forbidden = new HttpRequestMessage(HttpMethod.Post, "model-service/providers/gateway/http/responses")
        {
            Content = JsonContent.Create(new { model = "opus" })
        };
        forbidden.Headers.Authorization = new("Bearer", client.Token);
        forbidden.Headers.Add(ModelServiceProtocol.ContextHeader,
            Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new ModelServiceRequestContext("opus", null), new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        using var rejection = await serviceClient.SendAsync(forbidden, ct);
        Assert.Equal(HttpStatusCode.BadRequest, rejection.StatusCode);
        Assert.Equal(3, upstream.Requests.Count);

        await credentials.ModifyAsync((_, _) => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement("rotated-key")), ct);
        upstream.ExpectedKey = "rotated-key";
        var revised = ModelProviderResolver.ResolveMain(config, "gateway", "deepseek");
        using var revisedScope = ProviderPipelineOptionsScope.Push(new(revised, null, null, false, "standard", false, null));
        using var revisedChat = resolver.Resolve(revised).CreateChatClient(revised);
        Assert.Equal("ok", (await revisedChat.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: ct)).Text);
        Assert.Equal(4, upstream.Requests.Count);
    }

    private sealed class FixtureBackend : IModelBackend
    {
        public ModelBackendDescriptor Descriptor { get; } = new("routed-fixture", "Gateway", "auto",
            ["auto", .. GatewayHandler.Models.Select(item => item.Protocol)],
            [new("vault", "Gateway key", "apiKey")], new());

        public IModelProvider CreateProvider(ModelBackendContext context) => new GatewayProvider(context);
    }

    private sealed class GatewayProvider(ModelBackendContext context) :
        IModelProvider, IModelCatalogProvider, IProviderNativeHttpRouter, IProviderModelRuntimeResolver
    {
        private readonly string? _key = context.Credentials.ReadAsync(context.Stopping).GetAwaiter().GetResult()?.GetString();
        public IReadOnlyCollection<string> Protocols => ["auto", .. GatewayHandler.Models.Select(item => item.Protocol)];

        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) =>
            throw new InvalidOperationException("The server must not create a chat client for remote inference.");

        public Task<ModelCatalogResult> FetchModelsAsync(EffectiveModelRuntime runtime, CancellationToken cancellationToken) =>
            Task.FromResult(new ModelCatalogResult
            {
                Success = true,
                Models = GatewayHandler.Models.Select(item => new ModelCatalogEntry
                {
                    Id = item.Model, DefaultProtocol = item.Protocol, SupportedProtocols = [item.Protocol],
                    MaxOutputTokens = item.Protocol == ModelProviderProtocols.Anthropic ? 2_000 : null
                }).ToList()
            });

        public EffectiveModelRuntime ResolveRuntime(EffectiveModelRuntime runtime) =>
            runtime.Model == "opus" ? runtime with { MaxOutputTokens = ResolvedOutputLimit } : runtime;

        public ProviderHttpRoute ResolveRoute(EffectiveModelRuntime runtime)
        {
            var (model, protocol, _) = GatewayHandler.Models.Single(item => item.Model == runtime.Model);
            return new ProviderHttpRoute(protocol, protocol == ModelProviderProtocols.Anthropic
                ? "https://gateway.invalid/anthropic" : "https://gateway.invalid/openai/v1", model);
        }

        public Task<IReadOnlyDictionary<string, string>> AuthenticateAsync(EffectiveModelRuntime runtime, CancellationToken cancellationToken)
        {
            var secret = _key ?? throw new ProviderAuthenticationException("NoGatewayKey", "The provider is not signed in.");
            return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { ["x-gateway-key"] = secret });
        }
    }

    private sealed class GatewayHandler : HttpMessageHandler
    {
        public static readonly (string Model, string Protocol, string Path)[] Models =
        [
            ("opus", ModelProviderProtocols.Anthropic, "/anthropic/v1/messages"),
            ("gpt", ModelProviderProtocols.OpenAIResponses, "/openai/v1/responses"),
            ("deepseek", ModelProviderProtocols.OpenAIChatCompletions, "/openai/v1/chat/completions")
        ];

        public List<(string Model, string Path)> Requests { get; } = [];
        public string ExpectedKey { get; set; } = "upstream-key";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("x-api-key"));
            Assert.Equal(ExpectedKey, Assert.Single(request.Headers.GetValues("x-gateway-key")));
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var model = body.RootElement.GetProperty("model").GetString()!;
            if (model == "opus")
                Assert.Equal(ResolvedOutputLimit, body.RootElement.GetProperty("max_tokens").GetInt32());
            Requests.Add((model, request.RequestUri!.AbsolutePath));
            var content = model switch
            {
                "opus" => """{"id":"msg_1","type":"message","role":"assistant","model":"opus","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}""",
                "gpt" => ResponsesStream,
                _ => """{"id":"chat_1","object":"chat.completion","created":1,"model":"deepseek","choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}"""
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8,
                    model == "gpt" ? "text/event-stream" : "application/json")
            };
        }

        private const string ResponsesStream = """
            data: {"type":"response.created","response":{"id":"resp_1","created_at":1,"status":"in_progress","model":"gpt","output":[]}}

            data: {"type":"response.output_item.added","output_index":0,"item":{"type":"message","role":"assistant","id":"msg_1","status":"in_progress","content":[]}}

            data: {"type":"response.output_text.delta","item_id":"msg_1","output_index":0,"content_index":0,"delta":"ok"}

            data: {"type":"response.output_item.done","output_index":0,"item":{"type":"message","role":"assistant","id":"msg_1","status":"completed","content":[{"type":"output_text","text":"ok","annotations":[]}]}}

            data: {"type":"response.completed","response":{"id":"resp_1","created_at":1,"status":"completed","model":"gpt","output":[],"usage":{"input_tokens":1,"output_tokens":1,"total_tokens":2}}}


            """;
    }

    public void Dispose() => Directory.Delete(_state, recursive: true);
}
