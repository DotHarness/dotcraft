using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using DotCraft.Agents;
using DotCraft.Agents.Remote;
using DotCraft.Configuration;
using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Runtime;
using DotCraft.Sessions;
using DotCraft.Tests.Runtime.Plugins;
using DotCraft.Tools;
using DotCraft.Workspaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DotCraft.ModelService.Tests;

public sealed class ModelServicePluginTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory().FullName;
    private const string PluginId = HttpBackendPluginFixture.Id;
    private const string WorkerToken = "worker-fixture";

    [Fact]
    public async Task EmbeddedHostGatesPluginAuthenticationUsageAndStreamingByScopeAndTrust()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        var pluginRoot = Path.Combine(_root, "installed", PluginId);
        Directory.CreateDirectory(pluginRoot);
        File.WriteAllText(Path.Combine(pluginRoot, "settings.schema.json"), HttpBackendPluginFixture.Settings);
        DotNetExtensionTestBundle.WritePluginBundle(pluginRoot, PluginId, "HttpFixture.Plugin", HttpBackendPluginFixture.Source,
            settings: "./settings.schema.json");

        var first = Setup("first", "first-model");
        var second = Setup("second", "second-model");
        using var upstream = new BlockingUpstream("first-secret");
        await using var service = Build(first, [PluginId], upstream);
        await service.StartAsync(ct);
        Assert.Null(service.Services.GetService<ISessionService>());
        var runtime = service.Services.GetRequiredService<IDotNetExtensionRuntimeCoordinator>();
        var resolver = service.Services.GetRequiredService<ModelBackendResolver>();
        var auth = service.Services.GetRequiredService<ProviderAuthenticationCoordinator>();
        using var client = service.GetTestClient();
        using var remote = new RemoteProviderTransport(new(new Uri("http://localhost/model-service/"), WorkerToken), client);

        Assert.Empty((await remote.GetCatalogAsync(ct)).Providers);
        Assert.NotEqual(PluginRuntimeMutationOutcome.NotApplied, (await runtime.TrustAsync(PluginId, ct)).Outcome);
        Assert.Equal(PluginId, Assert.Single(resolver.GetBackendRegistrations()).PluginId);
        Assert.Empty(service.Services.GetRequiredService<IContributionView>().Resolve<IToolSource>());
        var beforeLogin = Assert.Single((await remote.GetCatalogAsync(ct)).Providers);
        Assert.False(beforeLogin.Authentication.IsAuthenticated);
        Assert.Contains("first-model", beforeLogin.Models!.Keys);
        using (var unauthenticated = await SendAsync(client, "first-model", ct))
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await LoginAsync(auth, first.Config, "first-secret", ct);
        Assert.Equal(12, (await auth.ReadUsageAsync(first.Config, "gateway", ct))!.Values["balance"]);
        var nativeUsage = await auth.ReadUsageAsync(first.Config, "native", ct);
        Assert.Equal(17, nativeUsage!.Values["balance"]);
        Assert.Equal(PluginId, nativeUsage.PluginId);
        Assert.True(Assert.Single((await remote.GetCatalogAsync(ct)).Providers).Authentication.IsAuthenticated);

        await using (var isolated = Build(second, [PluginId], upstream))
        {
            await isolated.StartAsync(ct);
            var otherRuntime = isolated.Services.GetRequiredService<IDotNetExtensionRuntimeCoordinator>();
            var otherResolver = isolated.Services.GetRequiredService<ModelBackendResolver>();
            Assert.Empty(otherResolver.GetBackendRegistrations());
            Assert.NotEqual(PluginRuntimeMutationOutcome.NotApplied, (await otherRuntime.TrustAsync(PluginId, ct)).Outcome);
            var otherAuth = isolated.Services.GetRequiredService<ProviderAuthenticationCoordinator>();
            Assert.False((await otherAuth.GetStatusAsync(second.Config, "gateway", ct)).IsAuthenticated);
            using var otherClient = isolated.GetTestClient();
            using var otherRemote = new RemoteProviderTransport(new(new Uri("http://localhost/model-service/"), WorkerToken), otherClient);
            Assert.Contains("second-model", Assert.Single((await otherRemote.GetCatalogAsync(ct)).Providers).Models!.Keys);
            await LoginAsync(otherAuth, second.Config, "other", ct);
            Assert.Equal(5, (await otherAuth.ReadUsageAsync(second.Config, "gateway", ct))!.Values["balance"]);
            Assert.Equal(12, (await auth.ReadUsageAsync(first.Config, "gateway", ct))!.Values["balance"]);
            await otherAuth.LogoutAsync(second.Config, "gateway", ct);
            Assert.False((await otherAuth.GetStatusAsync(second.Config, "gateway", ct)).IsAuthenticated);
            await isolated.StopAsync(ct);
        }

        await using (var notAllowed = Build(second, [], upstream))
        {
            await notAllowed.StartAsync(ct);
            Assert.Empty(notAllowed.Services.GetRequiredService<ModelBackendResolver>().GetBackendRegistrations());
            await notAllowed.StopAsync(ct);
        }

        using var response = await SendAsync(client, "first-model", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await upstream.StreamWaiting.Task.WaitAsync(ct);
        Assert.NotEqual(PluginRuntimeMutationOutcome.NotApplied, (await runtime.RevokeTrustAsync(PluginId, ct)).Outcome);
        await upstream.StreamCancelled.Task.WaitAsync(ct);
        Assert.Empty((await remote.GetCatalogAsync(ct)).Providers);
        await service.StopAsync(ct);
    }

    private (AppConfig Config, DotCraftPaths Paths) Setup(string name, string model)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, PluginConfigStore.FileName), JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [PluginId] = new { model }
        }));
        return (new AppConfig
        {
            GlobalConfigPath = Path.Combine(path, "config.json"),
            Providers = new()
            {
                ["gateway"] = new() { BackendId = PluginId },
                ["native"] = new() { EndPoint = "https://native.invalid/v1", ApiKey = "native-fixture-key" }
            }
        }, DotCraftPaths.Create(path, path));
    }

    private WebApplication Build((AppConfig Config, DotCraftPaths Paths) scope, string[] allowed, HttpMessageHandler upstream)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddDotCraftModelPluginRuntime(scope.Config, scope.Paths, [Path.Combine(_root, "installed")], allowed,
            createCredentialStore: (backend, provider) => new ProviderCredentialStore(
                Path.Combine(scope.Paths.Data.RootPath, "custom-vault"), backend, provider));
        builder.Services.AddSingleton<IModelServiceAccess>(services =>
            new EmbeddedAccess(scope.Config, services.GetRequiredService<ModelBackendResolver>()));
        builder.Services.AddDotCraftModelService();
        builder.Services.AddHttpClient("DotCraft.ModelService.Upstream").ConfigurePrimaryHttpMessageHandler(() => upstream);
        var service = builder.Build();
        service.MapDotCraftModelService();
        return service;
    }

    private static async Task LoginAsync(ProviderAuthenticationCoordinator auth, AppConfig config, string secret, CancellationToken ct)
    {
        var updates = Channel.CreateUnbounded<ProviderLoginState>();
        const string owner = "manager";
        void OnChanged(string changedOwner, ProviderLoginState state)
        {
            if (changedOwner == owner)
                updates.Writer.TryWrite(state);
        }

        auth.Changed += OnChanged;
        try
        {
            var session = auth.Login(config, "gateway", "vault", owner, ct);
            Assert.Throws<ProviderAuthenticationException>(() => auth.Get(session.Id, "different-manager"));
            while (true)
            {
                var state = await updates.Reader.ReadAsync(ct);
                if (state.PromptId is { } prompt)
                    auth.Respond(session.Id, prompt, secret, owner);
                if (state.Status is "completed" or "cancelled" or "failed")
                {
                    Assert.Equal("completed", state.Status);
                    break;
                }
            }
        }
        finally { auth.Changed -= OnChanged; }
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string model, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "model-service/providers/gateway/http/chat/completions")
        {
            Content = JsonContent.Create(new { model, stream = true })
        };
        request.Headers.Authorization = new("Bearer", WorkerToken);
        request.Headers.Add(ModelServiceProtocol.ContextHeader,
            Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new ModelServiceRequestContext(model, null),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private sealed class EmbeddedAccess(AppConfig config, ModelBackendResolver resolver) : IModelServiceAccess
    {
        public ValueTask<ModelServiceCaller?> AuthenticateAsync(HttpContext context, CancellationToken ct) =>
            ValueTask.FromResult(context.Request.Headers.Authorization == "Bearer " + WorkerToken
                ? new ModelServiceCaller("worker") : null);

        public Task<IReadOnlyList<ModelServiceProvider>> GetProvidersAsync(ModelServiceCaller caller, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ModelServiceProvider>>(
                ModelServiceProvider.ResolveBackend(ModelProviderResolver.ResolveProvider(config, "gateway"), resolver) is { } provider
                    ? [provider] : []);

        public ValueTask<IDisposable?> BeginRequestAsync(ModelServiceCaller caller, ModelServiceRequestContext request, CancellationToken ct) =>
            ValueTask.FromResult<IDisposable?>(null);
    }

    private sealed class BlockingUpstream(string secret) : HttpMessageHandler
    {
        public TaskCompletionSource StreamWaiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StreamCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/native/v1/chat/completions", request.RequestUri!.AbsolutePath);
            Assert.Equal(secret, Assert.Single(request.Headers.GetValues("x-fixture-key")));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new BlockingStream(StreamWaiting, StreamCancelled))
            };
            response.Content.Headers.ContentType = new("text/event-stream");
            return Task.FromResult(response);
        }
    }

    private sealed class BlockingStream(TaskCompletionSource waiting, TaskCompletionSource cancelled) : MemoryStream
    {
        private bool _started;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_started)
            {
                _started = true;
                var chunk = "data: {}\n\n"u8.ToArray();
                chunk.CopyTo(buffer);
                return chunk.Length;
            }
            waiting.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            finally { cancelled.TrySetResult(); }
            return 0;
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
