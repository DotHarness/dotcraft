using System.Collections.Concurrent;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Runtime;
using Microsoft.Extensions.Logging;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotNetExtensionTestBundle;

namespace DotCraft.Tests.Runtime.Plugins;

public sealed class RoutedProviderRetirementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CredentialChangeRetiresOnlyTheOldProviderAfterItsAdmittedCall(bool unloadWhileDraining)
    {
        using var harness = new PluginGenerationHarness();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = deadline.Token;
        var oldCallEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishOldCall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldProviderDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposals = new ConcurrentDictionary<string, int>();
        Func<string, Task> authenticate = credential =>
        {
            if (credential != "first")
                return Task.CompletedTask;
            oldCallEntered.TrySetResult();
            return finishOldCall.Task;
        };
        Action<string> disposed = credential =>
        {
            disposals.AddOrUpdate(credential, 1, (_, count) => count + 1);
            if (credential == "first")
                oldProviderDisposed.TrySetResult();
        };
        harness.Services = CreateServiceProvider((typeof(Func<string, Task>), authenticate), (typeof(Action<string>), disposed));
        WritePlugin(harness.PluginRoot("routed-fixture"), "routed-fixture", "RoutedFixture.Plugin", Source);
        var generation = Assert.IsType<PluginGeneration>((await harness.ActivateAsync("routed-fixture", cancellationToken: ct)).Generation);
        using var resolver = new ModelBackendResolver(new ModelProviderRegistry([]), harness.Registry, harness.Root);
        var config = new AppConfig
        {
            Providers = new()
            {
                ["route"] = new() { BackendId = "routed-fixture" },
                ["unrelated"] = new() { BackendId = "routed-fixture" }
            }
        };
        var runtime = ModelProviderResolver.ResolveProvider(config, "route") with { Model = "fixture-model" };
        var otherRuntime = ModelProviderResolver.ResolveProvider(config, "unrelated") with { Model = "fixture-model" };
        var credential = new ProviderCredentialStore(harness.Root, "routed-fixture", "route");
        var unrelatedCredential = new ProviderCredentialStore(harness.Root, "routed-fixture", "unrelated");
        Task<PluginGenerationRemnant>? cleanup = null;

        try
        {
            await SetCredentialAsync(credential, "first", ct);
            await SetCredentialAsync(unrelatedCredential, "other", ct);
            var previous = ResolveRouter(resolver, runtime);
            var unrelated = ResolveRouter(resolver, otherRuntime);
            var started = previous.AuthenticateAsync(runtime, ct);
            await oldCallEntered.Task.WaitAsync(ct);

            await SetCredentialAsync(credential, "second", ct);
            resolver.InvalidateAuthentication(runtime);

            var replacement = ResolveRouter(resolver, runtime);
            Assert.Equal("second", (await replacement.AuthenticateAsync(runtime, ct))["x-fixture-key"]);
            await Assert.ThrowsAsync<ModelBackendUnavailableException>(() => previous.AuthenticateAsync(runtime, ct));

            if (unloadWhileDraining)
            {
                cleanup = generation.BeginCleanup();
                Assert.False(cleanup.IsCompleted);
            }

            Assert.False(oldProviderDisposed.Task.IsCompleted);
            finishOldCall.TrySetResult();
            Assert.Equal("first", (await started.WaitAsync(ct))["x-fixture-key"]);
            await oldProviderDisposed.Task.WaitAsync(ct);
            if (!unloadWhileDraining)
            {
                Assert.Equal("second", (await replacement.AuthenticateAsync(runtime, ct))["x-fixture-key"]);
                Assert.Equal("other", (await unrelated.AuthenticateAsync(otherRuntime, ct))["x-fixture-key"]);
                Assert.False(disposals.ContainsKey("second"));
                Assert.False(disposals.ContainsKey("other"));
            }
        }
        finally
        {
            finishOldCall.TrySetResult();
            await (cleanup ?? generation.BeginCleanup()).WaitAsync(TimeSpan.FromSeconds(15));
        }

        Assert.Equal(1, disposals["first"]);
        Assert.Equal(1, disposals["second"]);
        Assert.Equal(1, disposals["other"]);
    }

    [Fact]
    public async Task RetirementLogsCleanupFailureWithoutPreventingReplacement()
    {
        using var harness = new PluginGenerationHarness();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = deadline.Token;
        var logger = new CleanupLogger();
        var disposeAttempts = 0;
        Action<string> dispose = credential =>
        {
            if (credential != "first")
                return;
            Interlocked.Increment(ref disposeAttempts);
            throw new InvalidOperationException("Fixture disposal failed.");
        };
        harness.Services = CreateServiceProvider(
            (typeof(Func<string, Task>), (Func<string, Task>)(_ => Task.CompletedTask)),
            (typeof(Action<string>), dispose),
            (typeof(ILogger<DotNetExtensionRuntimeManager>), logger));
        WritePlugin(harness.PluginRoot("routed-fixture"), "routed-fixture", "RoutedFixture.Plugin", Source);
        var generation = Assert.IsType<PluginGeneration>((await harness.ActivateAsync("routed-fixture", cancellationToken: ct)).Generation);
        using var resolver = new ModelBackendResolver(new ModelProviderRegistry([]), harness.Registry, harness.Root);
        var config = new AppConfig { Providers = new() { ["route"] = new() { BackendId = "routed-fixture" } } };
        var runtime = ModelProviderResolver.ResolveProvider(config, "route") with { Model = "fixture-model" };
        var credential = new ProviderCredentialStore(harness.Root, "routed-fixture", "route");

        try
        {
            await SetCredentialAsync(credential, "first", ct);
            _ = ResolveRouter(resolver, runtime);

            await SetCredentialAsync(credential, "second", ct);
            resolver.InvalidateAuthentication(runtime);
            var replacement = ResolveRouter(resolver, runtime);
            var warning = await logger.Warning.Task.WaitAsync(ct);
            Assert.Contains("routed-fixture", warning);
            Assert.DoesNotContain("Fixture disposal failed.", warning);
            Assert.Equal("second", (await replacement.AuthenticateAsync(runtime, ct))["x-fixture-key"]);
        }
        finally
        {
            await generation.BeginCleanup().WaitAsync(TimeSpan.FromSeconds(15));
        }

        Assert.Equal(1, disposeAttempts);
    }

    private sealed class CleanupLogger : ILogger<DotNetExtensionRuntimeManager>
    {
        public TaskCompletionSource<string> Warning { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            var message = formatter(state, exception);
            if (message.Contains(PluginActivationBlockers.CleanupFailed, StringComparison.Ordinal))
                Warning.TrySetResult(message);
        }
    }

    private static IProviderNativeHttpRouter ResolveRouter(ModelBackendResolver resolver, EffectiveModelRuntime runtime) =>
        Assert.IsAssignableFrom<IProviderNativeHttpRouter>(
            resolver.ResolveBackend(runtime).Provider.GetService(typeof(IProviderNativeHttpRouter)));

    private static Task SetCredentialAsync(ProviderCredentialStore store, string credential, CancellationToken ct) =>
        store.ModifyAsync((_, _) => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(credential)), ct);

    private const string Source = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using DotCraft.Agents;
        using DotCraft.Configuration;
        using DotCraft.Contributions;
        using DotCraft.Plugins;
        using Microsoft.Extensions.AI;

        namespace RoutedFixture;

        public sealed class Plugin : IDotCraftPlugin
        {
            public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken ct)
            {
                context.Contributions.Add<IModelBackendSource>(new Source(new Backend(
                    (Func<string, Task>)context.Services.GetService(typeof(Func<string, Task>))!,
                    (Action<string>)context.Services.GetService(typeof(Action<string>))!)));
                return ValueTask.CompletedTask;
            }
        }

        internal sealed record Source(IModelBackend Backend) : IModelBackendSource;

        internal sealed class Backend(Func<string, Task> authenticate, Action<string> disposed) : IModelBackend
        {
            public ModelBackendDescriptor Descriptor { get; } = new("routed-fixture", "Routed fixture",
                ModelProviderProtocols.OpenAIChatCompletions, [ModelProviderProtocols.OpenAIChatCompletions], [], new());

            public IModelProvider CreateProvider(ModelBackendContext context)
            {
                var credential = context.Credentials.ReadAsync(CancellationToken.None).GetAwaiter().GetResult()?.GetString()
                    ?? throw new InvalidOperationException("Test credential required.");
                return new Provider(credential, authenticate, disposed);
            }
        }

        internal sealed class Provider(string credential, Func<string, Task> authenticate, Action<string> disposed)
            : IModelProvider, IProviderNativeHttpRouter, IDisposable
        {
            public IReadOnlyCollection<string> Protocols => [ModelProviderProtocols.OpenAIChatCompletions];

            public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => throw new NotSupportedException();

            public ProviderHttpRoute ResolveRoute(EffectiveModelRuntime runtime) =>
                new(ModelProviderProtocols.OpenAIChatCompletions, "https://fixture.invalid/v1", runtime.Model);

            public async Task<IReadOnlyDictionary<string, string>> AuthenticateAsync(EffectiveModelRuntime runtime, CancellationToken ct)
            {
                await authenticate(credential);
                return new Dictionary<string, string> { ["x-fixture-key"] = credential };
            }

            public void Dispose() => disposed(credential);
        }
        """;
}
