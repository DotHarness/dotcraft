using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Runtime;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotNetPluginTestBundle;

namespace DotCraft.Tests.Runtime.Plugins;

public sealed class PluginModelBackendTests
{
    [Fact]
    public async Task CollectibleBackendGatesClientsCatalogAndLoginDuringRevocation()
    {
        using var harness = new PluginGenerationHarness();
        WritePlugin(harness.PluginRoot("backend-test"), "backend-test", "BackendTest.Plugin", Source);
        var attempt = await harness.ActivateAsync("backend-test");
        var generation = Assert.IsType<PluginGeneration>(attempt.Generation);
        using var resolver = new ModelBackendResolver(new ModelProviderRegistry([]), harness.Registry, harness.Root);
        var clients = new ChatClientRegistry(new ModelProviderRegistry([]), resolver);
        var config = new AppConfig { Providers = new() { ["demo"] = new() { BackendId = "test.backend" } } };
        var client = clients.GetChatClient(config, "demo", "model");
        Assert.Equal("plugin", (await client.GetResponseAsync([])).Text);
        Assert.Equal("model", Assert.Single((await ModelProviderCatalog.FetchAsync(config, resolver, "demo")).Models).Id);
        await Assert.ThrowsAsync<PluginContributionException>(() => client.GetResponseAsync([], new ChatOptions { ModelId = "raw" }));
        var runtime = clients.ResolveMainRuntime(config, "demo", "model");
        var historyFactory = clients.GetProviderService<IProviderHistorySessionFactory>(runtime)!;
        var identity = new ProviderConversationIdentity("thread", "thread", null, null, "turn", "window", ProviderRequestKind.Turn, 0, "test", null);
        var history = historyFactory.CreateSession(identity, new OpaqueProviderHistorySnapshot(
            new ProviderHistoryIdentity("demo", runtime.Protocol, 1, "thread", "turn", "window", "window"), [], null), [], null);
        var bridge = Assert.IsAssignableFrom<IProviderCompactionBridge>(history);
        var compactor = clients.GetProviderService<IProviderNativeCompactorFactory>(runtime)!.CreateCompactor(runtime);
        var compacted = await compactor.CompactAsync(new ProviderNativeCompactionInput(
            [new("input", JsonSerializer.SerializeToElement(new { text = "opaque" }))], 1, "turn"), [], null, default);
        await bridge.ReplaceAsync(compacted, default);
        Assert.Single(history.CaptureOpaqueSnapshot().Items);

        await using var authentication = new ProviderAuthenticationCoordinator(resolver);
        var waiting = new TaskCompletionSource<ProviderLoginState>(TaskCreationOptions.RunContinuationsAsynchronously);
        authentication.Changed += (_, state) => { if (state.Status == "waiting") waiting.TrySetResult(state); };
        var operation = authentication.Login(config, "demo", "local", "owner");
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using var stream = client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "wait")]).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        var blocked = stream.MoveNextAsync().AsTask();
        var cleanup = generation.BeginCleanup();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocked);
        await stream.DisposeAsync();
        await cleanup.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("cancelled", authentication.Get(operation.Id, "owner").Status);
        await Assert.ThrowsAsync<PluginContributionUnavailableException>(() => client.GetResponseAsync([]));
        Assert.Throws<ModelBackendUnavailableException>(() => clients.GetChatClient(config, "demo", "model"));
        Assert.Throws<PluginContributionUnavailableException>(() => history.CaptureOpaqueSnapshot());
        await Assert.ThrowsAsync<PluginContributionUnavailableException>(() =>
            compactor.CompactAsync(new ProviderNativeCompactionInput([], 0, null), [], null, default));
    }

    private const string Source = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        using DotCraft.Agents;
        using DotCraft.Configuration;
        using DotCraft.Contributions;
        using DotCraft.Plugins;
        using Microsoft.Extensions.AI;
        namespace BackendTest;
        public sealed class Plugin : IDotCraftPlugin
        {
            public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken ct)
            {
                context.Contributions.Add<IModelBackendSource>(new TestBackend());
                return ValueTask.CompletedTask;
            }
        }
        internal sealed class TestBackend : IModelBackendSource, IModelBackend, IModelProvider, IModelCatalogProvider,
            IProviderAuthenticator, IProviderHistorySessionFactory, IProviderNativeCompactorFactory, IProviderNativeCompactor
        {
            public IModelBackend Backend => this;
            public ModelBackendDescriptor Descriptor => new("test.backend", "Test", "backend-test", new[] { "backend-test" },
                new[] { new ProviderAuthenticationMethod("local", "Local", "delegated") }, new ModelProviderCapabilities());
            public IModelProvider CreateProvider(ModelBackendContext context) => this;
            public IReadOnlyCollection<string> Protocols => new[] { "backend-test" };
            public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => new Client();
            public object? GetService(Type type, object? key = null) => type.IsInstanceOfType(this) ? this : null;
            public Task<ModelCatalogResult> FetchModelsAsync(EffectiveModelRuntime runtime, CancellationToken ct) =>
                Task.FromResult(new ModelCatalogResult { Success = true, Models = new() { new() { Id = "model" } } });
            public Task<ProviderAuthenticationStatus> GetStatusAsync(CancellationToken ct) => Task.FromResult(new ProviderAuthenticationStatus(false));
            public async Task<ProviderAuthenticationStatus> LoginAsync(string method, IProviderLoginInteraction interaction, CancellationToken ct)
            {
                await interaction.PromptAsync(new ProviderLoginPrompt("manual_code", "Enter a code"), ct);
                return new ProviderAuthenticationStatus(true);
            }
            public Task LogoutAsync(CancellationToken ct) => Task.CompletedTask;
            public IProviderConversationHistory CreateSession(ProviderConversationIdentity identity, OpaqueProviderHistorySnapshot snapshot,
                IReadOnlyList<ChatMessage> messages, IProviderHistorySink? sink) => new History(snapshot);
            public IProviderNativeCompactor CreateCompactor(EffectiveModelRuntime runtime, IChatClient? client = null) => this;
            public Task<ProviderNativeCompactionReplacement> CompactAsync(ProviderNativeCompactionInput input,
                IReadOnlyList<ChatMessage> history, ChatOptions? options, CancellationToken ct) =>
                Task.FromResult(new ProviderNativeCompactionReplacement("backend-test", input.Items, input.CoveredMessageCount, input.CoveredThroughTurnId, 10));
        }
        internal sealed class History(OpaqueProviderHistorySnapshot snapshot) : IProviderConversationHistory, IProviderCompactionBridge
        {
            private OpaqueProviderHistorySnapshot _snapshot = snapshot;
            public ValueTask AppendLocalInputAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct) => ValueTask.CompletedTask;
            public ValueTask HistoryReplacedAsync(IReadOnlyList<ChatMessage> messages, ChatOptions? options, string reason,
                CancellationToken ct, string? turn = null) => ValueTask.CompletedTask;
            public void MarkProjectionCovered(IReadOnlyList<ChatMessage> messages) { }
            public string? BeginAttempt() => null;
            public ValueTask AbortAttemptAsync(string? id, CancellationToken ct) => ValueTask.CompletedTask;
            public void EndAttempt(string? id) { }
            public OpaqueProviderHistorySnapshot CaptureOpaqueSnapshot() => _snapshot;
            public ValueTask<ProviderNativeCompactionInput> CaptureInputAsync(ProviderCompactionPhase phase,
                IReadOnlyList<ChatMessage> messages, ChatOptions? options, CancellationToken ct) =>
                ValueTask.FromResult(new ProviderNativeCompactionInput(_snapshot.Items, messages.Count, null));
            public ValueTask ReplaceAsync(ProviderNativeCompactionReplacement replacement, CancellationToken ct)
            {
                _snapshot = _snapshot with { Items = replacement.Items };
                return ValueTask.CompletedTask;
            }
            public long EstimateContextTokens(ProviderNativeCompactionInput input, IReadOnlyList<ChatMessage> tail, ChatOptions? options) => 10;
        }
        internal sealed class Client : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            {
                var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "plugin"));
                if (options?.ModelId == "raw") response.RawRepresentation = new PrivateValue();
                return Task.FromResult(response);
            }
            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "ready");
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            public object? GetService(Type type, object? key = null) => null;
            public void Dispose() { }
            private sealed class PrivateValue { }
        }
        """;
}
