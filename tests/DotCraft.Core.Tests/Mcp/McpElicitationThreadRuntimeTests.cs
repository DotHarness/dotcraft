using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Persistence;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Mcp;

public sealed partial class McpElicitationInvocationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThreadAndBindingManagers_ForwardInvocationToWorkspaceHandler(bool binding)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcp-input-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var handler = new InputHandler();
            await using var workspaceManager = new McpClientManager(elicitationHandler: handler);
            var config = AppConfigTestFactory.CreateOpenAI();
            var memory = new MemoryStore(root);
            var skills = new SkillsLoader(root);
            var approval = new AutoApproveApprovalService();
            var registry = TestModelProviderRegistry.Create();
            await using var factory = new AgentFactory(root, root, config, memory, skills, approval, null,
                runtimeContext: new AgentRuntimeContext
                {
                    Config = config, WorkspacePath = root, BotPath = root, MemoryStore = memory,
                    SkillsLoader = skills, ContextPageManager = new ContextPageManager(),
                    ApprovalService = approval, ChatClientRegistry = registry, McpClientManager = workspaceManager
                }, chatClientRegistry: registry);
            using var database = new WorkspaceStateDatabase(root);
            await using var store = new ThreadStore(root, database);
            var sessions = new SessionService(factory, factory.CreateAgentForMode(AgentMode.Agent),
                new SessionPersistenceService(store), new SessionGate());
            var thread = await sessions.CreateThreadAsync(
                new SessionIdentity { ChannelName = "test", UserId = "user", WorkspacePath = root },
                new ThreadConfiguration
                {
                    Mode = "agent", ApprovalPolicy = ApprovalPolicy.AutoApprove,
                    McpServers = binding ? [] : [Config(null)]
                });
            try
            {
                if (binding)
                    await sessions.SetBindingMcpServersAsync(thread.Id, "fixture-binding", [Config(null)]);
                var manager = (await sessions.GetEffectiveMcpRuntimeAsync(thread.Id))!;
                var server = Assert.Single(await manager.ListConfigsAsync());
                Assert.Equal(binding, server.Origin.IsBinding);
                var generation = (await manager.GetGenerationAsync(server.Name))!.Value;
                var runtime = new McpToolRuntime(manager, server.Name, "choose", generation);
                var call = runtime.InvokeAsync(Context(thread.Id, null) with { CallId = "host-call" },
                    new System.Text.Json.Nodes.JsonObject { ["owner"] = thread.Id }).AsTask();
                var inputTask = handler.ReadAsync();
                if (await Task.WhenAny(inputTask, call) == call)
                {
                    var completed = await call;
                    Assert.Fail($"Tool completed before asking for input: {completed.Error?.Code}: {completed.Error?.Message}");
                }
                var input = await inputTask;
                Assert.Equal(thread.Id, input.Context.ThreadId);
                Assert.Null(input.Context.TurnId);
                Assert.Equal("host-call", input.Context.CallId);
                Assert.Equal(binding ? "binding:fixture-binding:inputs" : "inputs", input.ServerName);
                input.Answer("from-host");
                var result = await call.WaitAsync(Deadline);
                Assert.True(result.Success, result.Error?.Message);
                Assert.Equal("from-host", result.StructuredContent!.Value.GetProperty("answers")[0].GetString());
            }
            finally
            {
                await sessions.DeleteThreadPermanentlyAsync(thread.Id);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
