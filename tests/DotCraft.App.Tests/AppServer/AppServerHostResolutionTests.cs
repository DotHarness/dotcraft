using DotCraft.Workspaces;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Hosting;
using DotCraft.Runtime;
using DotCraft.Modules;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotCraft.Tests.AppServer;

public sealed class AppServerHostResolutionTests
{
    [Theory]
    [InlineData("started", DotCraft.Protocol.AppServer.AppServerMethodNames.TerminalStarted)]
    [InlineData("outputDelta", DotCraft.Protocol.AppServer.AppServerMethodNames.TerminalOutputDelta)]
    [InlineData("completed", DotCraft.Protocol.AppServer.AppServerMethodNames.TerminalCompleted)]
    [InlineData("stalled", DotCraft.Protocol.AppServer.AppServerMethodNames.TerminalStalled)]
    [InlineData("cleaned", DotCraft.Protocol.AppServer.AppServerMethodNames.TerminalCleaned)]
    public void BackgroundTerminalEvent_KnownType_UsesDeclaredNotification(
        string eventType,
        string expectedMethod)
    {
        Assert.Equal(expectedMethod, AppServerHost.ResolveBackgroundTerminalNotificationMethod(eventType));
    }

    [Fact]
    public void BackgroundTerminalEvent_UnknownType_DoesNotProduceNotification()
    {
        Assert.Null(AppServerHost.ResolveBackgroundTerminalNotificationMethod("future"));
    }

    [Fact]
    public async Task HostBuilder_BuildsAppServerHost_WithWorkspaceRuntimeRegistered()
    {
        using var fixture = new WorkspaceFixture();
        var config = new AppConfig();
        config.SetSection("AppServer", new AppServerConfig
        {
            Mode = AppServerMode.Stdio
        });

        var paths = new WorkspacePaths
        {
            WorkspacePath = fixture.WorkspacePath,
            CraftPath = fixture.BotPath
        };
        var registry = new ModuleRegistry();
        var hostFactories = new HostFactoryRegistry();
        ModuleRegistrations.RegisterAll(registry, hostFactories);

        var services = new ServiceCollection()
            .AddSingleton(registry)
            .AddSingleton<IConfigSchemaProvider>(ConfigSchemaRegistrations.CreateSchemaProvider())
            .AddDotCraftRuntime(new DotCraftRuntimeOptions
            {
                Config = config,
                WorkspacePath = fixture.WorkspacePath,
                CraftPath = fixture.BotPath
            });

        var builder = new HostBuilder(registry, hostFactories, config, paths, "app-server");
        var (provider, host) = builder.Build(services);

        await using var disposableProvider = (ServiceProvider)provider;
        Assert.IsType<AppServerHost>(host);
        Assert.NotNull(provider.GetRequiredService<WorkspaceRuntime>());
        Assert.NotNull(provider.GetRequiredService<WireRuntimeAdditionalContextProvider>());
        Assert.Contains(
            provider.GetServices<IThreadSystemPromptContextProvider>(),
            p => p.ContextPageKey == ContextPageKeys.RuntimeAdditionalContext());
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
