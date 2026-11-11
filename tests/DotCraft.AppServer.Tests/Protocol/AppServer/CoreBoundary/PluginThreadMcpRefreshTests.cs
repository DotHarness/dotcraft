using System.Diagnostics;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Mcp;
using DotCraft.McpTestServer;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Plugins;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using Xunit;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class PluginThreadMcpRefreshTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"plugin-thread-mcp-{Guid.NewGuid():N}");
    private string CraftPath => Path.Combine(_root, ".craft");

    [Fact]
    public async Task Remove_RefreshesLoadedThreadToolsAndReleasesPluginProcesses()
    {
        var pluginRoot = Path.Combine(CraftPath, "plugins", "fixture");
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"), """
            {"schemaVersion":1,"id":"fixture","version":"1.0.0","displayName":"Fixture","capabilities":["mcp"],"mcpServers":"./.mcp.json"}
            """);
        File.WriteAllText(Path.Combine(pluginRoot, ".mcp.json"), JsonSerializer.Serialize(new
        {
            mcpServers = new
            {
                state = new { command = "dotnet", arguments = new[] { McpFixtureProcess.AssemblyPath, "stateful" }, cwd = _root }
            }
        }));
        var config = AppConfigTestFactory.CreateOpenAI();
        config.GlobalConfigPath = Path.Combine(_root, "user-data", "config.json");
        config.Tools.DeferredLoading.Strategy = AppConfig.DeferredLoadingStrategy.Off;
        await using var workspace = new McpClientManager();
        var servers = PluginMcpServerResolver.LoadEffectiveServers(config, _root, CraftPath, out _);
        var server = Assert.Single(servers);
        await workspace.ConnectAsync(servers);
        var skills = new SkillsLoader(CraftPath);
        var memory = new MemoryStore(CraftPath);
        var registry = TestModelProviderRegistry.Create();
        var approval = new AutoApproveApprovalService();
        await using var factory = new AgentFactory(CraftPath, _root, config, memory, skills, approval, null,
            runtimeContext: new AgentRuntimeContext
            {
                Config = config, WorkspacePath = _root, BotPath = CraftPath,
                MemoryStore = memory, SkillsLoader = skills, ContextPageManager = new ContextPageManager(),
                ApprovalService = approval, ChatClientRegistry = registry, McpClientManager = workspace
            }, chatClientRegistry: registry);
        await using var store = new ThreadStore(CraftPath);
        await using var sessions = new SessionService(factory, null, new SessionPersistenceService(store), new SessionGate());
        var threads = new List<SessionThread>();
        var processes = new List<Process>();
        try
        {
            for (var index = 0; index < 2; index++)
            {
                var thread = await sessions.CreateThreadAsync(new SessionIdentity
                {
                    WorkspacePath = _root, ChannelName = "appserver", UserId = "test"
                });
                threads.Add(thread);
                var manager = (await sessions.GetEffectiveMcpRuntimeAsync(thread.Id))!;
                var result = await manager.CallToolAsync(server.Name, "state");
                var pid = JsonSerializer.SerializeToElement(result.StructuredContent).GetProperty("pid").GetInt32();
                processes.Add(Process.GetProcessById(pid));
            }
            var connection = new AppServerConnection();
            connection.TryMarkInitialized(new ClientConnectionInfo { Name = "test", Version = "1" },
                new ClientConnectionCapabilities());
            connection.MarkClientReady();
            await using var transport = new InMemoryTransport();
            var handler = new AppServerRequestHandler(sessions, connection, transport,
                new ModuleRegistryChannelListContributor(new ModuleRegistry()), new AppServerConnectionServices
                {
                    ServerVersion = "test", WorkspaceCraftPath = CraftPath, HostWorkspacePath = _root,
                    SkillsLoader = skills, MemoryStore = memory, AppConfigMonitor = new AppConfigMonitor(config),
                    McpClientManager = workspace
                });
            var request = InMemoryTransport.BuildRequest(Contract.AppServerMethodNames.PluginRemove, new { id = "fixture" }, 1);
            var response = await handler.HandleRequestAsync(request, default);
            if (response != null)
                await transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(request.Id, response));
            using var sent = await transport.ReadNextSentAsync();
            AppServerTestHarness.AssertIsSuccessResponse(sent);
            Assert.Equal("applied", sent.RootElement.GetProperty("result").GetProperty("outcome").GetString());
            Assert.False(Directory.Exists(pluginRoot));
            foreach (var process in processes)
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            foreach (var thread in threads)
            {
                var snapshot = await sessions.GetEffectiveToolSnapshotAsync(thread.Id);
                Assert.DoesNotContain(snapshot.Registrations.Values, tool => tool.Definition.Id.Kind == DotCraft.Tools.ToolSourceKind.Mcp);
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
