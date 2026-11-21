using System.Runtime.CompilerServices;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Runtime;
using Microsoft.Extensions.AI;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotNetExtensionTestBundle;

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
    }

    [Fact]
    public async Task HostRetriesATransientBackendStreamFailure()
    {
        using var harness = new PluginGenerationHarness();
        WritePlugin(harness.PluginRoot("backend-test"), "backend-test", "BackendTest.Plugin", Source);
        await harness.ActivateAsync("backend-test");
        using var resolver = new ModelBackendResolver(new ModelProviderRegistry([]), harness.Registry, harness.Root);
        var clients = new ChatClientRegistry(new ModelProviderRegistry([]), resolver);
        var config = new AppConfig { Providers = new() { ["demo"] = new() { BackendId = "test.backend" } } };
        var client = clients.GetChatClient(config, "demo", "model");

        var text = "";
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "flaky")]))
            text += update.Text;

        Assert.Equal("recovered", text);
    }

    [Fact]
    public async Task CollectibleBackendRuntimeResolverProjectsMetadataRejectsPluginTypesAndUnloads()
    {
        using var harness = new PluginGenerationHarness();
        WritePlugin(harness.PluginRoot("backend-test"), "backend-test", "BackendTest.Plugin", Source);
        var loadContext = await ExerciseRuntimeResolverAndCleanupAsync(harness);
        Assert.True(await WaitForCollectionAsync(loadContext));
    }

    [Fact]
    public async Task CollectibleBackendControlledLoginFailureSurfacesErrorAndUnloads()
    {
        using var harness = new PluginGenerationHarness();
        WritePlugin(harness.PluginRoot("backend-test"), "backend-test", "BackendTest.Plugin", Source);
        var loadContext = await ExerciseControlledLoginFailureAndCleanupAsync(harness);
        Assert.True(await WaitForCollectionAsync(loadContext));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> ExerciseRuntimeResolverAndCleanupAsync(PluginGenerationHarness harness)
    {
        var attempt = await harness.ActivateAsync("backend-test");
        var generation = Assert.IsType<PluginGeneration>(attempt.Generation);
        using var resolver = new ModelBackendResolver(new ModelProviderRegistry([]), harness.Registry, harness.Root);
        var config = new AppConfig { Providers = new() { ["demo"] = new() { BackendId = "test.backend" } } };
        var baseRuntime = ModelProviderResolver.ResolveProvider(config, "demo");

        var resolved = resolver.Normalize(baseRuntime with { Model = "model" });
        Assert.Equal(500_000, resolved.ModelMetadata?.ContextWindow);
        Assert.Equal(16_384, resolved.MaxOutputTokens);
        Assert.Equal(
            [ProviderReasoningEffort.Low, ProviderReasoningEffort.High],
            resolved.ModelMetadata?.Reasoning?.SupportedEfforts);
        Assert.True(resolved.Capabilities.ExtendedThinking);

        Assert.Throws<PluginContributionException>(() => resolver.Normalize(baseRuntime with { Model = "unsafe" }));

        var remnant = await generation.BeginCleanup();
        return Assert.IsType<WeakReference>(remnant.LoadContext);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> ExerciseControlledLoginFailureAndCleanupAsync(PluginGenerationHarness harness)
    {
        var attempt = await harness.ActivateAsync("backend-test");
        var generation = Assert.IsType<PluginGeneration>(attempt.Generation);
        using var resolver = new ModelBackendResolver(new ModelProviderRegistry([]), harness.Registry, harness.Root);
        var config = new AppConfig { Providers = new() { ["demo"] = new() { BackendId = "test.backend" } } };
        await using var authentication = new ProviderAuthenticationCoordinator(resolver);

        var operation = authentication.Login(config, "demo", "fail", "owner");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        ProviderLoginState state;
        while (true)
        {
            state = authentication.Get(operation.Id, "owner");
            if (state.Status == "failed")
                break;
            await Task.Delay(10, timeout.Token);
        }
        Assert.Equal("AccountNotProvisioned", state.ErrorCode);
        Assert.Equal("Enable access on your account first.", state.ErrorMessage);

        var remnant = await generation.BeginCleanup();
        return Assert.IsType<WeakReference>(remnant.LoadContext);
    }

    private static async Task<bool> WaitForCollectionAsync(WeakReference loadContext)
    {
        for (var attempt = 0; attempt < 40 && loadContext.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (!loadContext.IsAlive)
                return true;
            await Task.Delay(25);
        }
        return !loadContext.IsAlive;
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
            IProviderModelRuntimeResolver, IProviderAuthenticator
        {
            public IModelBackend Backend => this;
            public ModelBackendDescriptor Descriptor => new("test.backend", "Test", "backend-test", new[] { "backend-test" },
                new[]
                {
                    new ProviderAuthenticationMethod("local", "Local", "delegated"),
                    new ProviderAuthenticationMethod("fail", "Fail", "oauth")
                }, new ModelProviderCapabilities());
            public IModelProvider CreateProvider(ModelBackendContext context) => this;
            public IReadOnlyCollection<string> Protocols => new[] { "backend-test" };
            public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => new Client();
            public object? GetService(Type type, object? key = null) => type.IsInstanceOfType(this) ? this : null;
            public EffectiveModelRuntime ResolveRuntime(EffectiveModelRuntime runtime)
            {
                if (runtime.Model == "unsafe")
                {
                    return runtime with
                    {
                        ModelMetadata = new ModelCatalogEntry
                        {
                            Id = "unsafe",
                            Reasoning = new ModelReasoningSupport(
                                true,
                                new UnsafeList(),
                                ProviderReasoningEffort.High,
                                true)
                        }
                    };
                }
                if (runtime.Model == "model")
                {
                    return runtime with
                    {
                        IsBuiltInBackend = true,
                        MaxOutputTokens = 16_384,
                        Capabilities = new ModelProviderCapabilities { ExtendedThinking = true },
                        ModelMetadata = new ModelCatalogEntry
                        {
                            Id = "model",
                            ContextWindow = 500_000,
                            MaxOutputTokens = 16_384,
                            Reasoning = new ModelReasoningSupport(
                                true,
                                new[] { ProviderReasoningEffort.Low, ProviderReasoningEffort.High },
                                ProviderReasoningEffort.High,
                                true)
                        }
                    };
                }
                return runtime;
            }
            public Task<ModelCatalogResult> FetchModelsAsync(EffectiveModelRuntime runtime, CancellationToken ct) =>
                Task.FromResult(new ModelCatalogResult { Success = true, Models = new() { new() { Id = "model" } } });
            public Task<ProviderAuthenticationStatus> GetStatusAsync(CancellationToken ct) => Task.FromResult(new ProviderAuthenticationStatus(false));
            public async Task<ProviderAuthenticationStatus> LoginAsync(string method, IProviderLoginInteraction interaction, CancellationToken ct)
            {
                if (method == "fail")
                    throw new ProviderAuthenticationException("AccountNotProvisioned", "Enable access on your account first.");
                await interaction.PromptAsync(new ProviderLoginPrompt("manual_code", "Enter a code"), ct);
                return new ProviderAuthenticationStatus(true);
            }
            public Task LogoutAsync(CancellationToken ct) => Task.CompletedTask;
            private sealed class UnsafeList : List<ProviderReasoningEffort>;
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
                if (System.Linq.Enumerable.LastOrDefault(messages)?.Text == "flaky")
                {
                    if (Interlocked.Increment(ref _flakyAttempts) == 1)
                        throw new System.IO.IOException("connection reset");
                    yield return new ChatResponseUpdate(ChatRole.Assistant, "recovered");
                    yield break;
                }
                yield return new ChatResponseUpdate(ChatRole.Assistant, "ready");
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            public object? GetService(Type type, object? key = null) => null;
            public void Dispose() { }
            private static int _flakyAttempts;
            private sealed class PrivateValue { }
        }
        """;
}
