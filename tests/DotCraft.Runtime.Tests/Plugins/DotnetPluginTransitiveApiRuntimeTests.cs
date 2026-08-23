using DotCraft.Plugins;
using DotCraft.Runtime;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotnetPluginTestBundle;
using static DotCraft.Tests.Runtime.Plugins.PluginRuntimeHarness;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>Covers API assembly identity across transitive plugin dependency closures.</summary>
public sealed class DotnetPluginTransitiveApiRuntimeTests : IDisposable
{
    private readonly PluginRuntimeHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Runtime_SharesTransitiveApiIdentityWithAnIndirectConsumer()
    {
        var dependencyRoot = _harness.PluginRoot("transitive.dependency");
        var dependencyApi = Path.Combine(dependencyRoot, "dotnet", "Transitive.Dependency.Api.dll");
        Compile(
            dependencyApi,
            "namespace Transitive.Dependency.Api; public interface IDependency { string Value { get; } }");
        WritePluginBundle(
            dependencyRoot,
            "transitive.dependency",
            "TransitiveDependency.Plugin",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            using Transitive.Dependency.Api;
            namespace TransitiveDependency;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    context.Exports.Add<IDependency>(new Dependency());
                    return ValueTask.CompletedTask;
                }
                private sealed class Dependency : IDependency
                {
                    public string Value => "dependency";
                }
            }
            """,
            exportedApiAssemblies: ["./dotnet/Transitive.Dependency.Api.dll"],
            runtimeReferences: [dependencyApi]);

        var serviceRoot = _harness.PluginRoot("transitive.service");
        var serviceDotnet = Path.Combine(serviceRoot, "dotnet");
        Directory.CreateDirectory(serviceDotnet);
        var serviceDependencyApi = Path.Combine(serviceDotnet, "Transitive.Dependency.Api.dll");
        File.Copy(dependencyApi, serviceDependencyApi);
        var serviceApi = Path.Combine(serviceDotnet, "Transitive.Service.Api.dll");
        Compile(
            serviceApi,
            """
            using Transitive.Dependency.Api;
            namespace Transitive.Service.Api;
            public interface ITransitiveService { IDependency Dependency { get; } }
            """,
            references: [serviceDependencyApi]);
        WritePluginBundle(
            serviceRoot,
            "transitive.service",
            "TransitiveService.Plugin",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            using Transitive.Dependency.Api;
            using Transitive.Service.Api;
            namespace TransitiveService;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    var dependency = context.Dependencies.GetRequired<IDependency>("transitive.dependency");
                    context.Exports.Add<ITransitiveService>(new Service(dependency));
                    return ValueTask.CompletedTask;
                }
                private sealed class Service(IDependency dependency) : ITransitiveService
                {
                    public IDependency Dependency { get; } = dependency;
                }
            }
            """,
            dependencies: new Dictionary<string, string> { ["transitive.dependency"] = "1.0.0" },
            exportedApiAssemblies: ["./dotnet/Transitive.Service.Api.dll"],
            runtimeReferences: [serviceApi, serviceDependencyApi]);

        var consumerRoot = _harness.PluginRoot("transitive.consumer");
        var consumerDotnet = Path.Combine(consumerRoot, "dotnet");
        Directory.CreateDirectory(consumerDotnet);
        var consumerDependencyApi = Path.Combine(consumerDotnet, "Transitive.Dependency.Api.dll");
        var consumerServiceApi = Path.Combine(consumerDotnet, "Transitive.Service.Api.dll");
        File.Copy(dependencyApi, consumerDependencyApi);
        File.Copy(serviceApi, consumerServiceApi);
        WritePluginBundle(
            consumerRoot,
            "transitive.consumer",
            "TransitiveConsumer.Plugin",
            """
            using System;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            using Transitive.Dependency.Api;
            using Transitive.Service.Api;
            namespace TransitiveConsumer;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    Directory.CreateDirectory(context.DataRoot);
                    var service = context.Dependencies.GetRequired<ITransitiveService>("transitive.service");
                    var propertyType = typeof(ITransitiveService).GetProperty(nameof(ITransitiveService.Dependency))!.PropertyType;
                    File.WriteAllText(
                        Path.Combine(context.DataRoot, "identity.log"),
                        service.Dependency.Value + "|" + ReferenceEquals(propertyType, typeof(IDependency)));
                    return ValueTask.CompletedTask;
                }
            }
            """,
            dependencies: new Dictionary<string, string> { ["transitive.service"] = "1.0.0" },
            runtimeReferences: [consumerServiceApi, consumerDependencyApi]);
        await using var manager = _harness.CreateManager();

        await manager.StartAsync(CancellationToken.None);

        AssertState(Plugin(manager, "transitive.dependency"), PluginDotnetRuntimeState.Active);
        AssertState(Plugin(manager, "transitive.service"), PluginDotnetRuntimeState.Active);
        AssertState(Plugin(manager, "transitive.consumer"), PluginDotnetRuntimeState.Active);
        Assert.Equal(
            "dependency|True",
            File.ReadAllText(_harness.DataPath("transitive.consumer", "identity.log")));
    }

    [Fact]
    public async Task Runtime_BlocksAStableConflictFromDifferentTransitiveExporters()
    {
        var commonApi = Path.Combine(_harness.Root, "shared", "Transitive.Common.Api.dll");
        Compile(commonApi, "namespace Transitive.Common.Api; public interface ICommon { }");
        foreach (var originId in new[] { "diamond.origin-z", "diamond.origin-a" })
        {
            var originRoot = _harness.PluginRoot(originId);
            var originApi = Path.Combine(originRoot, "dotnet", "Transitive.Common.Api.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(originApi)!);
            File.Copy(commonApi, originApi);
            WritePluginBundle(
                originRoot,
                originId,
                "DiamondOrigin.Plugin",
                """
                using System.Threading;
                using System.Threading.Tasks;
                using DotCraft.Plugins;
                namespace DiamondOrigin;
                public sealed class Plugin : IDotCraftPlugin
                {
                    public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                        => ValueTask.CompletedTask;
                }
                """,
                exportedApiAssemblies: ["./dotnet/Transitive.Common.Api.dll"]);
        }

        _harness.WriteNoop(
            "diamond.bridge-a",
            dependencies: new Dictionary<string, string> { ["diamond.origin-z"] = "1.0.0" });
        _harness.WriteNoop(
            "diamond.bridge-z",
            dependencies: new Dictionary<string, string> { ["diamond.origin-a"] = "1.0.0" });
        _harness.WriteNoop(
            "diamond.consumer",
            dependencies: new Dictionary<string, string>
            {
                ["diamond.bridge-a"] = "1.0.0",
                ["diamond.bridge-z"] = "1.0.0"
            });
        await using var manager = _harness.CreateManager();

        await manager.StartAsync(CancellationToken.None);

        AssertState(Plugin(manager, "diamond.origin-a"), PluginDotnetRuntimeState.Active);
        AssertState(Plugin(manager, "diamond.origin-z"), PluginDotnetRuntimeState.Active);
        AssertState(Plugin(manager, "diamond.bridge-a"), PluginDotnetRuntimeState.Active);
        AssertState(Plugin(manager, "diamond.bridge-z"), PluginDotnetRuntimeState.Active);
        var consumer = Plugin(manager, "diamond.consumer");
        AssertState(consumer, PluginDotnetRuntimeState.Blocked);
        var blocker = Assert.Single(
            consumer.Blockers,
            static blocker => blocker.Code == "PluginApiAssemblyConflict");
        Assert.Equal("Transitive.Common.Api", blocker.Parameters["assemblyName"].GetString());
        Assert.Equal(
            ["diamond.origin-a", "diamond.origin-z"],
            blocker.Parameters["providerIds"].EnumerateArray().Select(static id => id.GetString()));
    }
}
