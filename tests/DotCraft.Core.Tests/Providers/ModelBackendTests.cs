using System.Runtime.CompilerServices;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Contributions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Providers;

public sealed class ModelBackendTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-backend-" + Guid.NewGuid().ToString("N"));
    private readonly ContributionRegistry _contributions = new();
    private ModelBackendResolver? _resolver;

    [Fact]
    public async Task SharedProtocolRoutesClientsAndCatalogsByBackend()
    {
        _contributions.Add<IModelBackendSource>(new Source(new Backend("alpha")));
        _contributions.Add<IModelBackendSource>(new Source(new Backend("beta")));
        var resolver = Resolver();
        var clients = new ChatClientRegistry(new ModelProviderRegistry([]), resolver);
        var config = Config(("a", "alpha"), ("b", "beta"));

        var first = await clients.GetChatClient(config, "a", "model").GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);
        var second = await clients.GetChatClient(config, "b", "model").GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);
        Assert.Equal("alpha", first.Messages.Single().Text);
        Assert.Equal("beta", second.Messages.Single().Text);
        Assert.Equal("alpha", Assert.Single((await ModelProviderCatalog.FetchAsync(config, resolver, "a")).Models).Id);
        Assert.Equal("beta", Assert.Single((await ModelProviderCatalog.FetchAsync(config, resolver, "b")).Models).Id);
    }

    [Fact]
    public void DuplicateBackendRegistrationDoesNotReplaceExistingRoute()
    {
        _contributions.Add<IModelBackendSource>(new Source(new Backend("alpha")));
        Assert.Throws<InvalidOperationException>(() =>
            _contributions.Add<IModelBackendSource>(new Source(new Backend("alpha"))));
        Assert.Equal("alpha", Assert.Single(Resolver().GetBackends()).Id);
    }

    [Fact]
    public void MissingBackendNeverUsesBuiltinProtocolFallback()
    {
        var config = Config(("a", "missing"));
        Assert.Throws<ModelBackendUnavailableException>(() =>
            new ChatClientRegistry(new ModelProviderRegistry([]), Resolver()).GetChatClient(config, "a", "model"));
    }

    [Fact]
    public async Task LoginPromptsAreCorrelatedAndCredentialsAreInstanceScoped()
    {
        _contributions.Add<IModelBackendSource>(new Source(new Backend("alpha")));
        await using var auth = new ProviderAuthenticationCoordinator(Resolver());
        var config = Config(("a", "alpha"), ("b", "alpha"));
        var login = auth.Login(config, "a", "oauth", "owner");
        var waiting = await WaitAsync(auth, login.Id, "waiting");
        Assert.Equal("secret", waiting.Prompt!.Kind);
        Assert.Throws<ProviderAuthenticationException>(() => auth.Respond(login.Id, "wrong", "secret", "owner"));
        Assert.Throws<ProviderAuthenticationException>(() => auth.Get(login.Id, "another-owner"));
        auth.Respond(login.Id, waiting.PromptId!, "account-a", "owner");
        var finished = await WaitAsync(auth, login.Id, "completed");
        Assert.Null(finished.Prompt);
        Assert.DoesNotContain("account-a", JsonSerializer.Serialize(finished));
        Assert.True((await auth.GetStatusAsync(config, "a", default)).IsAuthenticated);
        Assert.False((await auth.GetStatusAsync(config, "b", default)).IsAuthenticated);
        await auth.LogoutAsync(config, "a", default);
        Assert.False((await auth.GetStatusAsync(config, "a", default)).IsAuthenticated);
    }

    [Fact]
    public async Task CancellationAndLogoutReleasePendingPrompt()
    {
        _contributions.Add<IModelBackendSource>(new Source(new Backend("alpha")));
        await using var auth = new ProviderAuthenticationCoordinator(Resolver());
        var config = Config(("a", "alpha"));
        var login = auth.Login(config, "a", null, "owner");
        await WaitAsync(auth, login.Id, "waiting");
        auth.CancelOwner("owner");
        await WaitAsync(auth, login.Id, "cancelled");
        var next = auth.Login(config, "a", null, "owner");
        await WaitAsync(auth, next.Id, "waiting");
        await auth.LogoutAsync(config, "a", default);
        Assert.Equal("cancelled", auth.Get(next.Id, "owner").Status);
        Assert.False((await auth.GetStatusAsync(config, "a", default)).IsAuthenticated);
    }

    [Fact]
    public async Task CredentialModificationSerializesAcrossStoreInstances()
    {
        var first = new ProviderCredentialStore(_root, "alpha", "a");
        var second = new ProviderCredentialStore(_root, "alpha", "a");
        async Task<JsonElement?> Increment(JsonElement? current, CancellationToken ct)
        {
            await Task.Delay(20, ct);
            return JsonSerializer.SerializeToElement((current?.GetInt32() ?? 0) + 1);
        }
        await Task.WhenAll(first.ModifyAsync(Increment, default), second.ModifyAsync(Increment, default));
        Assert.Equal(2, (await first.ReadAsync(default))!.Value.GetInt32());
        var other = new ProviderCredentialStore(_root, "alpha", "b");
        Assert.Null(await other.ReadAsync(default));
    }

    [Fact]
    public async Task HostExecutesTheToolAndReturnsItsResultToTheBackend()
    {
        var backend = new Backend("alpha") { ClientFactory = () => new ToolRoundTripClient() };
        _contributions.Add<IModelBackendSource>(new Source(backend));
        var inner = new ChatClientRegistry(new ModelProviderRegistry([]), Resolver())
            .GetChatClient(Config(("a", "alpha")), "a", "model");
        using var client = new StreamingFunctionInvokingChatClient(inner);
        var executed = 0;
        var function = AIFunctionFactory.Create(() => { executed++; return "tool-result"; }, "TestTool");
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "use the tool")], new ChatOptions { Tools = [function] }))
            updates.Add(update);
        Assert.Equal(1, executed);
        Assert.Contains(updates.SelectMany(update => update.Contents).OfType<TextContent>(), text => text.Text == "received-tool-result");
    }

    [Fact]
    public async Task PluginRuntimeResolverProjectsMetadataPreservesHostInvariantsAndCachesClients()
    {
        static ModelCatalogEntry BuildMetadata() => new()
        {
            Id = "reasoner-4",
            DisplayName = "Reasoner 4",
            ContextWindow = 1_000_000,
            MaxOutputTokens = 32_768,
            Reasoning = new ModelReasoningSupport(
                SupportsDisable: true,
                SupportedEfforts: [ProviderReasoningEffort.Low, ProviderReasoningEffort.Medium, ProviderReasoningEffort.High],
                DefaultEffort: ProviderReasoningEffort.Medium,
                DefaultEnabled: true)
        };
        var backend = new Backend("alpha")
        {
            Protocols = ["anthropic", "gemini"],
            RuntimeResolver = runtime =>
            {
                if (string.IsNullOrWhiteSpace(runtime.Model))
                    throw new ArgumentException("Model id is required.");
                return runtime.Model switch
                {
                    "reasoner-4" => runtime with
                    {
                        ProviderId = "spoofed-provider",
                        BackendId = "spoofed-backend",
                        IsBuiltInBackend = true,
                        AuthMethod = "spoofed-auth",
                        Protocol = "gemini",
                        ModelMetadata = BuildMetadata(),
                        MaxOutputTokens = 32_768,
                        Capabilities = new ModelProviderCapabilities { ExtendedThinking = true }
                    },
                    "no-limit" => runtime with { MaxOutputTokens = null },
                    "bad-protocol" => runtime with { Protocol = "undeclared" },
                    _ => runtime
                };
            }
        };
        _contributions.Add<IModelBackendSource>(new Source(backend));
        var resolver = Resolver();
        var config = Config(("a", "alpha"));
        config.RuntimeResolver = resolver;

        var providerOnly = resolver.Normalize(ModelProviderResolver.ResolveProvider(config, "a"));
        Assert.Equal("anthropic", providerOnly.Protocol);
        Assert.Equal("alpha", Assert.Single((await ModelProviderCatalog.FetchAsync(config, resolver, "a")).Models).Id);

        var raw = ModelProviderResolver.ResolveProvider(config, "a") with { Model = "reasoner-4" };
        var resolved = resolver.Normalize(raw);

        Assert.Equal("a", resolved.ProviderId);
        Assert.Equal("alpha", resolved.BackendId);
        Assert.False(resolved.IsBuiltInBackend);
        Assert.Equal("oauth", resolved.AuthMethod);
        Assert.Equal("gemini", resolved.Protocol);
        Assert.Equal(1_000_000, resolved.ModelMetadata?.ContextWindow);
        Assert.Equal(32_768, resolved.MaxOutputTokens);
        Assert.True(resolved.Capabilities.ExtendedThinking);

        var clients = new ChatClientRegistry(new ModelProviderRegistry([]), resolver);
        Assert.Same(clients.GetChatClient(config, "a", "reasoner-4"), clients.GetChatClient(config, "a", "reasoner-4"));

        config.Providers["a"].MaxOutputTokens = 4_096;
        Assert.Equal(4_096, resolver.Normalize(ModelProviderResolver.ResolveProvider(config, "a") with { Model = "reasoner-4" }).MaxOutputTokens);
        Assert.Equal(4_096, resolver.Normalize(ModelProviderResolver.ResolveProvider(config, "a") with { Model = "no-limit" }).MaxOutputTokens);

        var reasoning = ModelThinkingAdapterResolver.ResolveReasoningCapability(config, resolved);
        Assert.NotNull(reasoning);
        Assert.Equal(
            [ProviderReasoningEffort.Low, ProviderReasoningEffort.Medium, ProviderReasoningEffort.High],
            reasoning.SupportedEfforts.Select(effort => effort.Effort).ToArray());
        var compaction = ModelCatalog.ResolveCompactionConfig(
            config, resolved.Model, resolved.ModelMetadata?.ContextWindow);
        Assert.Equal(1_000_000, compaction.ContextWindow);

        Assert.Throws<ArgumentException>(() => resolver.Normalize(raw with { Model = "bad-protocol" }));
    }

    [Fact]
    public async Task LoginSurfacesControlledFailureAndRedactsUnexpectedExceptions()
    {
        Exception? failure = new ProviderAuthenticationException("AccountNotEligible", "Sign in with an eligible account.");
        var backend = new Backend("alpha")
        {
            LoginHandler = (_, _, _) => Task.FromException<ProviderAuthenticationStatus>(failure!)
        };
        _contributions.Add<IModelBackendSource>(new Source(backend));
        await using var auth = new ProviderAuthenticationCoordinator(Resolver());
        var config = Config(("a", "alpha"));

        var controlled = auth.Login(config, "a", "oauth", "owner");
        var controlledFailed = await WaitStatusAsync(auth, controlled.Id, "failed");
        Assert.Equal("AccountNotEligible", controlledFailed.ErrorCode);
        Assert.Equal("Sign in with an eligible account.", controlledFailed.ErrorMessage);

        failure = new InvalidOperationException("internal secret token=abc");
        var unexpected = auth.Login(config, "a", "oauth", "owner");
        var unexpectedFailed = await WaitStatusAsync(auth, unexpected.Id, "failed");
        Assert.Equal("ProviderLoginFailed", unexpectedFailed.ErrorCode);
        Assert.Equal("Provider sign-in failed. Try again.", unexpectedFailed.ErrorMessage);
    }

    private ModelBackendResolver Resolver() => _resolver ??= new(new ModelProviderRegistry([]), _contributions, _root);
    private AppConfig Config(params (string Id, string Backend)[] entries) => new()
    {
        GlobalConfigPath = Path.Combine(_root, "config.json"),
        Providers = entries.ToDictionary(entry => entry.Id, entry => new AppConfig.ModelProviderConfig
        { BackendId = entry.Backend, Protocol = "anthropic" }, StringComparer.OrdinalIgnoreCase)
    };

    private static async Task<ProviderLoginState> WaitAsync(ProviderAuthenticationCoordinator auth, string id, string status)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var state = auth.Get(id, "owner");
            if (state.Status == status) return state;
            Assert.NotEqual("failed", state.Status);
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task<ProviderLoginState> WaitStatusAsync(ProviderAuthenticationCoordinator auth, string id, string status)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var state = auth.Get(id, "owner");
            if (state.Status == status) return state;
            await Task.Delay(10, timeout.Token);
        }
    }

    public void Dispose()
    {
        _resolver?.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed record Source(IModelBackend Backend) : IModelBackendSource;
    private sealed class Backend(string id) : IModelBackend
    {
        public Func<IChatClient>? ClientFactory { get; init; }
        public IReadOnlyList<string> Protocols { get; init; } = ["anthropic"];
        public Func<EffectiveModelRuntime, EffectiveModelRuntime>? RuntimeResolver { get; init; }
        public Func<string, IProviderLoginInteraction, CancellationToken, Task<ProviderAuthenticationStatus>>? LoginHandler { get; init; }
        public ModelBackendDescriptor Descriptor => new(id, id, Protocols[0], Protocols,
            [new("oauth", "Sign in", "oauth")], new ModelProviderCapabilities());
        public IModelProvider CreateProvider(ModelBackendContext context) => new Provider(this, id, context.Credentials);
    }

    private sealed class Provider(Backend backend, string id, IProviderCredentialStore credentials)
        : IModelProvider, IModelCatalogProvider, IProviderAuthenticator, IProviderModelRuntimeResolver
    {
        public IReadOnlyCollection<string> Protocols => backend.Protocols;
        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => backend.ClientFactory?.Invoke() ?? new Client(id);
        public object? GetService(Type type, object? key = null) =>
            type == typeof(IProviderModelRuntimeResolver)
                ? (backend.RuntimeResolver is not null ? this : null)
                : (type.IsInstanceOfType(this) ? this : null);
        public EffectiveModelRuntime ResolveRuntime(EffectiveModelRuntime runtime) => backend.RuntimeResolver!(runtime);
        public Task<ModelCatalogResult> FetchModelsAsync(EffectiveModelRuntime runtime, CancellationToken ct) =>
            Task.FromResult(new ModelCatalogResult { Success = true, Models = [new() { Id = id }] });
        public async Task<ProviderAuthenticationStatus> GetStatusAsync(CancellationToken ct) =>
            new(await credentials.ReadAsync(ct) is not null);
        public async Task<ProviderAuthenticationStatus> LoginAsync(string method, IProviderLoginInteraction interaction, CancellationToken ct)
        {
            if (backend.LoginHandler is not null)
                return await backend.LoginHandler(method, interaction, ct);
            var secret = await interaction.PromptAsync(new("secret", "Enter a test secret"), ct);
            await credentials.ModifyAsync((_, _) => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(secret)), ct);
            return new ProviderAuthenticationStatus(true);
        }
        public Task LogoutAsync(CancellationToken ct) => credentials.DeleteAsync(ct);
    }

    private sealed class Client(string text) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, text) { FinishReason = ChatFinishReason.Stop };
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class ToolRoundTripClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (messages.Any(message => message.Contents.OfType<FunctionResultContent>().Any()))
                yield return new ChatResponseUpdate(ChatRole.Assistant, "received-tool-result") { FinishReason = ChatFinishReason.Stop };
            else
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant, FinishReason = ChatFinishReason.ToolCalls,
                    Contents = [new FunctionCallContent("call", "TestTool", new Dictionary<string, object?>())]
                };
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
