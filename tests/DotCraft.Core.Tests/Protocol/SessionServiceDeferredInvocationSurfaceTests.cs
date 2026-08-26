using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Persistence;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceDeferredInvocationSurfaceTests : IDisposable
{
    private readonly string _tempDir;

    public SessionServiceDeferredInvocationSurfaceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DeferredInvocation_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Theory]
    [InlineData(ModelProviderProtocols.OpenAIResponses, AppConfig.DeferredLoadingStrategy.Native, true)]
    [InlineData(ModelProviderProtocols.Anthropic, AppConfig.DeferredLoadingStrategy.Native, false)]
    [InlineData(ModelProviderProtocols.OpenAIResponses, AppConfig.DeferredLoadingStrategy.Simulated, false)]
    public async Task SetThreadModeAsync_DeferredInvocationSurfaceMatchesProviderContract(
        string protocol,
        AppConfig.DeferredLoadingStrategy strategy,
        bool exposesAllDeferredLocally)
    {
        var config = string.Equals(protocol, ModelProviderProtocols.Anthropic, StringComparison.Ordinal)
            ? AppConfigTestFactory.CreateAnthropic()
            : AppConfigTestFactory.CreateOpenAI();
        config.Providers[config.ProviderId].Protocol = protocol;
        config.Tools.DeferredLoading.Strategy = strategy;

        await using var agentFactory = CreateAgentFactory(config, [new DeferredTestToolSource()]);
        using var stateDatabase = new WorkspaceStateDatabase(_tempDir);
        await using var threadStore = new ThreadStore(_tempDir, stateDatabase);
        var service = CreateSessionService(agentFactory, threadStore);
        var thread = await CreateThreadAsync(service);

        AssertDeferredInvocationSurface(
            GetCachedThreadAgent(service, thread.Id),
            agentFactory,
            exposesAllDeferredLocally);

        await service.SetThreadModeAsync(thread.Id, "agent");

        AssertDeferredInvocationSurface(
            GetCachedThreadAgent(service, thread.Id),
            agentFactory,
            exposesAllDeferredLocally);
    }

    [Fact]
    public async Task SetThreadModeAsync_OpenAIResponsesNativeDoesNotRetainRemovedDeferredTool()
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        config.Providers[config.ProviderId].Protocol = ModelProviderProtocols.OpenAIResponses;
        config.Tools.DeferredLoading.Strategy = AppConfig.DeferredLoadingStrategy.Native;

        await using var agentFactory = CreateAgentFactory(
            config,
            [new DeferredTestToolSource(requiredMode: "plan")]);
        using var stateDatabase = new WorkspaceStateDatabase(_tempDir);
        await using var threadStore = new ThreadStore(_tempDir, stateDatabase);
        var service = CreateSessionService(agentFactory, threadStore);
        var thread = await CreateThreadAsync(service);

        AssertDeferredInvocationSurface(GetCachedThreadAgent(service, thread.Id), agentFactory, expected: true);

        await service.SetThreadModeAsync(thread.Id, "agent");

        var invoking = Assert.IsType<StreamingFunctionInvokingChatClient>(
            GetCachedThreadAgent(service, thread.Id).ChatClient.GetService(
                typeof(StreamingFunctionInvokingChatClient)));
        var createdTools = Assert.IsAssignableFrom<IReadOnlyList<AITool>>(agentFactory.LastCreatedTools);
        Assert.Empty(invoking.AdditionalTools ?? []);
        Assert.DoesNotContain(createdTools, static tool => tool.Name == "fixture__Lookup");
        Assert.DoesNotContain(createdTools, static tool => tool.Name == "SearchTools");
    }

    private static SessionService CreateSessionService(AgentFactory agentFactory, ThreadStore threadStore) =>
        new(
            agentFactory,
            agentFactory.CreateAgentForMode(AgentMode.Agent),
            new SessionPersistenceService(threadStore),
            new SessionGate());

    private async Task<SessionThread> CreateThreadAsync(SessionService service)
    {
        var thread = await service.CreateThreadAsync(
            new SessionIdentity { ChannelName = "test", UserId = "u", WorkspacePath = _tempDir },
            new ThreadConfiguration { Mode = "plan" });
        await service.RefreshThreadAgentAsync(thread.Id);
        return thread;
    }

    private AgentFactory CreateAgentFactory(AppConfig config, IEnumerable<IToolSource> toolSources)
    {
        var memory = new MemoryStore(_tempDir);
        var skills = new SkillsLoader(_tempDir);
        return new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: memory,
            skillsLoader: skills,
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            toolSources: toolSources);
    }

    private static ChatClientAgent GetCachedThreadAgent(SessionService service, string threadId)
    {
        var runtime = service.DebugGetRuntime(threadId);
        Assert.NotNull(runtime);
        Assert.NotNull(runtime!.Agent);
        return runtime.Agent;
    }

    private static void AssertDeferredInvocationSurface(
        ChatClientAgent agent,
        AgentFactory agentFactory,
        bool expected)
    {
        var invoking = Assert.IsType<StreamingFunctionInvokingChatClient>(
            agent.ChatClient.GetService(typeof(StreamingFunctionInvokingChatClient)));
        var createdTools = Assert.IsAssignableFrom<IReadOnlyList<AITool>>(agentFactory.LastCreatedTools);
        var deferred = invoking.AdditionalTools?.Where(static tool => tool.Name == "fixture__Lookup").ToArray()
            ?? [];
        Assert.Equal(expected ? 1 : 0, deferred.Length);
        Assert.Contains(createdTools, static tool => tool.Name == "SearchTools");
        Assert.DoesNotContain(createdTools, static tool => tool.Name == "fixture__Lookup");
    }

    private sealed class DeferredTestToolSource(string? requiredMode = null) : IToolSource
    {
        public string SourceId => "deferred-test";

        public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
            ToolPlanningContext context,
            CancellationToken cancellationToken = default)
        {
            if (requiredMode != null && !string.Equals(requiredMode, context.Mode, StringComparison.Ordinal))
                return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([]);

            var sourceToolId = new SourceToolId("Lookup");
            var definitionId = new ToolDefinitionId(ToolSourceKind.CoreNative, SourceId, sourceToolId);
            var definition = new ToolDefinition(
                definitionId,
                new ToolName("fixture", "Lookup"),
                "Look up a fixture.",
                JsonSerializer.SerializeToElement(new { type = "object" }),
                provenance: new ToolProvenance(ToolSourceKind.CoreNative, SourceId));
            var binding = new ToolRuntimeBinding(
                new RuntimeBindingId($"deferred-test:{context.Revision}"),
                definitionId,
                new DeferredTestRuntime(),
                ToolBindingLeases.AlwaysAvailable,
                "test:deferred",
                context.Revision);
            return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([
                new ToolRegistration(
                    definition,
                    binding,
                    ToolProjectionShape.StandardPair,
                    ToolExposure.Deferred,
                    ToolInvocationAudience.Model,
                    new DeferredToolDescriptor("fixture", "Search fixture tools."))
            ]);
        }
    }

    private sealed class DeferredTestRuntime : IToolRuntime
    {
        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolExecutionResult.Succeeded("ok"));
    }
}
