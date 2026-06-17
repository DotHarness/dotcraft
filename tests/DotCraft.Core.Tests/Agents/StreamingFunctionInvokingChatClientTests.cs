using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Protocol;
using System.ClientModel.Primitives;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Agents;

public sealed partial class StreamingFunctionInvokingChatClientTests
{
    [Fact]
    public async Task GetStreamingResponseAsync_DrainsGuidanceBeforeNextModelRequest()
    {
        var inner = new RoundTripFakeChatClient();
        var tool = AIFunctionFactory.Create(() => "tool ok", name: "GetStatus");
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool]
        };
        var drained = false;

        using var scope = TurnGuidanceRuntimeScope.Set(new TurnGuidanceRuntimeContext
        {
            ThreadId = "thread_1",
            TurnId = "turn_1",
            TryDrainGuidanceMessageAsync = _ =>
            {
                if (drained)
                    return Task.FromResult<ChatMessage?>(null);
                drained = true;
                return Task.FromResult<ChatMessage?>(new ChatMessage(ChatRole.User, "guidance text"));
            }
        });

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
            updates.Add(update);

        Assert.True(drained);
        Assert.Equal(2, inner.Calls.Count);
        Assert.Contains(inner.Calls[1], message => message.Role == ChatRole.User && message.Text == "guidance text");
        Assert.Contains(updates, update => update.Contents.OfType<FunctionResultContent>().Any());
    }

    [Fact]
    public async Task GetStreamingResponseAsync_DrainsMailboxTurnContextAsUserBeforeNextModelRequest()
    {
        var inner = new RoundTripFakeChatClient();
        var tool = AIFunctionFactory.Create(() => "tool ok", name: "GetStatus");
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool]
        };
        var drained = false;
        const string notification = "<subagent_notification>{\"agentPath\":\"/root/worker\",\"status\":{\"completed\":\"done\"}}</subagent_notification>";

        using var scope = TurnGuidanceRuntimeScope.Set(new TurnGuidanceRuntimeContext
        {
            ThreadId = "thread_1",
            TurnId = "turn_1",
            TryDrainGuidanceMessageAsync = _ =>
            {
                if (drained)
                    return Task.FromResult<ChatMessage?>(null);
                drained = true;
                return Task.FromResult<ChatMessage?>(new ChatMessage(ChatRole.User, notification));
            }
        });

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        Assert.True(drained);
        Assert.Equal(2, inner.Calls.Count);
        Assert.Contains(inner.Calls[1], message => message.Role == ChatRole.User && message.Text == notification);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_RunsPreSamplingCompactionBeforeModelRequest()
    {
        var inner = new SingleReplyFakeChatClient();
        var client = new StreamingFunctionInvokingChatClient(inner);
        var callbackCalls = 0;
        var replacement = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "compacted summary"),
            new(ChatRole.User, "latest user")
        };

        using var scope = PreSamplingCompactionRuntimeScope.Set(new PreSamplingCompactionRuntimeContext
        {
            TryCompactAsync = (messages, _) =>
            {
                callbackCalls++;
                Assert.Contains(messages, message => message.Role == ChatRole.User && message.Text == "start");
                return Task.FromResult<IReadOnlyList<ChatMessage>?>(replacement);
            }
        });

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        Assert.Equal(1, callbackCalls);
        var call = Assert.Single(inner.Calls);
        Assert.Equal(["assistant:compacted summary", "user:latest user"], call.Select(m => $"{m.Role}:{m.Text}"));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_CapturesPromptRequestSnapshotAfterPreSamplingCompaction()
    {
        var inner = new SingleReplyFakeChatClient();
        var client = new StreamingFunctionInvokingChatClient(inner);
        var tool = AIFunctionFactory.Create(() => "ok", name: "ReadFile", description: "Read a file.");
        var replacement = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "compacted summary"),
            new(ChatRole.User, "latest user")
        };
        PromptRequestSnapshot? snapshot = null;

        using var scope = PreSamplingCompactionRuntimeScope.Set(new PreSamplingCompactionRuntimeContext
        {
            ThreadId = "thread_1",
            TurnId = "turn_1",
            Mode = "agent",
            TryCompactAsync = (_, _) => Task.FromResult<IReadOnlyList<ChatMessage>?>(replacement),
            CaptureSnapshotAsync = (value, _) =>
            {
                snapshot = value;
                return Task.CompletedTask;
            }
        });

        await foreach (var _ in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "start")],
            new ChatOptions
            {
                Instructions = "stable base instructions",
                ModelId = "gpt-test",
                Tools = [tool],
                AllowMultipleToolCalls = false
            }))
        {
        }

        var captured = Assert.IsType<PromptRequestSnapshot>(snapshot);
        Assert.Equal("thread_1", captured.ThreadId);
        Assert.Equal("turn_1", captured.TurnId);
        Assert.Equal("agent", captured.Mode);
        Assert.Equal("gpt-test", captured.ModelId);
        Assert.Equal("stable base instructions", captured.BaseInstructions);
        Assert.Equal(
            PromptRequestFingerprints.ComputeTextFingerprint("stable base instructions"),
            captured.BaseInstructionsFingerprint);
        Assert.Equal(["assistant:compacted summary", "user:latest user"], captured.Messages.Select(m => $"{m.Role}:{m.Text}"));
        var capturedTool = Assert.Single(captured.Tools);
        Assert.Equal("ReadFile", capturedTool.Name);
        Assert.Equal(PromptRequestFingerprints.ComputeToolFingerprint([tool]), captured.ToolFingerprint);
        Assert.False(captured.AllowMultipleToolCalls);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_PassesPromptRequestSnapshotToPreSamplingCompaction()
    {
        var inner = new SingleReplyFakeChatClient();
        var client = new StreamingFunctionInvokingChatClient(inner);
        var tool = AIFunctionFactory.Create(() => "ok", name: "ReadFile", description: "Read a file.");
        PromptRequestSnapshot? compactionSnapshot = null;
        var replacement = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "compacted summary"),
            new(ChatRole.User, "latest user")
        };

        using var scope = PreSamplingCompactionRuntimeScope.Set(new PreSamplingCompactionRuntimeContext
        {
            ThreadId = "thread_1",
            TurnId = "turn_1",
            Mode = "agent",
            TryCompactWithSnapshotAsync = (_, snapshot, _) =>
            {
                compactionSnapshot = snapshot;
                return Task.FromResult<IReadOnlyList<ChatMessage>?>(replacement);
            },
            TryCompactAsync = (_, _) => throw new InvalidOperationException("legacy callback should not run")
        });

        await foreach (var _ in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "start")],
            new ChatOptions
            {
                Instructions = "stable base instructions",
                ModelId = "gpt-test",
                Tools = [tool]
            }))
        {
        }

        var captured = Assert.IsType<PromptRequestSnapshot>(compactionSnapshot);
        Assert.Equal("thread_1", captured.ThreadId);
        Assert.Equal("turn_1", captured.TurnId);
        Assert.Equal("agent", captured.Mode);
        Assert.Equal("stable base instructions", captured.BaseInstructions);
        Assert.Equal("gpt-test", captured.ModelId);
        Assert.Equal(["user:start"], captured.Messages.Select(m => $"{m.Role}:{m.Text}"));
        Assert.Equal("ReadFile", Assert.Single(captured.Tools).Name);
        Assert.Equal(["assistant:compacted summary", "user:latest user"], inner.Calls.Single().Select(m => $"{m.Role}:{m.Text}"));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_LimitsGuidanceContinuationsAfterTermination()
    {
        var inner = new AlwaysCallsToolFakeChatClient();
        var tool = AIFunctionFactory.Create(() => "tool ok", name: "GetStatus");
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            MaximumGuidanceContinuationsPerRequest = 2
        };
        var drains = 0;

        using var scope = TurnGuidanceRuntimeScope.Set(new TurnGuidanceRuntimeContext
        {
            ThreadId = "thread_1",
            TurnId = "turn_1",
            TryDrainGuidanceMessageAsync = _ =>
            {
                drains++;
                return Task.FromResult<ChatMessage?>(new ChatMessage(ChatRole.User, $"guidance {drains}"));
            }
        });

        await foreach (var _ in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "start")],
            new ChatOptions { Tools = [tool] }))
        {
        }

        Assert.Equal(3, drains);
        Assert.Equal(4, inner.Calls.Count);
        Assert.Contains(inner.Calls[1], message => message.Role == ChatRole.User && message.Text == "guidance 1");
        Assert.Contains(inner.Calls[2], message => message.Role == ChatRole.User && message.Text == "guidance 2");
        Assert.Contains(inner.Calls[3], message => message.Role == ChatRole.User && message.Text == "guidance 3");
        Assert.DoesNotContain(inner.Calls[3], message => message.Role == ChatRole.User && message.Text == "guidance 4");
    }

    [Fact]
    public async Task GetStreamingResponseAsync_UnknownToolCreatesFunctionResultByDefault()
    {
        var inner = new UnknownToolFakeChatClient();
        var client = new StreamingFunctionInvokingChatClient(inner);

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        Assert.Equal(2, inner.Calls.Count);
        var result = Assert.Single(inner.Calls[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Equal("call-1", result.CallId);
        Assert.Contains("not found", result.Result?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_TerminateOnUnknownCallsLeavesCallForCaller()
    {
        var inner = new UnknownToolFakeChatClient();
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            TerminateOnUnknownCalls = true
        };

        var updates = await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]));

        Assert.Single(inner.Calls);
        Assert.Contains(updates, update => update.Contents.OfType<FunctionCallContent>().Any(call => call.Name == "Missing"));
        Assert.DoesNotContain(updates, update => update.Contents.OfType<FunctionResultContent>().Any());
    }

    [Fact]
    public async Task GetStreamingResponseAsync_AllowsMoreThanPreviousDefaultToolRounds()
    {
        const int toolRoundCount = 105;
        var inner = new ManyToolCallsFakeChatClient(toolRoundCount);
        var tool = AIFunctionFactory.Create(() => "tool ok", name: "GetStatus");
        var client = new StreamingFunctionInvokingChatClient(inner);

        var updates = await CollectAsync(client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "start")],
            new ChatOptions { Tools = [tool] }));

        Assert.Equal(toolRoundCount + 1, inner.Calls.Count);
        Assert.Contains(updates, update => update.Text.Contains("done", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_RepairsDanglingHistoricalToolCallBeforeSampling()
    {
        var inner = new SingleReplyFakeChatClient();
        var client = new StreamingFunctionInvokingChatClient(inner);
        PromptRequestSnapshot? snapshot = null;

        using var scope = PreSamplingCompactionRuntimeScope.Set(new PreSamplingCompactionRuntimeContext
        {
            ThreadId = "thread_1",
            TurnId = "turn_1",
            Mode = "agent",
            TryCompactWithSnapshotAsync = (_, _, _) =>
            {
                return Task.FromResult<IReadOnlyList<ChatMessage>?>(null);
            },
            CaptureSnapshotAsync = (value, _) =>
            {
                snapshot = value;
                return Task.CompletedTask;
            },
            TryCompactAsync = (_, _) => throw new InvalidOperationException("legacy callback should not run")
        });

        var danglingCall = new FunctionCallContent("call-dangling", "GetStatus", new Dictionary<string, object?>());
        await foreach (var _ in client.GetStreamingResponseAsync(
            [
                new ChatMessage(ChatRole.User, "start"),
                new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[danglingCall]),
                new ChatMessage(ChatRole.User, "continue")
            ]))
        {
        }

        var call = Assert.Single(inner.Calls);
        Assert.Equal([ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.User], call.Select(message => message.Role).ToArray());
        var repairedResult = Assert.IsType<FunctionResultContent>(Assert.Single(call[2].Contents));
        Assert.Equal("call-dangling", repairedResult.CallId);
        Assert.Contains("repaired an incomplete historical tool call", repairedResult.Result?.ToString());

        var captured = Assert.IsType<PromptRequestSnapshot>(snapshot);
        Assert.Equal([ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.User], captured.Messages.Select(message => message.Role).ToArray());
    }

    [Fact]
    public async Task GetStreamingResponseAsync_PropagatesConversationIdAndSendsOnlyToolResults()
    {
        var inner = new ConversationIdFakeChatClient();
        var tool = AIFunctionFactory.Create(() => "tool ok", name: "GetStatus");
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool]
        };

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        Assert.Equal(2, inner.Calls.Count);
        Assert.Equal("conv-1", inner.Options[1]?.ConversationId);
        Assert.DoesNotContain(inner.Calls[1], message => message.Role == ChatRole.User);
        Assert.Contains(inner.Calls[1], message => message.Role == ChatRole.Tool);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ClearsConversationIdAfterPreSamplingCompaction()
    {
        var inner = new ConversationIdFakeChatClient();
        var tool = AIFunctionFactory.Create(() => "tool ok", name: "GetStatus");
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool]
        };
        var compactionCalls = 0;
        var replacement = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "compacted summary"),
            new(ChatRole.Tool, (IList<AIContent>)
            [
                new FunctionResultContent("call-1", "tool ok")
            ])
        };

        using var scope = PreSamplingCompactionRuntimeScope.Set(new PreSamplingCompactionRuntimeContext
        {
            TryCompactAsync = (_, _) =>
            {
                compactionCalls++;
                return Task.FromResult<IReadOnlyList<ChatMessage>?>(
                    compactionCalls == 2 ? replacement : null);
            }
        });

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        Assert.Equal(2, inner.Calls.Count);
        Assert.Null(inner.Options[1]?.ConversationId);
        Assert.Equal(["assistant:compacted summary", "tool:"], inner.Calls[1].Select(m => $"{m.Role}:{m.Text}"));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_IncludesSanitizedToolErrorByDefault()
    {
        var genericInner = new FailingToolFakeChatClient();
        var genericClient = new StreamingFunctionInvokingChatClient(genericInner)
        {
            AdditionalTools = [AIFunctionFactory.Create(ThrowBoom, name: "Fail")]
        };

        await foreach (var _ in genericClient.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        var genericResult = Assert.Single(genericInner.Calls[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Equal("Error: Function failed. Reason: boom", genericResult.Result);

        var detailedInner = new FailingToolFakeChatClient();
        var detailedClient = new StreamingFunctionInvokingChatClient(detailedInner)
        {
            AdditionalTools = [AIFunctionFactory.Create(ThrowBoom, name: "Fail")],
            IncludeDetailedErrors = true
        };

        await foreach (var _ in detailedClient.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        var detailedResult = Assert.Single(detailedInner.Calls[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Contains("InvalidOperationException", detailedResult.Result?.ToString(), StringComparison.Ordinal);
        Assert.Contains("boom", detailedResult.Result?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_RedactsSensitiveToolErrorText()
    {
        var inner = new FailingToolFakeChatClient();
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [AIFunctionFactory.Create(ThrowSensitiveError, name: "Fail")]
        };

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        var result = Assert.Single(inner.Calls[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        var text = Assert.IsType<string>(result.Result);
        Assert.Contains("Bearer ***", text, StringComparison.Ordinal);
        Assert.Contains("token=***", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", text, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WithDeepThinking_PreservesAssistantReasoningBeforeToolResult()
    {
        var inner = new DeepThinkingRoundTripFakeChatClient();
        var deepThinking = new DeepThinkingChatClient(
            inner,
            new AppConfig
            {
                Model = "deepseek-reasoner"
            },
            "deepseek-reasoner",
            "https://api.deepseek.com/v1");
        var tool = AIFunctionFactory.Create(() => "tool ok", name: "GetStatus");
        var client = new StreamingFunctionInvokingChatClient(deepThinking)
        {
            AdditionalTools = [tool]
        };

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        Assert.Equal(2, inner.Calls.Count);
        var secondRequest = inner.Calls[1];
        var assistantIndex = secondRequest.FindIndex(message => message.Role == ChatRole.Assistant);
        var toolIndex = secondRequest.FindIndex(message => message.Role == ChatRole.Tool);
        Assert.True(assistantIndex >= 0);
        Assert.True(toolIndex > assistantIndex);

        var assistant = secondRequest[assistantIndex];
        Assert.Contains(assistant.Contents, content => content is TextReasoningContent);
        Assert.Contains(assistant.Contents, content => content is FunctionCallContent);
        var raw = Assert.IsType<OpenAI.Chat.AssistantChatMessage>(assistant.RawRepresentation);
        using var document = JsonDocument.Parse(ModelReaderWriter.Write(raw).ToString());
        var root = document.RootElement;
        Assert.Equal("need status", root.GetProperty("reasoning_content").GetString());
        Assert.Equal("Checking.", root.GetProperty("content").GetString());
        Assert.Equal("call-1", Assert.Single(root.GetProperty("tool_calls").EnumerateArray()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WithStreamRetryingClient_PreservesToolResultAdjacencyAfterMetadataUpdate()
    {
        var inner = new MetadataThenToolCallFakeChatClient();
        var retrying = new StreamRetryingChatClient(inner, new StreamRetryOptions(1, TimeSpan.FromSeconds(30)));
        var tool = AIFunctionFactory.Create(() => "tool ok", name: "GetStatus");
        var client = new StreamingFunctionInvokingChatClient(retrying)
        {
            AdditionalTools = [tool]
        };

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        Assert.Equal(2, inner.Calls.Count);
        var secondRequest = inner.Calls[1];
        var assistantIndex = secondRequest.FindIndex(message =>
            message.Role == ChatRole.Assistant &&
            message.Contents.OfType<FunctionCallContent>().Any(call => call.CallId == "call-1"));
        var toolIndex = secondRequest.FindIndex(message =>
            message.Role == ChatRole.Tool &&
            message.Contents.OfType<FunctionResultContent>().Any(result => result.CallId == "call-1"));

        Assert.True(assistantIndex >= 0);
        Assert.Equal(assistantIndex + 1, toolIndex);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ExposesCurrentInvocationContext()
    {
        var inner = new RoundTripFakeChatClient();
        var tool = AIFunctionFactory.Create(() => "tool ok", name: "GetStatus");
        FunctionInvocationContext? captured = null;
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool],
            FunctionInvoker = (context, _) =>
            {
                captured = StreamingFunctionInvokingChatClient.CurrentContext;
                return ValueTask.FromResult<object?>("tool ok");
            }
        };

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        Assert.NotNull(captured);
        Assert.Equal("GetStatus", captured.Function.Name);
        Assert.Null(StreamingFunctionInvokingChatClient.CurrentContext);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ThrowsEmptyResponse_WhenProviderEmitsOnlyEmptyUpdate()
    {
        var client = new StreamingFunctionInvokingChatClient(
            new FixedUpdatesFakeChatClient(new ChatResponseUpdate(ChatRole.Assistant, [])));

        var ex = await Assert.ThrowsAsync<EmptyProviderResponseException>(() =>
            CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")])));

        Assert.Contains("empty streaming response", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ThrowsEmptyResponse_WhenProviderEmitsOnlyUsage()
    {
        var client = new StreamingFunctionInvokingChatClient(
            new FixedUpdatesFakeChatClient(new ChatResponseUpdate(ChatRole.Assistant, [
                new UsageContent(new UsageDetails
                {
                    InputTokenCount = 10,
                    OutputTokenCount = 0
                })
            ])));

        var ex = await Assert.ThrowsAsync<EmptyProviderResponseException>(() =>
            CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")])));

        Assert.Contains("assistant content", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_EmitsToolExecutionCompletionAsEachParallelToolFinishes()
    {
        var inner = new ParallelToolsFakeChatClient();
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AllowConcurrentInvocation = true,
            AdditionalTools =
            [
                AIFunctionFactory.Create(() => "unused", name: "Slow"),
                AIFunctionFactory.Create(() => "unused", name: "Fast")
            ],
            FunctionInvoker = async (context, _) =>
            {
                if (context.CallContent.CallId == "call-slow")
                {
                    await Task.Delay(100);
                    return "slow result";
                }

                await Task.Delay(10);
                return "fast result";
            }
        };
        var turn = new SessionTurn { Id = "turn_1", ThreadId = "thread_1", StartedAt = DateTimeOffset.UtcNow };
        var completed = new List<SessionItem>();

        using var scope = ToolExecutionRuntimeScope.Set(new ToolExecutionRuntimeContext
        {
            TurnId = turn.Id,
            Turn = turn,
            NextItemSequence = () => turn.Items.Count + 1,
            EmitItemStarted = _ => { },
            EmitItemCompleted = item => completed.Add(item),
            SupportsToolExecutionLifecycle = true
        });
        RegisterToolExecution(turn, "item_slow", "call-slow", "Slow");
        RegisterToolExecution(turn, "item_fast", "call-fast", "Fast");

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        Assert.Equal(["call-fast", "call-slow"],
            completed.Select(item => Assert.IsType<ToolExecutionPayload>(item.Payload).CallId));
        Assert.Equal(2, inner.Calls.Count);
        var resultCallIds = inner.Calls[1]
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Select(result => result.CallId)
            .ToList();
        Assert.Equal(["call-slow", "call-fast"], resultCallIds);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_NormalizesNullFunctionCallArguments()
    {
        var inner = new NullArgumentsToolFakeChatClient();
        var tool = AIFunctionFactory.Create(() => "tool ok", name: "GetStatus");
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool]
        };

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        Assert.Equal(2, inner.Calls.Count);
        var functionCall = Assert.Single(inner.Calls[1].SelectMany(message => message.Contents).OfType<FunctionCallContent>());
        Assert.NotNull(functionCall.Arguments);
        Assert.Empty(functionCall.Arguments);
    }

    private static string ThrowBoom() => throw new InvalidOperationException("boom");

    private static string ThrowSensitiveError() =>
        throw new InvalidOperationException("Request failed.\nAuthorization: Bearer abc123 token=secret-token \u001b[31mred");

    private static void RegisterToolExecution(
        SessionTurn turn,
        string itemId,
        string callId,
        string toolName)
    {
        var item = new SessionItem
        {
            Id = itemId,
            TurnId = turn.Id,
            Type = ItemType.ToolExecution,
            Status = ItemStatus.Started,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new ToolExecutionPayload
            {
                CallId = callId,
                ToolName = toolName,
                Status = "inProgress"
            }
        };
        turn.Items.Add(item);
        ToolExecutionRuntimeScope.Current!.RegisterPending(new PendingToolExecutionRegistration
        {
            CallId = callId,
            ToolName = toolName,
            Item = item
        });
    }

    private static async Task<List<ChatResponseUpdate>> CollectAsync(IAsyncEnumerable<ChatResponseUpdate> updates)
    {
        var result = new List<ChatResponseUpdate>();
        await foreach (var update in updates)
            result.Add(update);
        return result;
    }

    private static void AssertToolChoiceNone(ChatOptions? options)
    {
        Assert.NotNull(options);
        var factory = options.RawRepresentationFactory;
        Assert.NotNull(factory);
        var raw = Assert.IsType<OpenAI.Chat.ChatCompletionOptions>(factory.Invoke(null!));
        using var document = JsonDocument.Parse(ModelReaderWriter.Write(raw).ToString());
        Assert.Equal("none", document.RootElement.GetProperty("tool_choice").GetString());
    }

    private sealed class RoundTripFakeChatClient : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(chatMessages.ToList());
            if (Calls.Count == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent("call-1", "GetStatus", new Dictionary<string, object?>())
                ]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class SingleReplyFakeChatClient : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "done")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(chatMessages.ToList());
            yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class FixedUpdatesFakeChatClient(params ChatResponseUpdate[] updates) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in updates)
                yield return update;
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class UnknownToolFakeChatClient : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(chatMessages.ToList());
            if (Calls.Count == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent("call-1", "Missing", new Dictionary<string, object?>())
                ]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class AlwaysCallsToolFakeChatClient : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];
        public List<ChatOptions?> Options { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(chatMessages.ToList());
            Options.Add(options);
            if (Options.Count == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent("call-1", "GetStatus", new Dictionary<string, object?>())
                ]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ManyToolCallsFakeChatClient(int toolRoundCount) : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(chatMessages.ToList());
            if (Calls.Count <= toolRoundCount)
            {
                var callId = $"call-{Calls.Count}";
                yield return new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent(callId, "GetStatus", new Dictionary<string, object?>())
                ]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ConversationIdFakeChatClient : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];
        public List<ChatOptions?> Options { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(chatMessages.ToList());
            Options.Add(options);
            if (Calls.Count == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent("call-1", "GetStatus", new Dictionary<string, object?>())
                ])
                {
                    ConversationId = "conv-1"
                };
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class FailingToolFakeChatClient : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(chatMessages.ToList());
            if (Calls.Count == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent("call-1", "Fail", new Dictionary<string, object?>())
                ]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class DeepThinkingRoundTripFakeChatClient : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(chatMessages.ToList());
            if (Calls.Count == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("need status")]);
                yield return new ChatResponseUpdate(ChatRole.Assistant, [
                    new TextContent("Checking."),
                    new FunctionCallContent("call-1", "GetStatus", new Dictionary<string, object?>())
                ]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class MetadataThenToolCallFakeChatClient : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(chatMessages.ToList());
            if (Calls.Count == 1)
            {
                yield return new ChatResponseUpdate { Role = ChatRole.Assistant };
                yield return new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent("call-1", "GetStatus", new Dictionary<string, object?>())
                ]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ToolCallFakeChatClient(string toolName, IDictionary<string, object?> arguments) : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(chatMessages.ToList());
            if (Calls.Count == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent("call-1", toolName, new Dictionary<string, object?>(arguments))
                ]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class NullArgumentsToolFakeChatClient : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(chatMessages.ToList());
            if (Calls.Count == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent("call-1", "GetStatus", null)
                ]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ParallelToolsFakeChatClient : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(chatMessages.ToList());
            if (Calls.Count == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent("call-slow", "Slow", new Dictionary<string, object?>()),
                    new FunctionCallContent("call-fast", "Fast", new Dictionary<string, object?>())
                ]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
