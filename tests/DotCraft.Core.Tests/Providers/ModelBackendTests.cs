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
    public async Task RevokedBackendCannotPublishAStaleCatalog()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<ModelCatalogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new Backend("alpha") { CatalogFactory = () => { started.SetResult(); return pending.Task; } };
        var handle = _contributions.Add<IModelBackendSource>(new Source(backend));
        var resolver = Resolver();
        var fetch = ModelProviderCatalog.FetchAsync(Config(("a", "alpha")), resolver, "a");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        handle.Dispose();
        pending.SetResult(new ModelCatalogResult { Success = true, Models = [new() { Id = "stale" }] });
        Assert.False((await fetch).Success);
        Assert.Empty((await fetch).Models);
    }

    [Fact]
    public async Task CancelledRefreshCannotRestoreCredentialsAfterLogout()
    {
        var store = new ProviderCredentialStore(_root, "alpha", "a");
        var other = new ProviderCredentialStore(_root, "alpha", "a");
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = store.ModifyAsync(async (_, _) =>
        {
            started.SetResult();
            await release.Task;
            return JsonSerializer.SerializeToElement("rotated-secret");
        }, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var logout = other.DeleteAsync(default);
        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        await logout;
        Assert.Null(await store.ReadAsync(default));
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
    public async Task SamplingSeesTheActiveConversationIdentity()
    {
        var backend = new Backend("alpha");
        _contributions.Add<IModelBackendSource>(new Source(backend));
        var client = new ChatClientRegistry(new ModelProviderRegistry([]), Resolver()).GetChatClient(Config(("a", "alpha")), "a", "model");
        var identity = new ProviderConversationIdentity(
            "thread", "thread", null, null, "turn", "window", ProviderRequestKind.Compaction, 0, "test", null);
        using var scope = ProviderRequestContextScope.Push(new ProviderRequestContext(identity));
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);
        Assert.Equal(identity, backend.LastIdentity);
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

    public void Dispose()
    {
        _resolver?.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed record Source(IModelBackend Backend) : IModelBackendSource;
    private sealed class Backend(string id) : IModelBackend
    {
        public Func<IChatClient>? ClientFactory { get; init; }
        public Func<Task<ModelCatalogResult>>? CatalogFactory { get; init; }
        public ProviderConversationIdentity? LastIdentity { get; set; }
        public ModelBackendDescriptor Descriptor => new(id, id, "anthropic", ["anthropic"],
            [new("oauth", "Sign in", "oauth")], new ModelProviderCapabilities());
        public IModelProvider CreateProvider(ModelBackendContext context) => new Provider(this, id, context.Credentials);
    }

    private sealed class Provider(Backend backend, string id, IProviderCredentialStore credentials)
        : IModelProvider, IModelCatalogProvider, IProviderAuthenticator
    {
        public IReadOnlyCollection<string> Protocols => ["anthropic"];
        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => backend.ClientFactory?.Invoke() ?? new Client(backend, id);
        public object? GetService(Type type, object? key = null) => type.IsInstanceOfType(this) ? this : null;
        public Task<ModelCatalogResult> FetchModelsAsync(EffectiveModelRuntime runtime, CancellationToken ct) =>
            backend.CatalogFactory?.Invoke() ?? Task.FromResult(new ModelCatalogResult { Success = true, Models = [new() { Id = id }] });
        public async Task<ProviderAuthenticationStatus> GetStatusAsync(CancellationToken ct) =>
            new(await credentials.ReadAsync(ct) is not null);
        public async Task<ProviderAuthenticationStatus> LoginAsync(string method, IProviderLoginInteraction interaction, CancellationToken ct)
        {
            var secret = await interaction.PromptAsync(new("secret", "Enter a test secret"), ct);
            await credentials.ModifyAsync((_, _) => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(secret)), ct);
            return new ProviderAuthenticationStatus(true);
        }
        public Task LogoutAsync(CancellationToken ct) => credentials.DeleteAsync(ct);
    }

    private sealed class Client(Backend backend, string text) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            backend.LastIdentity = ProviderRequestContextScope.Current?.CurrentIdentity;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        }
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
