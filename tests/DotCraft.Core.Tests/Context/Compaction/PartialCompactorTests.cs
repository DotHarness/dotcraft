using DotCraft.Context;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Context.Compaction;

public sealed class PartialCompactorTests
{
    [Fact]
    public void CalculateSplitIndex_SmallConversationKeepsEverything()
    {
        var cfg = new CompactionConfig
        {
            KeepRecentMinTokens = 100,
            KeepRecentMinGroups = 5,
            KeepRecentMaxTokens = 200,
        };
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "hi"),
            new(ChatRole.Assistant, "hello"),
        };

        Assert.Equal(0, PartialCompactor.CalculateSplitIndex(messages, cfg));
    }

    [Fact]
    public void CalculateSplitIndex_LargeConversationSplitsPreservingTail()
    {
        var cfg = new CompactionConfig
        {
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 2,
            KeepRecentMaxTokens = 100_000,
        };

        var messages = new List<ChatMessage>();
        for (var round = 0; round < 5; round++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"user turn {round}"));
            messages.Add(new ChatMessage(ChatRole.Assistant, $"assistant turn {round}"));
        }

        var splitIndex = PartialCompactor.CalculateSplitIndex(messages, cfg);
        // API-response grouping preserves the last assistant response and the request leading into it.
        Assert.Equal(7, splitIndex);
    }

    [Fact]
    public void CalculateSplitIndex_SinglePromptToolLoopSplits()
    {
        var cfg = new CompactionConfig
        {
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 3,
            KeepRecentMaxTokens = 100_000,
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "do the long agentic task"),
        };
        for (var round = 0; round < 8; round++)
        {
            var callId = $"call-{round}";
            messages.Add(new ChatMessage(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent(callId, "ReadFile", new Dictionary<string, object?>
                {
                    ["path"] = $"file-{round}.txt",
                }),
            })
            {
                MessageId = $"response-{round}",
            });
            messages.Add(new ChatMessage(ChatRole.Tool, new List<AIContent>
            {
                new FunctionResultContent(callId, new string('x', 512)),
            }));
        }
        messages.Add(new ChatMessage(ChatRole.Assistant, "final answer")
        {
            MessageId = "response-final",
        });

        var splitIndex = PartialCompactor.CalculateSplitIndex(messages, cfg);
        Assert.True(splitIndex > 0);
        Assert.True(splitIndex < messages.Count);
    }

    [Fact]
    public async Task CompactAsync_EmptyHistoryReturnsReason()
    {
        var cfg = new CompactionConfig();
        var partial = new PartialCompactor(new StubChatClient("summary"), cfg);

        var result = await partial.CompactAsync(Array.Empty<ChatMessage>());
        Assert.Null(result.Result);
        Assert.Equal("empty_history", result.Reason);
    }

    [Fact]
    public async Task CompactAsync_SummarizesPrefixAndRetainsTail()
    {
        var cfg = new CompactionConfig
        {
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 1,
            KeepRecentMaxTokens = 100_000,
        };
        var client = new StubChatClient("<analysis>thinking</analysis><summary>important bits</summary>");
        var partial = new PartialCompactor(client, cfg);

        var messages = new List<ChatMessage>();
        for (var round = 0; round < 4; round++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"user turn {round}"));
            messages.Add(new ChatMessage(ChatRole.Assistant, $"assistant turn {round}"));
        }

        var result = await partial.CompactAsync(messages);
        Assert.NotNull(result.Result);
        Assert.True(result.Result!.SummarizedPrefix.Count > 0);
        Assert.True(result.Result.PreservedTail.Count > 0);
        Assert.Contains("important bits", result.Result.FormattedSummary);
        // analysis should be stripped from FormattedSummary.
        Assert.DoesNotContain("<analysis>", result.Result.FormattedSummary);
        Assert.Equal("<analysis>thinking</analysis><summary>important bits</summary>", result.Result.RawSummary);
    }

    [Fact]
    public async Task CompactAsync_ReplacesToolImagesInPreservedTail()
    {
        var cfg = new CompactionConfig
        {
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 1,
            KeepRecentMaxTokens = 100_000,
        };
        var partial = new PartialCompactor(new StubChatClient("<summary>important bits</summary>"), cfg);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "old request"),
            new(ChatRole.Assistant, "old answer"),
            new(ChatRole.User, "inspect"),
            new(ChatRole.Assistant, (IList<AIContent>)[new FunctionCallContent("call-1", "Screenshot")]),
            new(ChatRole.Tool, (IList<AIContent>)
            [
                new FunctionResultContent("call-1", new AIContent[]
                {
                    new TextContent("captured"),
                    new DataContent(new byte[] { 1, 2, 3 }, "image/png")
                })
            ])
        };

        var attempt = await partial.CompactAsync(messages);

        Assert.NotNull(attempt.Result);
        var toolResult = Assert.Single(
            attempt.Result!.PreservedTail.SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        var text = Assert.IsType<string>(toolResult.Result);
        Assert.Contains("[Image (image/png)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("data:image/", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            attempt.Result.PreservedTail.SelectMany(message => message.Contents).OfType<FunctionCallContent>(),
            call => call.CallId == toolResult.CallId);
    }

    [Fact]
    public async Task CompactAsync_WithSnapshotRunsMaintenanceFork()
    {
        var cfg = new CompactionConfig
        {
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 1,
            KeepRecentMaxTokens = 100_000,
        };
        var client = new StubChatClient("<analysis>thinking</analysis><summary>important bits</summary>");
        var tool = AIFunctionFactory.Create(() => "ok", name: "ReadFile", description: "Read a file.");
        var partial = new PartialCompactor(client, cfg, new MaintenanceForkRunner(client));

        var messages = new List<ChatMessage>();
        for (var round = 0; round < 4; round++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"user turn {round}"));
            messages.Add(new ChatMessage(ChatRole.Assistant, $"assistant turn {round}"));
        }
        var snapshot = PromptRequestSnapshot.Capture(
            messages,
            new ChatOptions
            {
                Instructions = "stable base",
                ModelId = "gpt-test",
                MaxOutputTokens = 64_000,
                Tools = [tool]
            });

        var result = await partial.CompactAsync(messages, snapshot);

        Assert.NotNull(result.Result);
        Assert.Contains("important bits", result.Result!.FormattedSummary);
        Assert.Equal(["user:user turn 0", "assistant:assistant turn 0"], client.Messages.Take(2).Select(m => $"{m.Role}:{m.Text}"));
        Assert.Equal(ChatRole.User, client.Messages[^1].Role);
        Assert.Contains("## Maintenance Task", client.Messages[^1].Text);
        Assert.Contains("Task: context_compaction", client.Messages[^1].Text);
        Assert.Equal("stable base", client.Options?.Instructions);
        Assert.Equal("gpt-test", client.Options?.ModelId);
        Assert.Equal(12_000, client.Options?.MaxOutputTokens);
        var capturedTool = Assert.Single(client.Options?.Tools ?? []);
        Assert.Equal("ReadFile", capturedTool.Name);
    }

    [Fact]
    public async Task CompactAsync_WithSnapshotContextOverflowFallsBackToLegacySummary()
    {
        var cfg = new CompactionConfig
        {
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 1,
            KeepRecentMaxTokens = 100_000,
        };
        var client = new StubChatClient(
            "<analysis>thinking</analysis><summary>legacy after overflow</summary>",
            promptTooLongFailures: 1);
        var partial = new PartialCompactor(client, cfg, new MaintenanceForkRunner(client));

        var messages = new List<ChatMessage>();
        for (var round = 0; round < 4; round++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"user turn {round}"));
            messages.Add(new ChatMessage(ChatRole.Assistant, $"assistant turn {round}"));
        }
        var snapshot = PromptRequestSnapshot.Capture(
            messages,
            new ChatOptions
            {
                Instructions = "stable base",
                ModelId = "gpt-test"
            });

        var result = await partial.CompactAsync(messages, snapshot);

        Assert.NotNull(result.Result);
        Assert.Equal(2, client.CallCount);
        Assert.Contains("legacy after overflow", result.Result!.FormattedSummary);
        Assert.Equal(ChatRole.System, client.Messages[0].Role);
        AssertLegacyContextCompactionTask(client.Messages[^1]);
    }

    [Fact]
    public async Task CompactAsync_WithOversizedSnapshotPreflightFallsBackToLegacySummary()
    {
        var cfg = new CompactionConfig
        {
            ContextWindow = 1_000,
            SummaryMaxOutputTokens = 100,
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 1,
            KeepRecentMaxTokens = 100_000,
        };
        var client = new StubChatClient("<analysis>thinking</analysis><summary>legacy after preflight</summary>");
        var partial = new PartialCompactor(client, cfg, new MaintenanceForkRunner(client));

        var messages = new List<ChatMessage>();
        for (var round = 0; round < 4; round++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"user turn {round}"));
            messages.Add(new ChatMessage(ChatRole.Assistant, $"assistant turn {round}"));
        }
        var snapshot = PromptRequestSnapshot.Capture(
            messages,
            new ChatOptions
            {
                Instructions = "stable base",
                ModelId = "gpt-test"
            },
            estimatedInputTokens: 10_000);

        var result = await partial.CompactAsync(messages, snapshot);

        Assert.NotNull(result.Result);
        Assert.Equal(1, client.CallCount);
        Assert.Contains("legacy after preflight", result.Result!.FormattedSummary);
        Assert.Equal(ChatRole.System, client.Messages[0].Role);
        AssertLegacyContextCompactionTask(client.Messages[^1]);
    }

    [Fact]
    public async Task CompactAsync_WithSnapshotEmptyErrorContentFallsBackToLegacySummary()
    {
        var cfg = new CompactionConfig
        {
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 1,
            KeepRecentMaxTokens = 100_000,
        };
        var client = new SequenceChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, (IList<AIContent>)
            [
                new ErrorContent("provider returned an empty error response")
            ])),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "<analysis>thinking</analysis><summary>legacy after empty error</summary>")));
        var partial = new PartialCompactor(client, cfg, new MaintenanceForkRunner(client));

        var messages = new List<ChatMessage>();
        for (var round = 0; round < 4; round++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"user turn {round}"));
            messages.Add(new ChatMessage(ChatRole.Assistant, $"assistant turn {round}"));
        }
        var snapshot = PromptRequestSnapshot.Capture(
            messages,
            new ChatOptions
            {
                Instructions = "stable base",
                ModelId = "gpt-test"
            });

        var result = await partial.CompactAsync(messages, snapshot);

        Assert.NotNull(result.Result);
        Assert.Equal(2, client.CallCount);
        Assert.Contains("legacy after empty error", result.Result!.FormattedSummary);
        Assert.Equal(ChatRole.System, client.Messages[0].Role);
        AssertLegacyContextCompactionTask(client.Messages[^1]);
    }

    [Fact]
    public async Task CompactAsync_LegacyPathWithSnapshotPassesSnapshotTools()
    {
        var cfg = new CompactionConfig
        {
            SummaryMaxOutputTokens = 1_200,
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 1,
            KeepRecentMaxTokens = 100_000,
        };
        var client = new StubChatClient("<analysis>thinking</analysis><summary>legacy important bits</summary>");
        var tool = AIFunctionFactory.Create(() => "ok", name: "ReadFile", description: "Read a file.");
        var partial = new PartialCompactor(client, cfg);

        var messages = new List<ChatMessage>();
        for (var round = 0; round < 4; round++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"user turn {round}"));
            messages.Add(new ChatMessage(ChatRole.Assistant, $"assistant turn {round}"));
        }
        var snapshot = PromptRequestSnapshot.Capture(
            messages,
            new ChatOptions
            {
                Instructions = "stable base",
                ModelId = "gpt-test",
                MaxOutputTokens = 800,
                Tools = [tool]
            });

        var result = await partial.CompactAsync(messages, snapshot);

        Assert.NotNull(result.Result);
        Assert.Contains("legacy important bits", result.Result!.FormattedSummary);
        Assert.Equal(ChatRole.System, client.Messages[0].Role);
        AssertLegacyContextCompactionTask(client.Messages[^1]);
        var capturedTool = Assert.Single(client.Options?.Tools ?? []);
        Assert.Equal("ReadFile", capturedTool.Name);
        Assert.Equal(800, client.Options?.MaxOutputTokens);
        Assert.Equal("none", client.Options?.AdditionalProperties?["dotcraft.tool_choice"]);
    }

    [Fact]
    public async Task CompactAsync_LegacyPathWithoutSnapshotPassesFallbackTools()
    {
        var cfg = new CompactionConfig
        {
            SummaryMaxOutputTokens = 1_234,
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 1,
            KeepRecentMaxTokens = 100_000,
        };
        var client = new StubChatClient("<analysis>thinking</analysis><summary>legacy important bits</summary>");
        var tool = AIFunctionFactory.Create(() => "ok", name: "GetStatus", description: "Get status.");
        var partial = new PartialCompactor(client, cfg);

        var messages = new List<ChatMessage>();
        for (var round = 0; round < 4; round++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"user turn {round}"));
            messages.Add(new ChatMessage(ChatRole.Assistant, $"assistant turn {round}"));
        }

        var result = await partial.CompactAsync(
            messages,
            snapshot: null,
            threadId: "thread-1",
            fallbackTools: [tool]);

        Assert.NotNull(result.Result);
        Assert.Contains("legacy important bits", result.Result!.FormattedSummary);
        Assert.Equal(ChatRole.System, client.Messages[0].Role);
        AssertLegacyContextCompactionTask(client.Messages[^1]);
        var capturedTool = Assert.Single(client.Options?.Tools ?? []);
        Assert.Equal("GetStatus", capturedTool.Name);
        Assert.Equal(1_234, client.Options?.MaxOutputTokens);
        Assert.Equal("none", client.Options?.AdditionalProperties?["dotcraft.tool_choice"]);
    }

    [Fact]
    public async Task CompactAsync_ReturnsReasonOnChatClientFailure()
    {
        var cfg = new CompactionConfig
        {
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 1,
        };
        var partial = new PartialCompactor(new StubChatClient(string.Empty, throwOnCall: true), cfg);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "u1"),
            new(ChatRole.Assistant, "a1"),
            new(ChatRole.User, "u2"),
            new(ChatRole.Assistant, "a2"),
        };

        var result = await partial.CompactAsync(messages);
        Assert.Null(result.Result);
        Assert.Equal("summary_unavailable", result.Reason);
    }

    [Fact]
    public async Task CompactAsync_RetriesPromptTooLongByDroppingOldestGroups()
    {
        var cfg = new CompactionConfig
        {
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 1,
            KeepRecentMaxTokens = 100_000,
        };
        var client = new StubChatClient(
            "<analysis>thinking</analysis><summary>retried summary</summary>",
            promptTooLongFailures: 1);
        var partial = new PartialCompactor(client, cfg);

        var messages = new List<ChatMessage>();
        for (var round = 0; round < 5; round++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"user turn {round}"));
            messages.Add(new ChatMessage(ChatRole.Assistant, $"assistant turn {round}"));
        }

        var result = await partial.CompactAsync(messages);

        Assert.NotNull(result.Result);
        Assert.Equal(2, client.CallCount);
        Assert.Contains("retried summary", result.Result!.FormattedSummary);
        Assert.DoesNotContain(client.Messages, m => m.Text?.Contains("user turn 0", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task CompactAsync_PreflightTrimsOversizedLegacySummaryInput()
    {
        var cfg = new CompactionConfig
        {
            ContextWindow = 1_200,
            SummaryReserveTokens = 0,
            AutoCompactBufferTokens = 0,
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 1,
            KeepRecentMaxTokens = 100_000,
        };
        var client = new StubChatClient("<analysis>thinking</analysis><summary>trimmed summary</summary>");
        var partial = new PartialCompactor(client, cfg);

        var messages = new List<ChatMessage>();
        for (var round = 0; round < 8; round++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"user turn {round} " + new string('u', 1200)));
            messages.Add(new ChatMessage(ChatRole.Assistant, $"assistant turn {round} " + new string('a', 1200))
            {
                MessageId = $"response-{round}",
            });
        }

        var result = await partial.CompactAsync(messages);

        Assert.NotNull(result.Result);
        Assert.DoesNotContain(client.Messages, m => m.Text?.Contains("user turn 0", StringComparison.Ordinal) == true);
        Assert.Contains(client.Messages, m => m.Text?.Contains("user turn", StringComparison.Ordinal) == true);
        AssertLegacyContextCompactionTask(client.Messages[^1]);
    }

    [Fact]
    public async Task CompactAsync_PreflightTruncatesToolResultsBeforeDroppingGroups()
    {
        var cfg = new CompactionConfig
        {
            ContextWindow = 2_000,
            SummaryReserveTokens = 0,
            AutoCompactBufferTokens = 0,
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 1,
            KeepRecentMaxTokens = 100_000,
        };
        var client = new StubChatClient("<analysis>thinking</analysis><summary>truncated tool summary</summary>");
        var partial = new PartialCompactor(client, cfg);
        var messages = new List<ChatMessage>();
        for (var round = 0; round < 3; round++)
        {
            var callId = $"call-{round}";
            messages.Add(new ChatMessage(ChatRole.User, $"user turn {round}"));
            messages.Add(new ChatMessage(ChatRole.Assistant, (IList<AIContent>)
            [
                new FunctionCallContent(callId, "ReadFile", new Dictionary<string, object?>())
            ]));
            messages.Add(new ChatMessage(ChatRole.Tool, (IList<AIContent>)
            [
                new FunctionResultContent(callId, round == 0 ? new string('x', 6_000) : "small result")
            ]));
        }

        var result = await partial.CompactAsync(messages);

        Assert.NotNull(result.Result);
        Assert.Contains(client.Messages, m => m.Text?.Contains("user turn 0", StringComparison.Ordinal) == true);
        var toolResults = client.Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().ToArray();
        Assert.Contains(toolResults, result => result.Result?.ToString()?.Contains("Output exceeded the available model context", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(toolResults, result => result.Result?.ToString()?.Length > 1_000);
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

    private sealed class StubChatClient : IChatClient
    {
        private readonly string _responseText;
        private readonly bool _throwOnCall;
        private int _promptTooLongFailures;

        public IReadOnlyList<ChatMessage> Messages { get; private set; } = [];
        public ChatOptions? Options { get; private set; }
        public int CallCount { get; private set; }

        public StubChatClient(string responseText, bool throwOnCall = false, int promptTooLongFailures = 0)
        {
            _responseText = responseText;
            _throwOnCall = throwOnCall;
            _promptTooLongFailures = promptTooLongFailures;
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (_throwOnCall)
                throw new InvalidOperationException("boom");
            if (_promptTooLongFailures > 0)
            {
                _promptTooLongFailures--;
                throw new InvalidOperationException("prompt_too_long");
            }

            Messages = messages.ToArray();
            Options = options;
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, _responseText));
            return Task.FromResult(response);
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
