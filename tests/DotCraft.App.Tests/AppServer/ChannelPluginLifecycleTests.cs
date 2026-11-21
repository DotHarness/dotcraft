using System.Diagnostics;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Channels;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Modules;
using DotCraft.Plugins;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Tests.Sessions.Protocol.AppServer;
using DotCraft.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DotCraft.Tests.AppServer;

public sealed class ChannelPluginLifecycleTests
{
    [ChannelNodeFact]
    public async Task UpdateAndGlobalRemovalReconcileIndependentWorkspaceProcesses()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-channel-lifecycle-" + Guid.NewGuid().ToString("N"));
        var user = Path.Combine(root, "user");
        var installs = Path.Combine(user, "plugins");
        try
        {
            var source = ChannelPluginWorkspaceTests.CreatePlugin(Path.Combine(root, "channel"));
            var entry = Path.Combine(source, "launch.mjs");
            WriteLauncher(entry, "first");
            var deployer = new BuiltInPluginDeployer(installs, [source], new AppConfig.PluginsConfig { DisableDefaultPluginRegistry = true });
            var key = PluginIds.Key("channel-fixture", marketplaceName: null);
            Assert.Empty(deployer.DeployPlugin(key));

            await using var first = await TestHost.StartAsync(Path.Combine(root, "one"), user);
            await using var second = await TestHost.StartAsync(Path.Combine(root, "two"), user);
            Assert.Null(first.Runner.GetChannelStatuses().SingleOrDefault(c => c.Name == "fixture"));
            Assert.False(File.Exists(Path.Combine(first.CraftPath, "running.json")));
            await first.ConnectAsync();
            await second.ConnectAsync();

            var firstPid = await ReadRunAsync(first.CraftPath, "first");
            var secondPid = await ReadRunAsync(second.CraftPath, "first");
            Assert.NotEqual(firstPid, secondPid);

            WriteLauncher(entry, "updated");
            Assert.True(deployer.RefreshPlugin(key, []));
            PublishRevision(user);
            var updatedFirstPid = await ReadRunAsync(first.CraftPath, "updated");
            var updatedSecondPid = await ReadRunAsync(second.CraftPath, "updated");
            Assert.NotEqual(firstPid, updatedFirstPid);
            Assert.NotEqual(secondPid, updatedSecondPid);
            Assert.False(Running(firstPid));
            Assert.False(Running(secondPid));

            PluginDirectoryDeleter.Delete(Path.Combine(installs, "channel-fixture"), Path.Combine(user, "tmp"));
            PublishRevision(user);
            await WaitAsync(() => first.Runner.GetChannelStatuses().Single().FailureCode == "ChannelPluginNotInstalled");
            await WaitAsync(() => second.Runner.GetChannelStatuses().Single().FailureCode == "ChannelPluginNotInstalled");
            Assert.False(Running(updatedFirstPid));
            Assert.False(Running(updatedSecondPid));
            Assert.Contains("fixture-credential", File.ReadAllText(Path.Combine(first.CraftPath, "fixture.json")));

            Assert.Empty(deployer.DeployPlugin(key));
            PublishRevision(user);
            await WaitAsync(() => first.Runner.GetChannelStatuses().Single().FailureCode == "ChannelPluginActivationRequired");
            Assert.Equal("channel-fixture", first.Runner.GetChannelStatuses().Single().PluginId);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteLauncher(string path, string release) => File.WriteAllText(path, $$"""
        import {writeFileSync} from 'node:fs';
        import {join} from 'node:path';
        writeFileSync(join(process.env.DOTCRAFT_CHANNEL_CRAFT_PATH, 'running.json'),
            JSON.stringify({pid: process.pid, release: '{{release}}', endpoint: process.env.DOTCRAFT_CHANNEL_WS_URL}));
        setInterval(() => {}, 1000);
        """);

    private static void PublishRevision(string user) =>
        AtomicConfigDocument.Write(Path.Combine(user, "imports", "revision.json"), Guid.NewGuid().ToString("N"));

    private static async Task<int> ReadRunAsync(string craftPath, string release)
    {
        var pid = 0;
        await WaitAsync(() =>
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(craftPath, "running.json")));
                if (doc.RootElement.GetProperty("release").GetString() != release)
                    return false;
                Assert.Equal("ws://127.0.0.1:9133/ws", doc.RootElement.GetProperty("endpoint").GetString());
                pid = doc.RootElement.GetProperty("pid").GetInt32();
                return true;
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return false;
            }
        });
        return pid;
    }

    private static bool Running(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task WaitAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!predicate())
            await Task.Delay(50, timeout.Token);
    }

    private sealed class ChannelNodeFactAttribute : FactAttribute
    {
        public ChannelNodeFactAttribute()
        {
            if (!ChannelPluginLaunch.TryResolveNode(out _, out _))
                Skip = "Node.js 22 or later is needed to exercise an adapter process.";
        }
    }

    private sealed class TestHost(ChannelRunner runner, ServiceProvider services, ThreadStore threads, string craftPath) : IAsyncDisposable
    {
        public ChannelRunner Runner => runner;
        public string CraftPath => craftPath;

        public static async Task<TestHost> StartAsync(string workspace, string user)
        {
            var craftPath = Path.Combine(workspace, ".craft");
            Directory.CreateDirectory(craftPath);
            var config = AppConfigTestFactory.CreateOpenAI();
            config.Plugins.DisableDefaultPluginRegistry = true;
            config.SetSection("AppServer", new AppServerConfig
            {
                Mode = AppServerMode.WebSocket,
                WebSocket = new WebSocketServerConfig { Host = "0.0.0.0", Port = 9133 }
            });
            var modelProviders = new ModelProviderRegistry([new OpenAIClientProvider()]);
            var services = new ServiceCollection()
                .AddSingleton(modelProviders)
                .AddSingleton(new ChatClientRegistry(modelProviders))
                .AddSingleton(DotCraft.Commands.Core.CommandRegistry.CreateDefault(".craft"))
                .AddSingleton(new PathBlacklist([]))
                .AddSingleton(new ChannelHostRegistry())
                .AddSingleton<IChannelRuntimeRegistry>(new ChannelRuntimeRegistry())
                .AddSingleton(sp => new MessageRouter(sp.GetRequiredService<IChannelRuntimeRegistry>()))
                .AddSingleton(new WireRuntimeAdditionalContextProvider())
                .AddSingleton<IAppConfigMonitor>(new AppConfigMonitor(config))
                .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                .BuildServiceProvider();
            var runner = ChannelRunner.CreateForAppServer(services, config,
                DotCraftPaths.Create(workspace, craftPath, user), new ModuleRegistry());
            runner.BuildPoolThroughBuildAll();
            var threads = new ThreadStore(craftPath);
            runner.CompleteAfterSession(new TestableSessionService(threads));
            await runner.StartWebPoolAsync();
            runner.BeginChannelLoops(CancellationToken.None);
            return new TestHost(runner, services, threads, craftPath);
        }

        public async Task ConnectAsync()
        {
            var entry = new ChannelEntry { Name = "fixture", PluginId = "channel-fixture", Enabled = true };
            File.WriteAllText(Path.Combine(craftPath, "fixture.json"), """{"adapter":{"token":"fixture-credential"}}""");
            runner.ChannelPlugins.Authorize(entry);
            await runner.ApplyChannelSetEnabledAsync(entry, CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await runner.DisposeAsync();
            await threads.DisposeAsync();
            threads.StateDatabase.Dispose();
            await services.DisposeAsync();
        }
    }
}
