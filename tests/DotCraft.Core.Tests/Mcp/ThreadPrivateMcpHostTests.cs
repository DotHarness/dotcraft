using System.Diagnostics;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.McpTestServer;
using DotCraft.Modules;
using DotCraft.Runtime;
using DotCraft.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotCraft.Tests.Mcp;

public sealed class ThreadPrivateMcpHostTests
{
    [Fact]
    public async Task WorkspaceStop_ReleasesInheritedThreadProcessAndForwardsScopedStatus()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcp-host-{Guid.NewGuid():N}");
        try
        {
            var config = AppConfigTestFactory.CreateOpenAI();
            config.McpServers = [new()
            {
                Name = "state", Command = "dotnet", Arguments = [McpFixtureProcess.AssemblyPath, "stateful"]
            }];
            var services = new ServiceCollection();
            services.AddOpenAIModelProvider();
            services.AddDotCraftRuntime(new DotCraftRuntimeOptions
            {
                Config = config, WorkspacePath = root, DataPath = Path.Combine(root, ".craft")
            });
            services.AddSingleton<IConfigDescriptorRegistry>(new Descriptors());
            services.AddSingleton<IConfigSchemaProvider>(new Descriptors());
            await using var provider = services.BuildServiceProvider();
            var runtime = provider.GetRequiredService<WorkspaceRuntime>();
            await runtime.StartAsync(new ModuleRegistry());
            var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            runtime.ThreadMcpStatusChanged += (id, status) =>
            {
                if (status.StartupState == "ready") ready.TrySetResult(id);
            };
            var thread = await runtime.Sessions.CreateThreadAsync(new SessionIdentity
            {
                ChannelName = "test", UserId = "user", WorkspacePath = root
            });
            Assert.Equal(thread.Id, await ready.Task.WaitAsync(TimeSpan.FromSeconds(15)));
            var manager = (await ((IThreadMcpRuntimeService)runtime.Sessions).GetEffectiveMcpRuntimeAsync(thread.Id))!;
            Assert.NotSame(runtime.McpClientManager, manager);
            var state = await manager.CallToolAsync("state", "state");
            var pid = JsonSerializer.SerializeToElement(state.StructuredContent).GetProperty("pid").GetInt32();
            using var process = Process.GetProcessById(pid);

            await runtime.StopAsync();

            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(runtime.IsStarted);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class Descriptors : IConfigDescriptorRegistry, IConfigSchemaProvider
    {
        public IReadOnlyList<ConfigFieldDescriptor> Fields => [];
        public IReadOnlyList<ConfigSchemaSection> GetConfigSchema() => [];
    }
}
