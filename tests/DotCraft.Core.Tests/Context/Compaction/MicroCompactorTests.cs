using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Context.Compaction;

public sealed class MicroCompactorTests
{
    private static CompactionConfig Config(
        int keepRecent = 2,
        int gapMinutes = 0,
        bool enabled = true) => new()
    {
        MicrocompactEnabled = enabled,
        MicrocompactKeepRecent = keepRecent,
        MicrocompactGapMinutes = gapMinutes,
    };

    private static ChatMessage AssistantCall(string callId, string toolName = "ReadFile")
    {
        return new ChatMessage(ChatRole.Assistant, new List<AIContent>
        {
            new FunctionCallContent(callId, toolName, new Dictionary<string, object?>()),
        });
    }

    private static ChatMessage ToolResult(string callId, string payload = "payload")
    {
        return new ChatMessage(ChatRole.User, new List<AIContent>
        {
            new FunctionResultContent(callId, payload),
        });
    }

    [Fact]
    public void Run_WithoutAnIdleGapOrWhenDisabled_ReturnsUnchanged()
    {
        var messages = new List<ChatMessage>();
        for (var i = 0; i < 4; i++)
        {
            var id = $"c{i}";
            messages.Add(AssistantCall(id));
            messages.Add(ToolResult(id, payload: $"body-{i}"));
        }

        var countOnly = new MicroCompactor(Config(keepRecent: 2, gapMinutes: 0)).Run(messages);
        var disabled = new MicroCompactor(Config(keepRecent: 0, enabled: false)).Run(messages);

        Assert.Equal(MicroCompactTrigger.None, countOnly.Trigger);
        Assert.Equal(0, countOnly.ClearedCount);
        Assert.Same(messages, countOnly.Messages);
        Assert.Equal(MicroCompactTrigger.None, disabled.Trigger);
    }

    [Fact]
    public void Run_TimeTrigger_ClearsCompactableResultsOverKeepRecentOnly()
    {
        var micro = new MicroCompactor(Config(keepRecent: 2, gapMinutes: 5));
        var messages = new List<ChatMessage>();
        for (var i = 0; i < 4; i++)
        {
            var id = $"c{i}";
            messages.Add(AssistantCall(id));
            messages.Add(ToolResult(id));
        }
        messages.Add(AssistantCall("unknown", "SomeUnknownTool"));
        messages.Add(ToolResult("unknown", "keep-me"));

        var result = micro.Run(messages, DateTimeOffset.UtcNow.AddMinutes(-10));

        Assert.Equal(MicroCompactTrigger.TimeBased, result.Trigger);
        Assert.Equal(2, result.ClearedCount);
        Assert.Equal("keep-me", ((FunctionResultContent)result.Messages[^1].Contents[0]).Result as string);
        Assert.True(CompactableToolNames.IsCompactable("mcp__svr__tool"));
    }

    [Fact]
    public void Run_AlreadyClearedResultsDoNotRetrigger()
    {
        var micro = new MicroCompactor(Config(keepRecent: 1, gapMinutes: 5));
        var messages = new List<ChatMessage>
        {
            AssistantCall("c1"),
            ToolResult("c1", CompactableToolNames.ClearedResultMarker),
            AssistantCall("c2"),
            ToolResult("c2", "body-2"),
        };

        var result = micro.Run(messages, DateTimeOffset.UtcNow.AddMinutes(-10));

        Assert.Equal(MicroCompactTrigger.None, result.Trigger);
        Assert.Equal(0, result.ClearedCount);
        Assert.Same(messages, result.Messages);
    }
}
