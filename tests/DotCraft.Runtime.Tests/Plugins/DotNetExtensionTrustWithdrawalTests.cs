using System.Collections.Concurrent;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Runtime;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotNetExtensionTestBundle;

namespace DotCraft.Tests.Runtime.Plugins;

public sealed class DotNetExtensionTrustWithdrawalTests : IDisposable
{
    private const string ProviderId = "fixture.provider";
    private const string ConsumerId = "fixture.consumer";
    private readonly PluginRuntimeHarness _harness = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrustRevocationWithdrawsProviderWhileConsumerStillUsesItsExport(bool external)
    {
        WriteBundles();
        var signals = Enumerable.Range(0, 3)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var disposalOrder = new ConcurrentQueue<string>();
        _harness.Services = CreateServiceProvider(
            (typeof(TaskCompletionSource[]), signals),
            (typeof(ConcurrentQueue<string>), disposalOrder));

        DotNetExtensionTrustConfigStore? authority = null;
        var providerFingerprint = DotNetExtensionFingerprint.Compute(_harness.PluginRoot(ProviderId));
        if (external)
        {
            var configPath = Path.Combine(_harness.Root, "user-data", "config.json");
            _harness.Config.GlobalConfigPath = configPath;
            authority = new(DotNetExtensionTrustConfigStore.PathForConfig(configPath));
            authority.SetTrusted(ProviderId, providerFingerprint, isTrusted: true);
            authority.SetTrusted(ConsumerId, DotNetExtensionFingerprint.Compute(_harness.PluginRoot(ConsumerId)), isTrusted: true);
        }

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = deadline.Token;
        await using var manager = _harness.CreateManager(trustInstalled: !external);
        await manager.StartAsync(ct);
        using var resolver = new ModelBackendResolver(new ModelProviderRegistry([]), _harness.Registry, _harness.Root);
        _harness.Config.Providers["fixture"] = new() { BackendId = ProviderId };
        var runtime = ModelProviderResolver.ResolveProvider(_harness.Config, "fixture") with { Model = "fixture-model" };
        var (resolved, provider, stopping) = resolver.ResolveBackend(runtime);
        var router = Assert.IsAssignableFrom<IProviderNativeHttpRouter>(provider.GetService(typeof(IProviderNativeHttpRouter)));

        Task<PluginRuntimeMutationResult>? revoke = null;
        try
        {
            if (authority != null)
                authority.SetTrusted(ProviderId, providerFingerprint, isTrusted: false);
            else
                revoke = manager.RevokeTrustAsync(ProviderId, ct);

            await signals[0].Task.WaitAsync(ct);
            Assert.True(stopping.IsCancellationRequested);
            Assert.Throws<ModelBackendUnavailableException>(() => router.ResolveRoute(resolved));
            Assert.Throws<ModelBackendUnavailableException>(() => resolver.ResolveBackend(runtime));
            Assert.Empty(disposalOrder);
        }
        finally
        {
            signals[1].TrySetResult();
        }

        if (revoke != null)
            Assert.Equal(PluginRuntimeMutationOutcome.Applied, (await revoke.WaitAsync(ct)).Outcome);
        await signals[2].Task.WaitAsync(ct);
        Assert.Equal(["consumer", "provider"], disposalOrder.ToArray());
    }

    private void WriteBundles()
    {
        const string apiName = "Fixture.Withdrawal.Api.dll";
        var providerRoot = _harness.PluginRoot(ProviderId);
        var apiPath = Path.Combine(providerRoot, "dotnet", apiName);
        Compile(apiPath, "namespace Fixture.Withdrawal.Api; public interface IValue { string Read(); }");
        WritePluginBundle(providerRoot, ProviderId, "Fixture.Provider.Plugin", ProviderSource,
            exportedApiAssemblies: ["./dotnet/" + apiName], runtimeReferences: [apiPath]);
        var consumerRoot = _harness.PluginRoot(ConsumerId);
        var consumerApi = Path.Combine(consumerRoot, "dotnet", apiName);
        Directory.CreateDirectory(Path.GetDirectoryName(consumerApi)!);
        File.Copy(apiPath, consumerApi);
        WritePluginBundle(consumerRoot, ConsumerId, "Fixture.Consumer.Plugin", ConsumerSource,
            dependencies: new Dictionary<string, string> { [ProviderId] = "1.0.0" }, runtimeReferences: [consumerApi]);
    }

    public void Dispose() => _harness.Dispose();

    private const string ProviderSource = """
        using System;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using DotCraft.Agents;
        using DotCraft.Configuration;
        using DotCraft.Contributions;
        using DotCraft.Plugins;
        using Fixture.Withdrawal.Api;
        using Microsoft.Extensions.AI;

        namespace Fixture.Provider;
        public sealed class Plugin : IDotCraftPlugin, IDisposable
        {
            private ConcurrentQueue<string> _order;
            private TaskCompletionSource _disposed;

            public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
            {
                _order = (ConcurrentQueue<string>)context.Services.GetService(typeof(ConcurrentQueue<string>));
                _disposed = ((TaskCompletionSource[])context.Services.GetService(typeof(TaskCompletionSource[])))[2];
                context.Exports.Add<IValue>(new Value(context.Lifetime.Stopping));
                context.Contributions.Add<IModelBackendSource>(new BackendFixture());
                return ValueTask.CompletedTask;
            }

            public void Dispose()
            {
                _order.Enqueue("provider");
                _disposed.TrySetResult();
            }

            private sealed class Value(CancellationToken stopping) : IValue
            {
                public string Read()
                {
                    stopping.ThrowIfCancellationRequested();
                    return "consumer";
                }
            }

            private sealed class BackendFixture : IModelBackend, IModelBackendSource
            {
                IModelBackend IModelBackendSource.Backend => this;
                public ModelBackendDescriptor Descriptor { get; } = new("fixture.provider", "Fixture", "openai-chat-completions",
                    ["openai-chat-completions"], [], new());
                public IModelProvider CreateProvider(ModelBackendContext context) => new RoutedProvider();
            }

            private sealed class RoutedProvider : IModelProvider, IProviderNativeHttpRouter
            {
                public IReadOnlyCollection<string> Protocols => ["openai-chat-completions"];
                public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => throw new NotSupportedException();
                public ProviderHttpRoute ResolveRoute(EffectiveModelRuntime runtime) =>
                    new("openai-chat-completions", "https://upstream.invalid/v1", runtime.Model);
                public Task<IReadOnlyDictionary<string, string>> AuthenticateAsync(EffectiveModelRuntime runtime, CancellationToken ct) =>
                    throw new NotSupportedException();
            }
        }
        """;

    private const string ConsumerSource = """
        using System;
        using System.Collections.Concurrent;
        using System.Threading;
        using System.Threading.Tasks;
        using DotCraft.Plugins;
        using Fixture.Withdrawal.Api;

        namespace Fixture.Consumer;
        public sealed class Plugin : IDotCraftPlugin, IAsyncDisposable
        {
            private ConcurrentQueue<string> _order;
            private TaskCompletionSource[] _signals;
            private IValue _value;

            public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
            {
                _order = (ConcurrentQueue<string>)context.Services.GetService(typeof(ConcurrentQueue<string>));
                _signals = (TaskCompletionSource[])context.Services.GetService(typeof(TaskCompletionSource[]));
                _value = context.Dependencies.GetRequired<IValue>("fixture.provider");
                return ValueTask.CompletedTask;
            }

            public async ValueTask DisposeAsync()
            {
                _signals[0].TrySetResult();
                await _signals[1].Task.ConfigureAwait(false);
                _order.Enqueue(_value.Read());
            }
        }
        """;
}
