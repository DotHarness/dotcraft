using DotCraft.Context.Compaction;
using DotCraft.Contributions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Contributions;

public sealed class CompactableToolPolicyContributionTests
{
    private const string PluginTool = "acme__review";
    private const string ThreadId = "thread-a";

    [Fact]
    public void WithoutContributions_TheBuiltInAllowListStillGoverns()
    {
        Assert.True(CompactableToolPolicyCatalog.IsCompactable(null, null, "ReadFile"));
        Assert.True(CompactableToolPolicyCatalog.IsCompactable(null, null, "mcp__files__read"));
        Assert.False(CompactableToolPolicyCatalog.IsCompactable(null, null, PluginTool));
        Assert.True(CompactableToolPolicyCatalog.IsCompactable(new ContributionRegistry(), null, "ReadFile"));
        Assert.False(CompactableToolPolicyCatalog.IsCompactable(new ContributionRegistry(), null, PluginTool));
    }

    [Fact]
    public void ContributedPolicy_MakesAPluginToolResultPrunable()
    {
        var registry = CreateRegistry();
        registry.Add<ICompactableToolPolicy>(new FixedPolicy("scoped", "acme__other", true), ContributionOptions.ForThread(ThreadId));
        registry.Add<ICompactableToolPolicy>(new FixedPolicy("acme", PluginTool, true));

        Assert.True(CompactableToolPolicyCatalog.IsCompactable(registry, null, PluginTool));
        Assert.True(CompactableToolPolicyCatalog.IsCompactable(registry, ThreadId, "acme__other"));
        Assert.False(CompactableToolPolicyCatalog.IsCompactable(registry, "thread-b", "acme__other"));
        Assert.True(CompactableToolPolicyCatalog.IsCompactable(registry, null, "ReadFile"));
    }

    [Fact]
    public void TheFirstOpinionWins_DeclinesDeferAndADenialOverridesTheBuiltIn()
    {
        var registry = CreateRegistry();
        registry.Add<ICompactableToolPolicy>(
            new FixedPolicy("declines", PluginTool, null),
            new ContributionOptions(Order: 10));
        registry.Add<ICompactableToolPolicy>(
            new FixedPolicy("first", PluginTool, true),
            new ContributionOptions(Order: 20));
        registry.Add<ICompactableToolPolicy>(
            new FixedPolicy("second", PluginTool, false),
            new ContributionOptions(Order: 30));
        registry.Add<ICompactableToolPolicy>(
            new FixedPolicy("deny", "ReadFile", false),
            new ContributionOptions(Order: 10));

        Assert.True(CompactableToolPolicyCatalog.IsCompactable(registry, null, PluginTool));
        Assert.False(CompactableToolPolicyCatalog.IsCompactable(registry, null, "ReadFile"));
    }

    [Fact]
    public void ReplacingTheBuiltIn_ShadowsItAndDisposalRestoresIt()
    {
        var registry = CreateRegistry();
        var handle = registry.Add<ICompactableToolPolicy>(
            new FixedPolicy("replacement", PluginTool, true),
            new ContributionOptions(Order: 200, ReplaceTarget: CompactableToolPolicyCatalog.BuiltInTargetName));

        Assert.True(CompactableToolPolicyCatalog.IsCompactable(registry, null, PluginTool));
        Assert.False(CompactableToolPolicyCatalog.IsCompactable(registry, null, "ReadFile"));

        handle.Dispose();

        Assert.True(CompactableToolPolicyCatalog.IsCompactable(registry, null, "ReadFile"));
        Assert.False(CompactableToolPolicyCatalog.IsCompactable(registry, null, PluginTool));
    }

    [Fact]
    public void APluginToolResult_IsClearedByTheMicroCompactPassOnlyWithAPolicy()
    {
        var registry = CreateRegistry();
        var messages = Conversation();

        Assert.Equal(MicroCompactTrigger.None, Run(registry, messages).Trigger);

        registry.Add<ICompactableToolPolicy>(new FixedPolicy("acme", PluginTool, true));
        var result = Run(registry, messages);

        Assert.Equal(MicroCompactTrigger.TimeBased, result.Trigger);
        Assert.Equal(2, result.ClearedCount);
    }

    private static MicroCompactResult Run(IContributionView contributions, IReadOnlyList<ChatMessage> messages) =>
        new MicroCompactor(
                new CompactionConfig
                {
                    MicrocompactEnabled = true,
                    MicrocompactKeepRecent = 2,
                    MicrocompactGapMinutes = 1
                },
                contributions)
            .Run(messages, DateTimeOffset.UtcNow.AddMinutes(-5));

    private static List<ChatMessage> Conversation()
    {
        var messages = new List<ChatMessage>();
        for (var index = 0; index < 4; index++)
        {
            var callId = $"call-{index}";
            messages.Add(new ChatMessage(
                ChatRole.Assistant,
                new List<AIContent> { new FunctionCallContent(callId, PluginTool, new Dictionary<string, object?>()) }));
            messages.Add(new ChatMessage(
                ChatRole.User,
                new List<AIContent> { new FunctionResultContent(callId, $"body-{index}") }));
        }

        return messages;
    }

    private static ContributionRegistry CreateRegistry()
    {
        var registry = new ContributionRegistry();
        CompactableToolPolicyCatalog.RegisterBuiltIns(registry);
        return registry;
    }

    private sealed class FixedPolicy(string name, string toolName, bool? opinion) : ICompactableToolPolicy
    {
        public string Name => name;

        public bool? IsCompactable(string tool) =>
            string.Equals(tool, toolName, StringComparison.Ordinal) ? opinion : null;
    }
}
