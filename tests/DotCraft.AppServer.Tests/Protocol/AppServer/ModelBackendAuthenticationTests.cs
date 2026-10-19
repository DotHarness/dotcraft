using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Contributions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class ModelBackendAuthenticationTests
{
    [Fact]
    public async Task InteractiveLoginTraversesRpcAndDisconnectCancelsPendingPrompt()
    {
        var root = Path.Combine(Path.GetTempPath(), "backend-rpc-" + Guid.NewGuid().ToString("N"));
        var contributions = new ContributionRegistry();
        var source = new Source();
        contributions.Add<IModelBackendSource>(source);
        using var resolver = new ModelBackendResolver(new ModelProviderRegistry([]), contributions, root);
        await using var authentication = new ProviderAuthenticationCoordinator(resolver);
        var config = new AppConfig
        {
            ProviderId = "demo",
            Providers = new() { ["demo"] = new() { BackendId = "rpc-demo" } }
        };
        using var harness = new AppServerTestHarness(appConfigMonitor: new AppConfigMonitor(config),
            modelRuntimeResolver: resolver, providerAuthentication: authentication);
        await harness.InitializeAsync();
        var list = await Request(harness, "provider/backend/list", new { });
        Assert.Equal("rpc-demo", list.GetProperty("backends")[0].GetProperty("id").GetString());
        var login = await Request(harness, "provider/auth/login", new { providerId = "demo", method = "manual" });
        var id = login.GetProperty("loginId").GetString()!;
        var waiting = await WaitState(harness, id, "waiting");
        Assert.Equal("secret", waiting.GetProperty("prompt").GetProperty("kind").GetString());
        await Request(harness, "provider/auth/input", new
        {
            loginId = id, promptId = waiting.GetProperty("promptId").GetString(), value = "test-secret"
        });
        var completed = await WaitState(harness, id, "completed");
        Assert.DoesNotContain("test-secret", completed.GetRawText());
        Assert.True((await Request(harness, "provider/auth/status", new { providerId = "demo" }))
            .GetProperty("isAuthenticated").GetBoolean());
        await Request(harness, "provider/auth/logout", new { providerId = "demo" });
        Assert.False((await Request(harness, "provider/auth/status", new { providerId = "demo" }))
            .GetProperty("isAuthenticated").GetBoolean());
        login = await Request(harness, "provider/auth/login", new { providerId = "demo" });
        id = login.GetProperty("loginId").GetString()!;
        await WaitState(harness, id, "waiting");
        harness.Connection.MarkClosed();
        await source.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private static async Task<JsonElement> WaitState(AppServerTestHarness harness, string id, string status)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var state = await Request(harness, "provider/auth/state", new { loginId = id });
            if (state.GetProperty("status").GetString() == status) return state;
            await Task.Delay(10);
        }
        throw new TimeoutException(status);
    }

    private static async Task<JsonElement> Request(AppServerTestHarness harness, string method, object parameters)
    {
        var request = harness.BuildRequest(method, parameters);
        await harness.ExecuteRequestAsync(request);
        while (true)
        {
            using var message = await harness.Transport.ReadNextSentAsync(TimeSpan.FromSeconds(5));
            if (!message.RootElement.TryGetProperty("id", out _)) continue;
            Assert.False(message.RootElement.TryGetProperty("error", out _), message.RootElement.GetRawText());
            return message.RootElement.GetProperty("result").Clone();
        }
    }

    private sealed class Source : IModelBackendSource, IModelBackend, IModelProvider, IProviderAuthenticator
    {
        private IProviderCredentialStore? _credentials;
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IModelBackend Backend => this;
        public ModelBackendDescriptor Descriptor => new("rpc-demo", "RPC demo", "test", ["test"],
            [new("manual", "Manual", "secret")], new ModelProviderCapabilities());
        public IReadOnlyCollection<string> Protocols => ["test"];
        public IModelProvider CreateProvider(ModelBackendContext context) { _credentials = context.Credentials; return this; }
        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => throw new NotSupportedException();
        public object? GetService(Type type, object? key = null) => type.IsInstanceOfType(this) ? this : null;
        public async Task<ProviderAuthenticationStatus> GetStatusAsync(CancellationToken ct) =>
            new(await _credentials!.ReadAsync(ct) is not null);
        public async Task<ProviderAuthenticationStatus> LoginAsync(string method, IProviderLoginInteraction interaction, CancellationToken ct)
        {
            try
            {
                var value = await interaction.PromptAsync(new("secret", "Secret"), ct);
                await _credentials!.ModifyAsync((_, _) => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(value)), ct);
                return new(true);
            }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
        }
        public Task LogoutAsync(CancellationToken ct) => _credentials!.DeleteAsync(ct);
    }
}
