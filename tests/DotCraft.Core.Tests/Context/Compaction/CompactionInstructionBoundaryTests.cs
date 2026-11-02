using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Context.Compaction;

public sealed class CompactionInstructionBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstructionsRemainInReplayButNotReplacement(bool full)
    {
        using var client = new CaptureClient();
        var messages = new List<ChatMessage>
        {
            AgentInstructionsHistory.Create("original workspace instructions"),
            new(ChatRole.User, "old task"),
            new(ChatRole.Assistant, "old answer"),
            AgentInstructionsHistory.Create("additional workspace instructions"),
            new(ChatRole.User, "recent task")
        };
        var snapshot = PromptRequestSnapshot.Capture(messages, new ChatOptions { Instructions = "original system" });
        messages.Add(new ChatMessage(ChatRole.Assistant, "recent answer"));
        if (full)
            Assert.NotNull((await new FullCompactor(client, new MaintenanceForkRunner(client))
                .CompactAsync(messages, snapshot)).Result);
        else
        {
            var config = new CompactionConfig { KeepRecentMinTokens = 1, KeepRecentMinGroups = 1, KeepRecentMaxTokens = 100_000 };
            var result = await new PartialCompactor(client, config, new MaintenanceForkRunner(client))
                .CompactAsync(messages, snapshot);
            Assert.NotNull(result.Result);
            Assert.DoesNotContain(result.Result.SummarizedPrefix, AgentInstructionsHistory.IsInstructions);
            Assert.DoesNotContain(result.Result.PreservedTail, AgentInstructionsHistory.IsInstructions);
            Assert.Equal("recent answer", Assert.Single(result.Result.PreservedTail).Text);
            Assert.Contains(result.Result.SummarizedPrefix, message => message.Text == "recent task");
        }
        Assert.Equal("original system", client.Options?.Instructions);
        Assert.Equal(messages.Select(message => message.Text), client.Messages.Take(messages.Count).Select(message => message.Text));
        Assert.Equal(2, client.Messages.Count(AgentInstructionsHistory.IsInstructions));
        Assert.Equal(2, messages.Count(AgentInstructionsHistory.IsInstructions));
    }

    private sealed class CaptureClient : IChatClient
    {
        public ChatMessage[] Messages { get; private set; } = [];
        public ChatOptions? Options { get; private set; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Messages = messages.ToArray();
            Options = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Useful handoff.")));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type type, object? key = null) => null;
        public void Dispose() { }
    }
}
