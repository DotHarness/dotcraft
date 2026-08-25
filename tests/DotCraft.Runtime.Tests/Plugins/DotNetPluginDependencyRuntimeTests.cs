using DotCraft.Plugins;
using DotCraft.Runtime;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotNetPluginTestBundle;
using static DotCraft.Tests.Runtime.Plugins.PluginRuntimeHarness;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>Covers cross-plugin dependency ordering, binding, and the blockers it produces.</summary>
public sealed class DotNetPluginDependencyRuntimeTests : IDisposable
{
    private readonly PluginRuntimeHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Runtime_BindsDirectTypedServiceAndRestartsConsumerAfterProvider()
    {
        var providerRoot = _harness.PluginRoot("service.provider");
        var providerDotnet = Path.Combine(providerRoot, "dotnet");
        var apiPath = Path.Combine(providerDotnet, "Greeting.Api.dll");
        var providerPrivate = Path.Combine(providerDotnet, "Private.Shared.dll");
        Compile(apiPath, "namespace Greeting.Api; public interface IGreeting { string Get(); }");
        Compile(
            providerPrivate,
            "namespace PrivateShared; public static class Value { public static string Get() => \"provider\"; }");
        WritePluginBundle(
            providerRoot,
            "service.provider",
            "ServiceProvider.Plugin",
            """
            using System;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            using Greeting.Api;
            using PrivateShared;
            namespace ServiceProvider;
            public sealed class Plugin : IDotCraftPlugin, IDisposable
            {
                private string _workspace = "";
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    _workspace = context.WorkspaceRoot;
                    context.Exports.Add<IGreeting>(new Greeting());
                    return ValueTask.CompletedTask;
                }
                public void Dispose() => File.AppendAllText(Path.Combine(_workspace, "stop-order.log"), "provider\n");
                private sealed class Greeting : IGreeting
                {
                    public string Get() => Value.Get();
                }
            }
            """,
            exportedApiAssemblies: ["./dotnet/Greeting.Api.dll"],
            runtimeReferences: [apiPath, providerPrivate]);

        var consumerRoot = _harness.PluginRoot("service.consumer");
        var consumerDotnet = Path.Combine(consumerRoot, "dotnet");
        Directory.CreateDirectory(consumerDotnet);
        var consumerApi = Path.Combine(consumerDotnet, "Greeting.Api.dll");
        File.Copy(apiPath, consumerApi);
        var consumerPrivate = Path.Combine(consumerDotnet, "Private.Shared.dll");
        Compile(
            consumerPrivate,
            "namespace PrivateShared; public static class Value { public static string Get() => \"consumer\"; }");
        WritePluginBundle(
            consumerRoot,
            "service.consumer",
            "ServiceConsumer.Plugin",
            """
            using System;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            using Greeting.Api;
            using PrivateShared;
            namespace ServiceConsumer;
            public sealed class Plugin : IDotCraftPlugin, IDisposable
            {
                private string _workspace = "";
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    _workspace = context.WorkspaceRoot;
                    Directory.CreateDirectory(context.DataRoot);
                    var output = Path.Combine(context.DataRoot, "service.log");
                    var resolver = context.Dependencies;
                    var service = resolver.GetRequired<IGreeting>("service.provider");
                    File.AppendAllText(output, service.Get() + "-" + Value.Get() + "\n");
                    var apiFromConsumer = Path.GetFullPath(typeof(IGreeting).Assembly.Location)
                        .StartsWith(Path.GetFullPath(context.ContentRoot), StringComparison.OrdinalIgnoreCase);
                    File.AppendAllText(output, "api-from-consumer=" + apiFromConsumer + "\n");
                    context.Lifetime.Run(async stopping =>
                    {
                        await Task.Delay(20, stopping);
                        File.AppendAllText(output, "retained=" + service.Get() + "\n");
                        try { resolver.GetRequired<IGreeting>("service.provider"); }
                        catch (Exception) { File.AppendAllText(output, "late-rejected\n"); }
                    });
                    return ValueTask.CompletedTask;
                }
                public void Dispose() => File.AppendAllText(Path.Combine(_workspace, "stop-order.log"), "consumer\n");
            }
            """,
            dependencies: new Dictionary<string, string> { ["service.provider"] = "1.0.0" },
            runtimeReferences: [consumerApi, consumerPrivate]);
        await using var manager = _harness.CreateManager();

        await manager.StartAsync(CancellationToken.None);

        Assert.All(manager.Snapshot.Plugins, plugin => AssertState(plugin, PluginDotnetRuntimeState.Active));
        AssertDependencyAvailability(
            manager,
            "service.consumer",
            "service.provider",
            PluginDependencyAvailability.Active,
            "1.0.0");
        var firstProvider = Plugin(manager, "service.provider").GenerationId;
        var firstConsumer = Plugin(manager, "service.consumer").GenerationId;
        var outputPath = _harness.DataPath("service.consumer", "service.log");
        await WaitForLineAsync(outputPath, "late-rejected");
        Assert.Contains("provider-consumer", PluginLogFile.ReadLines(outputPath));
        Assert.Contains("api-from-consumer=False", PluginLogFile.ReadLines(outputPath));
        Assert.Contains("retained=provider", PluginLogFile.ReadLines(outputPath));

        await manager.SetEnabledAsync("service.provider", enabled: false);

        await WaitForStateAsync(manager, "service.provider", PluginDotnetRuntimeState.Stopped);
        var consumer = Plugin(manager, "service.consumer");
        AssertState(consumer, PluginDotnetRuntimeState.Blocked);
        Assert.Contains(consumer.Blockers, blocker => blocker.Code == "PluginDependencyUnsatisfied");
        AssertDependencyAvailability(
            manager,
            "service.consumer",
            "service.provider",
            PluginDependencyAvailability.Disabled,
            "1.0.0");
        Assert.Equal(["consumer", "provider"], PluginLogFile.ReadLines(Path.Combine(_harness.Workspace, "stop-order.log")));

        await manager.SetEnabledAsync("service.provider", enabled: true);

        var provider = Plugin(manager, "service.provider");
        consumer = Plugin(manager, "service.consumer");
        AssertState(provider, PluginDotnetRuntimeState.Active);
        AssertState(consumer, PluginDotnetRuntimeState.Active);
        AssertDependencyAvailability(
            manager,
            "service.consumer",
            "service.provider",
            PluginDependencyAvailability.Active,
            "1.0.0");
        Assert.NotEqual(firstProvider, provider.GenerationId);
        Assert.NotEqual(firstConsumer, consumer.GenerationId);
    }

    [Fact]
    public async Task Runtime_TellsUnsatisfiedDependencyReasonsApartAndKeepsCycleSeparate()
    {
        _harness.WriteNoop("missing.consumer", dependencies: new Dictionary<string, string> { ["absent.provider"] = "1.0.0" });
        _harness.WriteNoop("below.provider", version: "1.0.0");
        _harness.WriteNoop("below.consumer", dependencies: new Dictionary<string, string> { ["below.provider"] = "2.0.0" });
        _harness.WriteNoop("disabled.provider");
        _harness.WriteNoop("disabled.consumer", dependencies: new Dictionary<string, string> { ["disabled.provider"] = "1.0.0" });
        _harness.WriteNoop("cycle.a", dependencies: new Dictionary<string, string> { ["cycle.b"] = "1.0.0" });
        _harness.WriteNoop("cycle.b", dependencies: new Dictionary<string, string> { ["cycle.a"] = "1.0.0" });
        var builtInRoot = Path.Combine(_harness.Root, "built-ins");
        WritePluginBundle(
            Path.Combine(builtInRoot, "catalog.provider"),
            "catalog.provider",
            "CatalogProvider.Plugin",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            namespace CatalogProvider;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }
            """);
        _harness.WriteNoop(
            "catalog.consumer",
            dependencies: new Dictionary<string, string> { ["catalog.provider"] = "1.0.0" });
        _harness.Config.Plugins.DisabledPlugins.Add("disabled.provider");
        await using var manager = _harness.CreateManager(builtInPluginSourceRoots: [builtInRoot]);

        await manager.StartAsync(CancellationToken.None);

        AssertBlocker(manager, "missing.consumer", "PluginDependencyUnsatisfied", "missing");
        AssertBlocker(manager, "below.consumer", "PluginDependencyUnsatisfied", "versionUnsatisfied");
        AssertBlocker(manager, "disabled.consumer", "PluginDependencyUnsatisfied", "disabled");
        AssertBlocker(manager, "catalog.consumer", "PluginDependencyUnsatisfied", "missing");
        AssertBlocker(manager, "cycle.a", "PluginDependencyCycle");
        AssertBlocker(manager, "cycle.b", "PluginDependencyCycle");
        AssertDependencyAvailability(
            manager,
            "missing.consumer",
            "absent.provider",
            PluginDependencyAvailability.Missing,
            null);
        AssertDependencyAvailability(
            manager,
            "below.consumer",
            "below.provider",
            PluginDependencyAvailability.VersionUnsatisfied,
            "1.0.0");
        AssertDependencyAvailability(
            manager,
            "disabled.consumer",
            "disabled.provider",
            PluginDependencyAvailability.Disabled,
            "1.0.0");
        AssertDependencyAvailability(
            manager,
            "cycle.a",
            "cycle.b",
            PluginDependencyAvailability.Blocked,
            "1.0.0");
        AssertDependencyAvailability(
            manager,
            "catalog.consumer",
            "catalog.provider",
            PluginDependencyAvailability.Missing,
            null);
        AssertState(Plugin(manager, "below.provider"), PluginDotnetRuntimeState.Active);
        AssertState(Plugin(manager, "disabled.provider"), PluginDotnetRuntimeState.Stopped);
    }

    [Fact]
    public async Task Runtime_AcceptsAProviderAboveTheDeclaredMinimum()
    {
        _harness.WriteNoop("minimum.provider", version: "1.4.2");
        _harness.WriteNoop("minimum.consumer", dependencies: new Dictionary<string, string> { ["minimum.provider"] = "1.0.0" });
        await using var manager = _harness.CreateManager();

        await manager.StartAsync(CancellationToken.None);

        AssertState(Plugin(manager, "minimum.provider"), PluginDotnetRuntimeState.Active);
        AssertState(Plugin(manager, "minimum.consumer"), PluginDotnetRuntimeState.Active);
    }

    [Fact]
    public async Task Runtime_BlocksAProviderOutsideTheDeclaredCompatibilityLine()
    {
        _harness.WriteNoop("major.provider", version: "2.0.0");
        _harness.WriteNoop(
            "major.consumer",
            dependencies: new Dictionary<string, string> { ["major.provider"] = "1.0.0" });
        await using var manager = _harness.CreateManager();

        await manager.StartAsync(CancellationToken.None);

        AssertState(Plugin(manager, "major.provider"), PluginDotnetRuntimeState.Active);
        AssertBlocker(manager, "major.consumer", "PluginDependencyUnsatisfied", "versionUnsatisfied");
        AssertDependencyAvailability(
            manager,
            "major.consumer",
            "major.provider",
            PluginDependencyAvailability.VersionUnsatisfied,
            "2.0.0");
    }

    [Fact]
    public async Task Runtime_ProjectsFaultedProviderAsUnavailableToConsumer()
    {
        WritePluginBundle(
            _harness.PluginRoot("faulted.provider"),
            "faulted.provider",
            "Faulted.Plugin",
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            namespace Faulted;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                    => throw new InvalidOperationException("provider failed");
            }
            """);
        _harness.WriteNoop(
            "faulted.consumer",
            dependencies: new Dictionary<string, string> { ["faulted.provider"] = "1.0.0" });
        await using var manager = _harness.CreateManager();

        await manager.StartAsync(CancellationToken.None);

        AssertState(Plugin(manager, "faulted.provider"), PluginDotnetRuntimeState.Faulted);
        AssertBlocker(manager, "faulted.consumer", "PluginDependencyUnsatisfied", "faulted");
        AssertDependencyAvailability(
            manager,
            "faulted.consumer",
            "faulted.provider",
            PluginDependencyAvailability.Faulted,
            "1.0.0");
    }

    [Fact]
    public async Task Runtime_BlocksMissingServiceAndAmbiguousDirectProviderApis()
    {
        var missingProviderRoot = _harness.PluginRoot("missing-service.provider");
        var missingApi = Path.Combine(missingProviderRoot, "dotnet", "Missing.Service.Api.dll");
        Compile(missingApi, "namespace Missing.Service.Api; public interface IService { }");
        WritePluginBundle(
            missingProviderRoot,
            "missing-service.provider",
            "MissingProvider.Plugin",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            namespace MissingProvider;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }
            """,
            exportedApiAssemblies: ["./dotnet/Missing.Service.Api.dll"]);
        var missingConsumerRoot = _harness.PluginRoot("missing-service.consumer");
        var missingConsumerApi = Path.Combine(missingConsumerRoot, "dotnet", "Missing.Service.Api.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(missingConsumerApi)!);
        File.Copy(missingApi, missingConsumerApi);
        WritePluginBundle(
            missingConsumerRoot,
            "missing-service.consumer",
            "MissingConsumer.Plugin",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            using Missing.Service.Api;
            namespace MissingConsumer;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    context.Dependencies.GetRequired<IService>("missing-service.provider");
                    return ValueTask.CompletedTask;
                }
            }
            """,
            dependencies: new Dictionary<string, string> { ["missing-service.provider"] = "1.0.0" },
            runtimeReferences: [missingConsumerApi]);

        var commonApi = Path.Combine(_harness.Root, "shared", "Common.Api.dll");
        Compile(commonApi, "namespace Common.Api; public interface ICommon { }");
        foreach (var providerId in new[] { "common.a", "common.b" })
        {
            var providerRoot = _harness.PluginRoot(providerId);
            var providerApi = Path.Combine(providerRoot, "dotnet", "Common.Api.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(providerApi)!);
            File.Copy(commonApi, providerApi);
            WritePluginBundle(
                providerRoot,
                providerId,
                "CommonProvider.Plugin",
                """
                using System.Threading;
                using System.Threading.Tasks;
                using DotCraft.Plugins;
                namespace CommonProvider;
                public sealed class Plugin : IDotCraftPlugin
                {
                    public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                        => ValueTask.CompletedTask;
                }
                """,
                exportedApiAssemblies: ["./dotnet/Common.Api.dll"]);
        }

        _harness.WriteNoop(
            "common.consumer",
            dependencies: new Dictionary<string, string>
            {
                ["common.a"] = "1.0.0",
                ["common.b"] = "1.0.0"
            });
        await using var manager = _harness.CreateManager();

        await manager.StartAsync(CancellationToken.None);

        AssertBlocker(manager, "missing-service.consumer", "PluginServiceExportMissing");
        AssertBlocker(manager, "common.consumer", "PluginApiAssemblyConflict");
        AssertState(Plugin(manager, "common.a"), PluginDotnetRuntimeState.Active);
        AssertState(Plugin(manager, "common.b"), PluginDotnetRuntimeState.Active);
    }

    [Fact]
    public async Task Runtime_DeactivatesProviderAlthoughItsConsumerLeaked()
    {
        WriteDisposable("pin.provider", "provider");
        WriteDisposable(
            "pin.middle",
            "middle",
            new Dictionary<string, string> { ["pin.provider"] = "1.0.0" });
        _harness.WriteLeaking(
            "pin.leaf",
            dependencies: new Dictionary<string, string> { ["pin.middle"] = "1.0.0" });
        await using var manager = _harness.CreateManager();
        await manager.StartAsync(CancellationToken.None);

        await manager.SetEnabledAsync("pin.provider", enabled: false);

        // The leaf pinned its own memory only: teardown still ran through it to the plugins behind.
        await WaitForStateAsync(manager, "pin.provider", PluginDotnetRuntimeState.Stopped);
        AssertState(Plugin(manager, "pin.middle"), PluginDotnetRuntimeState.Blocked);
        AssertState(Plugin(manager, "pin.leaf"), PluginDotnetRuntimeState.Blocked);
        Assert.Equal(1, Plugin(manager, "pin.leaf").LeakedGenerations);
        Assert.Equal(0, Plugin(manager, "pin.provider").LeakedGenerations);
        Assert.Equal(
            ["middle", "provider"],
            PluginLogFile.ReadLines(Path.Combine(_harness.Workspace, "pin-stop.log")));
    }

    private void WriteDisposable(
        string pluginId,
        string label,
        IReadOnlyDictionary<string, string>? dependencies = null) =>
        WritePluginBundle(
            _harness.PluginRoot(pluginId),
            pluginId,
            "DisposablePlugin.Plugin",
            $$"""
            using System;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            namespace DisposablePlugin;
            public sealed class Plugin : IDotCraftPlugin, IDisposable
            {
                private string _workspace = "";
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    _workspace = context.WorkspaceRoot;
                    return ValueTask.CompletedTask;
                }
                public void Dispose() => File.AppendAllText(Path.Combine(_workspace, "pin-stop.log"), "{{label}}\n");
            }
            """,
            dependencies: dependencies);

    private static void AssertDependencyAvailability(
        DotNetPluginRuntimeManager manager,
        string consumerId,
        string providerId,
        PluginDependencyAvailability expectedAvailability,
        string? expectedVersion)
    {
        var observations = Plugin(manager, consumerId).DependencyObservations;
        Assert.NotNull(observations);
        var observation = Assert.Single(
            observations,
            dependency => string.Equals(dependency.Id, providerId, StringComparison.Ordinal));
        Assert.Equal(expectedAvailability, observation.Availability);
        Assert.Equal(expectedVersion, observation.ObservedVersion);
    }
}
