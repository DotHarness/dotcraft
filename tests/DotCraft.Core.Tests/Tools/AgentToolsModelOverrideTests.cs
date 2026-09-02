using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using TestableSessionService = DotCraft.Tests.Sessions.Protocol.AppServer.CoreTestableSessionService;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class AgentToolsModelOverrideTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("agent_model_overrides_");
    private readonly TestableSessionService _sessionService;

    public AgentToolsModelOverrideTests()
    {
        _sessionService = new TestableSessionService(new ThreadStore(_directory.FullName));
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("all", null, null)]
    [InlineData("all", "", "")]
    [InlineData(null, "", null)]
    [InlineData("all", null, "")]
    [InlineData(" all ", " \t", "\r\n")]
    public async Task SpawnAgent_FullHistoryWithBlankOverrides_InheritsParentPreference(
        string? forkTurns, string? model, string? reasoningEffort)
    {
        var context = await CreateContextAsync();
        using var scope = SubAgentSessionScope.Set(context);
        var function = AIFunctionFactory.Create(new AgentTools().SpawnAgent);

        await function.InvokeAsync(new AIFunctionArguments
        {
            ["message"] = "inspect code",
            ["taskName"] = "inspect",
            ["forkTurns"] = forkTurns,
            ["model"] = model,
            ["reasoningEffort"] = reasoningEffort
        });

        var child = await GetChildAsync(context);
        Assert.Equal("parent-model", child.Configuration?.Model);
        Assert.Equal(ModelReasoningEffort.High, child.Configuration?.Reasoning?.Effort);
        Assert.True(child.Configuration?.Reasoning?.Enabled);
    }

    [Theory]
    [InlineData(null, "child-model", null)]
    [InlineData("all", null, "low")]
    [InlineData("all", "child-model", "low")]
    public async Task SpawnAgent_FullHistoryWithExplicitOverrides_DoesNotCreateChild(
        string? forkTurns, string? model, string? reasoningEffort)
    {
        var context = await CreateContextAsync();
        using var scope = SubAgentSessionScope.Set(context);
        var createdChildren = 0;
        _sessionService.CreateThreadHandler = (_, _) =>
        {
            createdChildren++;
            return Task.CompletedTask;
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentTools().SpawnAgent(
            "inspect code", "inspect", forkTurns: forkTurns, model: model, reasoningEffort: reasoningEffort));

        Assert.Equal(0, createdChildren);
        Assert.Empty(await _sessionService.ListSubAgentChildrenAsync(context.ParentThread.Id, includeClosed: true));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("1")]
    public async Task SpawnAgent_FreshOrBoundedHistory_AppliesExplicitOverrides(string forkTurns)
    {
        var context = await CreateContextAsync();
        using var scope = SubAgentSessionScope.Set(context);
        var tools = new AgentTools(
            appConfig: AppConfigTestFactory.CreateOpenAI(model: "parent-model"),
            modelCatalogSnapshot: new SubAgentModelCatalogSnapshot
            {
                ProviderId = "openai",
                Models =
                [
                    new SubAgentModelCatalogItem
                    {
                        Id = "child-model",
                        SupportedReasoningEfforts = [ModelReasoningEffort.Low]
                    }
                ]
            });

        await tools.SpawnAgent("inspect code", "inspect", forkTurns: forkTurns,
            model: "child-model", reasoningEffort: "low");

        var child = await GetChildAsync(context);
        Assert.Equal("child-model", child.Configuration?.Model);
        Assert.Equal(ModelReasoningEffort.Low, child.Configuration?.Reasoning?.Effort);
    }

    private async Task<SubAgentSessionContext> CreateContextAsync()
    {
        var parent = await _sessionService.CreateThreadAsync(
            new SessionIdentity { WorkspacePath = _directory.FullName, UserId = "user", ChannelName = "desktop" },
            new ThreadConfiguration
            {
                ProviderId = "openai",
                Model = "parent-model",
                Reasoning = new AppConfig.ReasoningConfig { Enabled = true, Effort = ModelReasoningEffort.High }
            });
        return new SubAgentSessionContext
        {
            SessionService = _sessionService,
            ParentThread = parent,
            ParentTurnId = "turn_parent",
            RootThreadId = parent.Id,
            Depth = 0
        };
    }

    private async Task<SessionThread> GetChildAsync(SubAgentSessionContext context)
    {
        var edge = Assert.Single(await _sessionService.ListSubAgentChildrenAsync(context.ParentThread.Id, includeClosed: true));
        await SubAgentSessionControl.WaitAgentAsync(_sessionService, edge.ChildThreadId, timeoutSeconds: 5, CancellationToken.None);
        return await _sessionService.GetThreadAsync(edge.ChildThreadId);
    }

    public void Dispose()
    {
        try
        {
            _directory.Delete(recursive: true);
        }
        catch (IOException)
        {
            // SQLite pooled connections can outlive the test fixture on Windows.
        }
    }
}
