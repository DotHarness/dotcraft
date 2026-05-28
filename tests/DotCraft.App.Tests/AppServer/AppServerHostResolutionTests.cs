using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Hosting;
using DotCraft.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Tests.AppServer;

public sealed class AppServerHostResolutionTests
{
    [Fact]
    public async Task HostBuilder_BuildsAppServerHost_WithWorkspaceRuntimeRegistered()
    {
        using var fixture = new WorkspaceFixture();
        var config = new AppConfig();
        config.SetSection("AppServer", new AppServerConfig
        {
            Mode = AppServerMode.Stdio
        });

        var paths = new DotCraftPaths
        {
            WorkspacePath = fixture.WorkspacePath,
            CraftPath = fixture.BotPath
        };
        var registry = new ModuleRegistry();
        ModuleRegistrations.RegisterAll(registry);

        var services = new ServiceCollection()
            .AddSingleton(registry)
            .AddDotCraft(config, fixture.WorkspacePath, fixture.BotPath);

        var builder = new HostBuilder(registry, config, paths, "app-server");
        var (provider, host) = builder.Build(services);

        await using var disposableProvider = (ServiceProvider)provider;
        Assert.IsType<AppServerHost>(host);
        Assert.NotNull(provider.GetRequiredService<WorkspaceRuntime>());
    }

    private sealed class WorkspaceFixture : IDisposable
    {
        public string WorkspacePath { get; } = Path.Combine(
            Path.GetTempPath(),
            "AppServerHostResolutionWs_" + Guid.NewGuid().ToString("N")[..8]);

        public string BotPath { get; }

        public WorkspaceFixture()
        {
            Directory.CreateDirectory(WorkspacePath);
            BotPath = Path.Combine(WorkspacePath, ".craft");
            Directory.CreateDirectory(BotPath);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(WorkspacePath, recursive: true);
            }
            catch
            {
                // ignored
            }
        }
    }
}
