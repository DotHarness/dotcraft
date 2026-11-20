using System.Text.Json;
using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Hosting;
using DotCraft.Modules;
using DotCraft.Plugins;
using DotCraft.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DotCraft.Tests.AppServer;

[Collection("BuiltInPluginRootsEnvironment")]
public sealed class AppServerStartupPluginAutoUpdateTests
{
    [Fact]
    public async Task AppServerHost_StartupAutoUpdatesInstalledBuiltInPluginAndDesktopRevision()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-plugin-autoupdate-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "workspace");
        var workspaceCraftPath = Path.Combine(workspace, ".craft");
        var userCraftHome = Path.Combine(root, "user-craft");
        var bundledRoot = Path.Combine(root, "bundled");
        var installedPluginsRoot = Path.Combine(userCraftHome, "plugins");
        var previousBundledRoots = Environment.GetEnvironmentVariable(BuiltInPluginSourceResolver.EnvironmentVariableName);

        Directory.CreateDirectory(workspaceCraftPath);
        Directory.CreateDirectory(userCraftHome);

        WriteDesktopPlugin(Path.Combine(bundledRoot, "demo"), "demo", "v1", "export function activate() { return 1; }");

        var pluginsConfig = new AppConfig.PluginsConfig { DisableDefaultPluginRegistry = true };
        new BuiltInPluginDeployer(installedPluginsRoot, [bundledRoot], pluginsConfig, userCraftHome)
            .DeployPlugins(["demo@dotcraft-bundled"]);
        var v1DesktopRevision = PluginDesktopRevision.Compute(
            Path.Combine(installedPluginsRoot, "demo"),
            "./desktop/dist/index.mjs",
            []);

        WriteDesktopPlugin(Path.Combine(bundledRoot, "demo"), "demo", "v2", "export function activate() { return 2; }");

        await File.WriteAllTextAsync(Path.Combine(userCraftHome, "config.json"), """
            {
              "Plugins": { "DisableDefaultPluginRegistry": true }
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(workspaceCraftPath, "config.json"), """
            {
              "DashBoard": { "Enabled": false },
              "Automations": { "Enabled": false },
              "Dreams": { "Enabled": false },
              "AppServer": {
                "Mode": "WebSocket",
                "WebSocket": { "Host": "127.0.0.1", "Port": 0 }
              },
              "Plugins": {
                "DisableDefaultPluginRegistry": true
              }
            }
            """);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var hostCts = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        try
        {
            Environment.SetEnvironmentVariable(BuiltInPluginSourceResolver.EnvironmentVariableName, bundledRoot);

            var config = AppConfig.LoadWithGlobalFallback(
                Path.Combine(workspaceCraftPath, "config.json"),
                Path.Combine(userCraftHome, "config.json"));
            config.GlobalConfigPath = Path.Combine(userCraftHome, "config.json");
            config.ProviderId = "test-provider";
            config.ProviderPreferences["test-provider"] = new ModelPreference { Model = "test-model" };
            config.Providers["test-provider"] = new AppConfig.ModelProviderConfig
            {
                DisplayName = "Test Provider",
                Protocol = ModelProviderProtocols.OpenAI,
                ApiKey = "test-key",
                EndPoint = "https://127.0.0.1:9/v1"
            };

            var moduleRegistry = new ModuleRegistry();
            var hostFactories = new HostFactoryRegistry();
            ModuleRegistrations.RegisterAll(moduleRegistry, hostFactories);

            var services = new ServiceCollection()
                .AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance)
                .AddSingleton(moduleRegistry)
                .AddSingleton<IConfigSchemaProvider>(ConfigSchemaRegistrations.CreateSchemaProvider())
                .AddSingleton<IConfigDescriptorRegistry>(ConfigSchemaRegistrations.CreateDescriptorRegistry())
                .AddOpenAIModelProvider()
                .AddDotCraftRuntime(new DotCraftRuntimeOptions
                {
                    Config = config,
                    WorkspacePath = workspace,
                    DataPath = workspaceCraftPath,
                    UserDataPath = userCraftHome
                });

            var (provider, host) = new HostBuilder(moduleRegistry, hostFactories, config, "app-server").Build(services);
            await using var disposableProvider = (ServiceProvider)provider;
            await using var appServerHost = Assert.IsType<AppServerHost>(host);
            var hostRunTask = appServerHost.RunAsync(hostCts.Token);
            try
            {
                var endpoint = await WaitForWebSocketEndpointAsync(workspaceCraftPath, hostRunTask, deadline.Token);
                await using var client = await WebSocketClientConnection.ConnectAsync(endpoint, ct: deadline.Token);
                using (await client.Wire.InitializeAsync(ct: deadline.Token)) { }

                JsonElement? updatedDemo = null;

                while (!deadline.IsCancellationRequested)
                {
                    using var response = await client.Wire.SendRequestAsync(
                        "plugin/list",
                        new { includeDisabled = true },
                        ct: deadline.Token);
                    var demo = response.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray()
                        .Single(plugin =>
                            plugin.GetProperty("id").GetString() == "demo"
                            && plugin.GetProperty("installed").GetBoolean());
                    if (demo.GetProperty("displayName").GetString() == "v2")
                    {
                        updatedDemo = demo.Clone();
                        break;
                    }

                    await Task.Delay(50, deadline.Token);
                }

                Assert.NotNull(updatedDemo);
                var v2DesktopRevision = updatedDemo.Value.GetProperty("desktop").GetProperty("revision").GetString();
                Assert.False(string.IsNullOrWhiteSpace(v2DesktopRevision));
                Assert.NotEqual(v1DesktopRevision, v2DesktopRevision);
                Assert.Equal("export function activate() { return 2; }",
                    await File.ReadAllTextAsync(Path.Combine(installedPluginsRoot, "demo", "desktop", "dist", "index.mjs")));
            }
            finally
            {
                hostCts.Cancel();
                try { await hostRunTask.WaitAsync(TimeSpan.FromSeconds(10)); } catch (OperationCanceledException) { }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(BuiltInPluginSourceResolver.EnvironmentVariableName, previousBundledRoots);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task<Uri> WaitForWebSocketEndpointAsync(
        string workspaceCraftPath,
        Task hostRunTask,
        CancellationToken ct)
    {
        var lockPath = AppServerWorkspaceLock.GetLockFilePath(workspaceCraftPath);
        while (!ct.IsCancellationRequested)
        {
            if (hostRunTask.IsFaulted)
                await hostRunTask;

            var info = AppServerWorkspaceLock.TryRead(lockPath);
            if (info?.Endpoints.TryGetValue("appServerWebSocket", out var address) == true
                && !string.IsNullOrWhiteSpace(address))
            {
                return new Uri(address);
            }

            await Task.Delay(50, ct);
        }

        throw new TimeoutException("Timed out waiting for AppServer WebSocket endpoint.");
    }

    private static void WriteDesktopPlugin(string pluginRoot, string id, string displayName, string script)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "desktop", "dist"));
        File.WriteAllText(Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"), $$"""
            {
              "schemaVersion": 1,
              "id": "{{id}}",
              "version": "1.0.0",
              "displayName": "{{displayName}}",
              "description": "Desktop plugin fixture.",
              "desktop": {
                "entry": "./desktop/dist/index.mjs"
              },
              "interface": { "displayName": "{{displayName}}" }
            }
            """);
        File.WriteAllText(Path.Combine(pluginRoot, "desktop", "dist", "index.mjs"), script);
    }
}
