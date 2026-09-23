using System.Net;
using System.Text;
using DotCraft.Agents;
using DotCraft.Agents.Remote;
using DotCraft.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotCraft.ModelService.Tests;

public sealed class ModelServiceTransportTests
{
    [Fact]
    public async Task NativeRequestAndStreamRetainWireSemanticsAndUseServerCredentials()
    {
        var bytes = new byte[] { 0x28, 0xb5, 0x2f, 0xfd, 1, 2, 3 };
        const string stream = "event: response.output_item.added\ndata: {\"type\":\"response.output_item.added\",\"item\":{\"type\":\"function_call\",\"name\":\"shell\"}}\n\ndata: {\"type\":\"response.completed\",\"response\":{\"usage\":{\"input_tokens\":12,\"output_tokens\":8,\"input_tokens_details\":{\"cached_tokens\":4}}}}\n\n";
        var calls = 0;
        await using var fixture = await Fixture.CreateAsync(async request =>
        {
            calls++;
            Assert.Equal("https://upstream.example/v1/responses", request.RequestUri!.AbsoluteUri);
            Assert.Equal("upstream-secret", request.Headers.Authorization?.Parameter);
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.False(request.Headers.Contains(ModelServiceProtocol.ContextHeader));
            Assert.Equal("zstd", request.Content!.Headers.ContentEncoding.Single());
            Assert.Equal(bytes, await request.Content.ReadAsByteArrayAsync());
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(stream, Encoding.UTF8, "text/event-stream") };
            response.Headers.Add("x-request-id", "upstream-request");
            response.Headers.Add("x-codex-turn-state", "continuation");
            return response;
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://upstream.example/v1/responses")
        {
            Content = new ByteArrayContent(bytes)
        };
        request.Headers.Authorization = new("Bearer", "sdk-placeholder");
        request.Headers.Add("Cookie", "secret-cookie");
        request.Content.Headers.ContentEncoding.Add("zstd");
        using var response = await fixture.ProviderClient.SendAsync(request);
        Assert.Equal(stream, await response.Content.ReadAsStringAsync());
        Assert.Equal("upstream-request", response.Headers.GetValues("x-request-id").Single());
        Assert.Equal("continuation", response.Headers.GetValues("x-codex-turn-state").Single());
        Assert.Equal(1, calls);
        Assert.Equal(new ProviderHttpUsage(12, 8, 4), fixture.Access.Call!.Usage);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(400)]
    public async Task UpstreamErrorsReachNativeProviderUnchanged(int status)
    {
        const string body = "{\"error\":{\"code\":\"context_length_exceeded\",\"message\":\"too long\"}}";
        await using var fixture = await Fixture.CreateAsync(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) };
            response.Headers.Add("retry-after", "7");
            return Task.FromResult(response);
        });
        using var response = await fixture.ProviderClient.PostAsync("https://upstream.example/v1/responses", new StringContent("{}"));
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(body, await response.Content.ReadAsStringAsync());
        Assert.Equal("7", response.Headers.GetValues("retry-after").Single());
    }

    [Fact]
    public async Task NativeImageGenerationAndEditsUseTheRemoteTransport()
    {
        var operations = new List<string>();
        await using var fixture = await Fixture.CreateAsync(async request =>
        {
            operations.Add(request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("image-model", body);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"created":1,"data":[{"b64_json":"AQID"}]}""",
                    Encoding.UTF8, "application/json")
            };
        });
        var provider = new OpenAIClientProvider(httpTransport: fixture.Transport);
        var runtime = new EffectiveModelRuntime("primary", "chat-model", ModelProviderProtocols.OpenAIResponses,
            "Primary", "", "https://upstream.example/v1", 30, null,
            ModelProviderCapabilities.ForProtocol(ModelProviderProtocols.OpenAIResponses), IsRemote: true);
        var images = (IProviderImageGeneration)provider;
        Assert.Equal(new byte[] { 1, 2, 3 }, await images.GenerateAsync(runtime, "image-model", "draw", default));
        var reference = new ProviderImageReference([1, 2, 3], "image/png", "reference.png");
        foreach (var references in new[] { new[] { reference }, new[] { reference, reference } })
            Assert.Equal(new byte[] { 1, 2, 3 }, await images.EditAsync(runtime, "image-model", "edit", references, default));
        Assert.Equal(["/v1/images/generations", "/v1/images/edits", "/v1/images/edits"], operations);
        Assert.Equal("image-model", fixture.Access.Call!.Context.Model);
        Assert.Equal("images/edits", fixture.Access.Call.Context.Operation);
    }

    [Fact]
    public async Task CallerRevocationAndProviderAuthorizationPreventUpstreamCalls()
    {
        await using var fixture = await Fixture.CreateAsync(_ => throw new Xunit.Sdk.XunitException("Unexpected upstream call."));
        using var other = fixture.Transport.CreateClient("forbidden", new Uri("https://upstream.example/v1"));
        var forbidden = await Assert.ThrowsAsync<ModelServiceException>(() =>
            other.PostAsync("https://upstream.example/v1/responses", new StringContent("{}")));
        Assert.Equal("provider_forbidden", forbidden.Code);
        fixture.Access.Revoked = true;
        var revoked = await Assert.ThrowsAsync<ModelServiceException>(() => fixture.Transport.GetCatalogAsync());
        Assert.Equal("client_unauthorized", revoked.Code);
    }

    [Fact]
    public async Task ServiceRejectsOperationsOutsideModelApi()
    {
        await using var fixture = await Fixture.CreateAsync(_ => throw new Xunit.Sdk.XunitException("Unexpected upstream call."));
        var error = await Assert.ThrowsAsync<ModelServiceException>(() =>
            fixture.ProviderClient.GetAsync("https://upstream.example/v1/organization/api_keys"));
        Assert.Equal("operation_not_supported", error.Code);
    }

    [Fact]
    public async Task ObserverFailureDoesNotChangeResponse()
    {
        await using var fixture = await Fixture.CreateAsync(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"usage\":{\"prompt_tokens\":4,\"completion_tokens\":2}}")
        }));
        fixture.Access.ThrowOnUsage = true;
        using var response = await fixture.ProviderClient.PostAsync("https://upstream.example/v1/chat/completions", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("prompt_tokens", await response.Content.ReadAsStringAsync());
    }

    private sealed class Fixture(WebApplication app, HttpClient client, Access access, RemoteProviderTransport transport) : IAsyncDisposable
    {
        public Access Access => access;
        public RemoteProviderTransport Transport => transport;
        public HttpClient ProviderClient { get; } = transport.CreateClient("primary", new Uri("https://upstream.example/v1"));
        public static async Task<Fixture> CreateAsync(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            var access = new Access();
            builder.Services.AddSingleton<IModelServiceAccess>(access);
            builder.Services.AddSingleton<IModelServiceObserver>(access);
            builder.Services.AddDotCraftModelService();
            builder.Services.AddHttpClient("DotCraft.ModelService.Upstream").ConfigurePrimaryHttpMessageHandler(() => new Handler(send));
            var app = builder.Build();
            app.MapDotCraftModelService();
            await app.StartAsync();
            var client = app.GetTestClient();
            var transport = new RemoteProviderTransport(new ModelServiceConnection(new Uri("http://localhost/model-service/"), "caller-secret"), client);
            return new Fixture(app, client, access, transport);
        }
        public async ValueTask DisposeAsync()
        {
            transport.Dispose();
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    private sealed class Access : IModelServiceAccess, IModelServiceObserver
    {
        public bool Revoked { get; set; }
        public bool ThrowOnUsage { get; set; }
        public ModelServiceCall? Call { get; private set; }
        public ValueTask<ModelServiceCaller?> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(!Revoked && context.Request.Headers.Authorization == "Bearer caller-secret" ? new ModelServiceCaller("stack-a") : null);
        public Task<IReadOnlyList<ModelServiceProvider>> GetProvidersAsync(ModelServiceCaller caller, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelServiceProvider>>([
                new(new RemoteModelProvider("primary", "Primary", ModelProviderProtocols.OpenAIResponses,
                    "https://upstream.example/v1", "apiKey", true, new ProviderAuthenticationStatus(true)),
                    new EffectiveModelRuntime("primary", "", ModelProviderProtocols.OpenAIResponses, "Primary", "upstream-secret",
                        "https://upstream.example/v1", 30, null, ModelProviderCapabilities.ForProtocol(ModelProviderProtocols.OpenAIResponses)))
            ]);
        public ValueTask<IDisposable?> BeginRequestAsync(ModelServiceCaller caller, ModelServiceRequestContext request, CancellationToken cancellationToken) => ValueTask.FromResult<IDisposable?>(null);
        public ValueTask OnCompletedAsync(ModelServiceCall call, CancellationToken cancellationToken)
        {
            Call = call;
            if (ThrowOnUsage)
                throw new IOException("Usage store unavailable.");
            return ValueTask.CompletedTask;
        }
    }
}
