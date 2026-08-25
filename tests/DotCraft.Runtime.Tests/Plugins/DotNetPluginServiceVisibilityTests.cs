using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotNetPluginTestBundle;
using static DotCraft.Tests.Runtime.Plugins.PluginRuntimeHarness;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>Covers the service boundary projected into plugin activation contexts.</summary>
public sealed class DotNetPluginServiceVisibilityTests : IDisposable
{
    private readonly PluginRuntimeHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Runtime_HidesHostControlsAndPreservesApplicationServices()
    {
        WritePluginBundle(
            _harness.PluginRoot("service-visibility"),
            "service-visibility",
            "ServiceVisibility.Plugin",
            """
            using System;
            using System.Collections.Generic;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Contributions;
            using DotCraft.Plugins;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Hosting;
            namespace ServiceVisibility;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    Type[] hiddenTypes =
                    [
                        typeof(IServiceProvider),
                        typeof(IServiceScopeFactory),
                        typeof(IHost),
                        typeof(IHostedService),
                        typeof(IEnumerable<IHostedService>),
                        typeof(IHostApplicationLifetime),
                        typeof(IHostLifetime),
                        typeof(IPluginDotnetRuntimeCoordinator),
                        typeof(IContributionView),
                        typeof(IEnumerable<IContributionView>)
                    ];
                    foreach (var hiddenType in hiddenTypes)
                    {
                        if (context.Services.GetService(hiddenType) is not null)
                            throw new InvalidOperationException($"Host control service '{hiddenType.Name}' escaped.");
                    }

                    var value = (string?)context.Services.GetService(typeof(string))
                        ?? throw new InvalidOperationException("Approved application service is missing.");
                    Directory.CreateDirectory(context.DataRoot);
                    File.WriteAllText(Path.Combine(context.DataRoot, "service.txt"), value);
                    return ValueTask.CompletedTask;
                }
            }
            """,
            runtimeReferences:
            [
                typeof(IContributionView).Assembly.Location,
                typeof(IServiceScopeFactory).Assembly.Location,
                typeof(IHostedService).Assembly.Location
            ]);
        var hidden = new object();
        _harness.Services = CreateServiceProvider(
            (typeof(string), "approved"),
            (typeof(IServiceProvider), EmptyServices),
            (typeof(IServiceScopeFactory), hidden),
            (typeof(IHost), hidden),
            (typeof(IHostedService), hidden),
            (typeof(IEnumerable<IHostedService>), hidden),
            (typeof(IHostApplicationLifetime), hidden),
            (typeof(IHostLifetime), hidden),
            (typeof(IPluginDotnetRuntimeCoordinator), hidden),
            (typeof(IContributionView), _harness.Registry),
            (typeof(IEnumerable<IContributionView>), new IContributionView[] { _harness.Registry }));
        await using var manager = _harness.CreateManager();

        await manager.StartAsync(CancellationToken.None);

        AssertState(Plugin(manager, "service-visibility"), PluginDotnetRuntimeState.Active);
        Assert.Equal(
            "approved",
            PluginLogFile.ReadText(_harness.DataPath("service-visibility", "service.txt")));
    }
}
