using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceNativeCompactionTests
{
    [Theory]
    [InlineData(true, 828_400, 784_800)]
    [InlineData(false, 902_000, 889_000)]
    public async Task SubscriptionBudget_CompactsAn800kWindowBeforeNextSampling(
        bool oauth, int usableWindow, int autoThreshold)
    {
        var craftPath = Path.Combine(_workspace, ".craft");
        Directory.CreateDirectory(craftPath);
        File.WriteAllText(Path.Combine(craftPath, "models.json"), """
            { "models": { "gpt-test": { "contextWindow": 1050000, "maxInputTokens": 922000 } } }
            """);
        _provider.ContextBudget = new ProviderContextBudget(872_000);
        await using var factory = CreateFactory(oauth: oauth, configure: config =>
        {
            config.Compaction.MaxContextWindow = 1_000_000;
            ModelCatalog.ApplyToConfig(config, globalConfigPath: null,
                workspaceConfigPath: Path.Combine(craftPath, "config.json"));
        });
        var service = CreateService(factory);
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _workspace, ChannelName = "test", UserId = "user"
        });

        _provider.InputTokensNextResponse = 799_990;
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("initial question")]));
        var usage = service.TryGetContextUsageSnapshot(thread.Id);
        Assert.NotNull(usage);
        Assert.Equal(800_000L, usage.Tokens);
        Assert.Equal(usableWindow, usage.ContextWindow);
        Assert.Equal(autoThreshold, usage.AutoCompactThreshold);
        Assert.Empty(_provider.Compactions);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("continue the conversation")]));
        var completed = await service.GetThreadAsync(thread.Id);
        Assert.Equal(TurnStatus.Completed, completed.Turns[^1].Status);
        if (oauth)
        {
            Assert.Single(_provider.Compactions);
            Assert.Contains("encrypted-test", _provider.Requests[^1]);
        }
        else
        {
            Assert.Empty(_provider.Compactions);
        }
    }
}
