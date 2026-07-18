using DotCraft.Context;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Context.Compaction;

public sealed class FullCompactorTests
{
    [Fact]
    public async Task CompactAsync_WithCompatibleSnapshot_PreservesCachedPrefixAndAppendsTailBeforeTask()
    {
        var client = new RecordingChatClient("<analysis>ok</analysis><summary>cached full summary</summary>");
        var full = new FullCompactor(client, new MaintenanceForkRunner(client));
        var tool = AIFunctionFactory.Create(() => "ok", name: "ReadFile", description: "Read a file.");
        var snapshotMessages = new List<ChatMessage>
        {
            new(ChatRole.User, "first user"),
            new(ChatRole.Assistant, "first assistant"),
            new(ChatRole.User, "second user")
        };
        var snapshot = PromptRequestSnapshot.Capture(
            snapshotMessages,
            new ChatOptions
            {
                Instructions = "stable base instructions",
                ModelId = "gpt-test",
                MaxOutputTokens = 64_000,
                Tools = [tool]
            });
        var history = snapshotMessages
            .Concat([new ChatMessage(ChatRole.Assistant, "second assistant")])
            .ToList();

        var result = await full.CompactAsync(history, snapshot);

        Assert.NotNull(result.Result);
        Assert.Contains("cached full summary", result.Result!.FormattedSummary);
        Assert.Equal(
            ["user:first user", "assistant:first assistant", "user:second user"],
            client.Messages.Take(3).Select(message => $"{message.Role}:{message.Text}"));
        Assert.Equal("assistant:second assistant", $"{client.Messages[3].Role}:{client.Messages[3].Text}");
        Assert.Equal(ChatRole.User, client.Messages[^1].Role);
        Assert.Contains("Task: context_compaction", client.Messages[^1].Text);
        Assert.Equal("stable base instructions", client.Options?.Instructions);
        Assert.Equal("gpt-test", client.Options?.ModelId);
        Assert.Equal(12_000, client.Options?.MaxOutputTokens);
        var capturedTool = Assert.Single(client.Options?.Tools ?? []);
        Assert.Equal("ReadFile", capturedTool.Name);
    }

    [Fact]
    public async Task CompactAsync_WithSnapshotContextOverflowFallsBackToLegacySummary()
    {
        var client = new RecordingChatClient(
            "<analysis>ok</analysis><summary>legacy full after overflow</summary>",
            promptTooLongFailures: 1);
        var full = new FullCompactor(client, new MaintenanceForkRunner(client));
        var snapshotMessages = new List<ChatMessage>
        {
            new(ChatRole.User, "first user"),
            new(ChatRole.Assistant, "first assistant")
        };
        var snapshot = PromptRequestSnapshot.Capture(
            snapshotMessages,
            new ChatOptions
            {
                Instructions = "stable base instructions",
                ModelId = "gpt-test"
            });

        var result = await full.CompactAsync(snapshotMessages, snapshot);

        Assert.NotNull(result.Result);
        Assert.Equal(2, client.CallCount);
        Assert.Contains("legacy full after overflow", result.Result!.FormattedSummary);
        Assert.Equal(ChatRole.System, client.Messages[0].Role);
        AssertLegacyContextCompactionTask(client.Messages[^1]);
    }

    [Fact]
    public async Task CompactAsync_WithOversizedSnapshotPreflightFallsBackToLegacySummary()
    {
        var client = new RecordingChatClient("<analysis>ok</analysis><summary>legacy full after preflight</summary>");
        var config = new CompactionConfig
        {
            ContextWindow = 1_000,
            SummaryMaxOutputTokens = 100
        };
        var full = new FullCompactor(client, new MaintenanceForkRunner(client), config: config);
        var snapshotMessages = new List<ChatMessage>
        {
            new(ChatRole.User, "first user"),
            new(ChatRole.Assistant, "first assistant")
        };
        var snapshot = PromptRequestSnapshot.Capture(
            snapshotMessages,
            new ChatOptions
            {
                Instructions = "stable base instructions",
                ModelId = "gpt-test"
            },
            estimatedInputTokens: 10_000);

        var result = await full.CompactAsync(snapshotMessages, snapshot);

        Assert.NotNull(result.Result);
        Assert.Equal(1, client.CallCount);
        Assert.Contains("legacy full after preflight", result.Result!.FormattedSummary);
        Assert.Equal(ChatRole.System, client.Messages[0].Role);
        AssertLegacyContextCompactionTask(client.Messages[^1]);
    }

    [Fact]
    public async Task CompactAsync_WithSnapshotEmptyErrorContentFallsBackToLegacySummary()
    {
        var client = new SequenceChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, (IList<AIContent>)
            [
                new ErrorContent("provider returned an empty error response")
            ])),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "<analysis>ok</analysis><summary>legacy full after empty error</summary>")));
        var full = new FullCompactor(client, new MaintenanceForkRunner(client));
        var snapshotMessages = new List<ChatMessage>
        {
            new(ChatRole.User, "first user"),
            new(ChatRole.Assistant, "first assistant")
        };
        var snapshot = PromptRequestSnapshot.Capture(
            snapshotMessages,
            new ChatOptions
            {
                Instructions = "stable base instructions",
                ModelId = "gpt-test"
            });

        var result = await full.CompactAsync(snapshotMessages, snapshot);

        Assert.NotNull(result.Result);
        Assert.Equal(2, client.CallCount);
        Assert.Contains("legacy full after empty error", result.Result!.FormattedSummary);
        Assert.Equal(ChatRole.System, client.Messages[0].Role);
        AssertLegacyContextCompactionTask(client.Messages[^1]);
    }

    [Fact]
    public async Task CompactAsync_LegacyPathUsesFullContextBoundaryInstructionsAndNoTools()
    {
        var client = new RecordingChatClient("<analysis>ok</analysis><summary>legacy full summary</summary>");
        var full = new FullCompactor(client, config: new CompactionConfig { SummaryMaxOutputTokens = 1_234 });
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "first user"),
            new(ChatRole.Assistant, "first assistant")
        };

        var result = await full.CompactAsync(history, snapshot: null);

        Assert.NotNull(result.Result);
        Assert.Contains("legacy full summary", result.Result!.FormattedSummary);
        Assert.Equal(ChatRole.System, client.Messages[0].Role);
        Assert.Contains("Summarize the complete conversation visible above", client.Messages[0].Text);
        Assert.Contains("replace the current model-visible history", client.Messages[0].Text);
        AssertLegacyContextCompactionTask(client.Messages[^1]);
        Assert.Null(client.Options?.Tools);
        Assert.Equal(1_234, client.Options?.MaxOutputTokens);
    }

    [Fact]
    public async Task CompactAsync_LegacyPathWithSnapshotPassesSnapshotTools()
    {
        var client = new RecordingChatClient("<analysis>ok</analysis><summary>legacy full summary</summary>");
        var full = new FullCompactor(client, new MaintenanceForkRunner(client));
        var tool = AIFunctionFactory.Create(() => "ok", name: "ReadFile", description: "Read a file.");
        var snapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "different prefix")],
            new ChatOptions
            {
                Instructions = "stable base instructions",
                ModelId = "gpt-test",
                MaxOutputTokens = 800,
                Tools = [tool]
            });
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "first user"),
            new(ChatRole.Assistant, "first assistant")
        };

        var result = await full.CompactAsync(history, snapshot);

        Assert.NotNull(result.Result);
        Assert.Contains("legacy full summary", result.Result!.FormattedSummary);
        Assert.Equal(ChatRole.System, client.Messages[0].Role);
        AssertLegacyContextCompactionTask(client.Messages[^1]);
        var capturedTool = Assert.Single(client.Options?.Tools ?? []);
        Assert.Equal("ReadFile", capturedTool.Name);
        Assert.Equal(800, client.Options?.MaxOutputTokens);
        Assert.Null(client.Options?.ToolMode);
        Assert.NotNull(client.Options?.RawRepresentationFactory);
    }

    [Fact]
    public async Task CompactAsync_LegacyPathWithoutSnapshotPassesFallbackTools()
    {
        var client = new RecordingChatClient("<analysis>ok</analysis><summary>legacy full summary</summary>");
        var full = new FullCompactor(client);
        var tool = AIFunctionFactory.Create(() => "ok", name: "GetStatus", description: "Get status.");
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "first user"),
            new(ChatRole.Assistant, "first assistant")
        };

        var result = await full.CompactAsync(
            history,
            snapshot: null,
            threadId: "thread-1",
            fallbackTools: [tool]);

        Assert.NotNull(result.Result);
        Assert.Contains("legacy full summary", result.Result!.FormattedSummary);
        Assert.Equal(ChatRole.System, client.Messages[0].Role);
        AssertLegacyContextCompactionTask(client.Messages[^1]);
        var capturedTool = Assert.Single(client.Options?.Tools ?? []);
        Assert.Equal("GetStatus", capturedTool.Name);
        Assert.Null(client.Options?.ToolMode);
        Assert.NotNull(client.Options?.RawRepresentationFactory);
    }

    [Fact]
    public async Task CompactAsync_PreflightTrimsOversizedLegacySummaryInput()
    {
        var client = new RecordingChatClient("<analysis>ok</analysis><summary>trimmed full summary</summary>");
        var config = new CompactionConfig
        {
            ContextWindow = 1_200,
            SummaryReserveTokens = 0,
            AutoCompactBufferTokens = 0,
        };
        var full = new FullCompactor(client, config: config);
        var history = new List<ChatMessage>();
        for (var round = 0; round < 8; round++)
        {
            history.Add(new ChatMessage(ChatRole.User, $"first user {round} " + new string('u', 1200)));
            history.Add(new ChatMessage(ChatRole.Assistant, $"assistant {round} " + new string('a', 1200))
            {
                MessageId = $"response-{round}",
            });
        }

        var result = await full.CompactAsync(history, snapshot: null);

        Assert.NotNull(result.Result);
        Assert.DoesNotContain(client.Messages, m => m.Text?.Contains("first user 0", StringComparison.Ordinal) == true);
        Assert.Contains(client.Messages, m => m.Text?.Contains("assistant 7", StringComparison.Ordinal) == true);
        AssertLegacyContextCompactionTask(client.Messages[^1]);
    }

    [Fact]
    public async Task CompactAsync_OmitsNestedToolImageFromSummaryRequest()
    {
        var client = new RecordingChatClient("<summary>safe summary</summary>");
        var full = new FullCompactor(client);
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "inspect"),
            new(ChatRole.Assistant, (IList<AIContent>)[new FunctionCallContent("call-1", "Screenshot")]),
            new(ChatRole.Tool, (IList<AIContent>)
            [
                new FunctionResultContent("call-1", new List<AIContent>
                {
                    new TextContent("captured"),
                    new DataContent(new byte[] { 1, 2, 3 }, "image/png")
                })
            ])
        };

        var attempt = await full.CompactAsync(history, snapshot: null);

        Assert.NotNull(attempt.Result);
        var result = Assert.Single(
            client.Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        var text = Assert.IsType<string>(result.Result);
        Assert.Contains("[Image (image/png)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("data:image/", text, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertLegacyContextCompactionTask(ChatMessage message)
    {
        Assert.Equal(ChatRole.User, message.Role);
        Assert.Contains("<system-reminder>", message.Text);
        Assert.Contains("## Maintenance Task", message.Text);
        Assert.Contains("Task: context_compaction", message.Text);
        Assert.Contains("Do not call tools", message.Text);
    }

    private sealed class SequenceChatClient(params ChatResponse[] responses) : IChatClient
    {
        private readonly Queue<ChatResponse> _responses = new(responses);

        public IReadOnlyList<ChatMessage> Messages { get; private set; } = [];
        public ChatOptions? Options { get; private set; }
        public int CallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Messages = messages.ToArray();
            Options = options;
            return Task.FromResult(_responses.Dequeue());
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private sealed class RecordingChatClient(string responseText, int promptTooLongFailures = 0) : IChatClient
    {
        private int _promptTooLongFailures = promptTooLongFailures;

        public IReadOnlyList<ChatMessage> Messages { get; private set; } = [];
        public ChatOptions? Options { get; private set; }
        public int CallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (_promptTooLongFailures > 0)
            {
                _promptTooLongFailures--;
                throw new InvalidOperationException("prompt_too_long");
            }

            Messages = messages.ToArray();
            Options = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
