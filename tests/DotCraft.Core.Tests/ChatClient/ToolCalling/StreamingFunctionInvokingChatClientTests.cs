using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Hooks;
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
    public async Task GetStreamingResponseAsync_AddsPostToolUseAdditionalContextToNextModelRequestOnly()
    {
        using var workspace = new TempDirectory("HookFeedback_");
        var inner = new RoundTripFakeChatClient();
        var tool = new HookWrappedFunction(
            AIFunctionFactory.Create(() => "tool ok", name: "GetStatus"),
            CreateHookRunner(workspace.Root, HookEvent.PostToolUse, JsonAdditionalContextCommand("PostToolUse", "SECURITY_CONTEXT")));
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool]
        };

        var updates = await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]));

        Assert.Equal(2, inner.Calls.Count);
        var reminder = Assert.Single(inner.Calls[1], message =>
            message.Role == ChatRole.User &&
            message.Text.Contains("Lifecycle Hook Feedback", StringComparison.Ordinal));
        Assert.Contains("PostToolUse additionalContext", reminder.Text, StringComparison.Ordinal);
        Assert.Contains("SECURITY_CONTEXT", reminder.Text, StringComparison.Ordinal);

        var toolResult = Assert.Single(updates.SelectMany(update => update.Contents).OfType<FunctionResultContent>());
        Assert.Equal("tool ok", toolResult.Result?.ToString());
        Assert.DoesNotContain("SECURITY_CONTEXT", toolResult.Result?.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(updates, update =>
            update.Contents.OfType<TextContent>().Any(text =>
                text.Text.Contains("SECURITY_CONTEXT", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_DoesNotAddHookFeedbackMessageWithoutAdditionalContext()
    {
        using var workspace = new TempDirectory("HookFeedback_");
        var inner = new RoundTripFakeChatClient();
        var tool = new HookWrappedFunction(
            AIFunctionFactory.Create(() => "tool ok", name: "GetStatus"),
            CreateHookRunner(workspace.Root, HookEvent.PostToolUse, JsonNoAdditionalContextCommand()));
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool]
        };

        var updates = await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]));

        Assert.Equal(2, inner.Calls.Count);
        Assert.DoesNotContain(inner.Calls[1], message =>
            message.Role == ChatRole.User &&
            message.Text.Contains("Lifecycle Hook Feedback", StringComparison.Ordinal));
        var toolResult = Assert.Single(updates.SelectMany(update => update.Contents).OfType<FunctionResultContent>());
        Assert.Equal("tool ok", toolResult.Result?.ToString());
    }

    [Fact]
    public async Task GetStreamingResponseAsync_AddsPostToolUseExitCodeTwoFeedbackToNextModelRequestOnly()
    {
        using var workspace = new TempDirectory("HookFeedback_");
        var inner = new RoundTripFakeChatClient();
        var tool = new HookWrappedFunction(
            AIFunctionFactory.Create(() => "tool ok", name: "GetStatus"),
            CreateHookRunner(workspace.Root, HookEvent.PostToolUse, JsonBlockCommand("BLOCK_REASON")));
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool]
        };

        var updates = await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]));

        Assert.Equal(2, inner.Calls.Count);
        var reminder = Assert.Single(inner.Calls[1], message =>
            message.Role == ChatRole.User &&
            message.Text.Contains("Lifecycle Hook Feedback", StringComparison.Ordinal));
        Assert.Contains("PostToolUse exit-code-2 feedback", reminder.Text, StringComparison.Ordinal);
        Assert.Contains("BLOCK_REASON", reminder.Text, StringComparison.Ordinal);

        var toolResult = Assert.Single(updates.SelectMany(update => update.Contents).OfType<FunctionResultContent>());
        Assert.Equal("tool ok", toolResult.Result?.ToString());
        Assert.DoesNotContain("BLOCK_REASON", toolResult.Result?.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(updates, update =>
            update.Contents.OfType<TextContent>().Any(text =>
                text.Text.Contains("BLOCK_REASON", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_AddsPostToolUseFailureAdditionalContextToNextModelRequestOnly()
    {
        using var workspace = new TempDirectory("HookFeedback_");
        var inner = new FailingToolFakeChatClient();
        var tool = new HookWrappedFunction(
            AIFunctionFactory.Create(ThrowBoom, name: "Fail"),
            CreateHookRunner(workspace.Root, HookEvent.PostToolUseFailure, JsonAdditionalContextCommand("PostToolUseFailure", "FAILURE_CONTEXT")));
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool]
        };

        var updates = await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]));

        Assert.Equal(2, inner.Calls.Count);
        var reminder = Assert.Single(inner.Calls[1], message =>
            message.Role == ChatRole.User &&
            message.Text.Contains("Lifecycle Hook Feedback", StringComparison.Ordinal));
        Assert.Contains("PostToolUseFailure additionalContext", reminder.Text, StringComparison.Ordinal);
        Assert.Contains("FAILURE_CONTEXT", reminder.Text, StringComparison.Ordinal);

        var toolResult = Assert.Single(updates.SelectMany(update => update.Contents).OfType<FunctionResultContent>());
        Assert.Contains("Function failed", toolResult.Result?.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("FAILURE_CONTEXT", toolResult.Result?.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(updates, update =>
            update.Contents.OfType<TextContent>().Any(text =>
                text.Text.Contains("FAILURE_CONTEXT", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_NotifiesToolHandlerFinishedAfterHandlerRuns()
    {
        var inner = new RoundTripFakeChatClient();
        var tool = AIFunctionFactory.Create(() => "tool ok", name: "GetStatus");
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool]
        };
        var callbacks = new List<(string ToolName, string CallId)>();

        using var scope = TurnGuidanceRuntimeScope.Set(new TurnGuidanceRuntimeContext
        {
            ThreadId = "thread_1",
            TurnId = "turn_1",
            TryDrainGuidanceMessageAsync = _ => Task.FromResult<ChatMessage?>(null),
            OnToolHandlerFinishedAsync = (toolName, callId, _) =>
            {
                callbacks.Add((toolName, callId));
                return Task.CompletedTask;
            }
        });

        await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]));

        var callback = Assert.Single(callbacks);
        Assert.Equal("GetStatus", callback.ToolName);
        Assert.Equal("call-1", callback.CallId);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_DoesNotNotifyToolHandlerFinishedForPolicyDeniedOrMissingTool()
    {
        var deniedInner = new RoundTripFakeChatClient();
        var deniedClient = new StreamingFunctionInvokingChatClient(deniedInner)
        {
            AdditionalTools = [AIFunctionFactory.Create(() => "tool ok", name: "GetStatus")],
            ToolCallPolicy = _ => ModeToolPolicyDecision.DenyRecoverable("TOOL_POLICY_DENIED")
        };
        var missingInner = new UnknownToolFakeChatClient();
        var missingClient = new StreamingFunctionInvokingChatClient(missingInner);
        var callbacks = 0;

        using var scope = TurnGuidanceRuntimeScope.Set(new TurnGuidanceRuntimeContext
        {
            ThreadId = "thread_1",
            TurnId = "turn_1",
            TryDrainGuidanceMessageAsync = _ => Task.FromResult<ChatMessage?>(null),
            OnToolHandlerFinishedAsync = (_, _, _) =>
            {
                callbacks++;
                return Task.CompletedTask;
            }
        });

        await CollectAsync(deniedClient.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]));
        await CollectAsync(missingClient.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]));

        Assert.Equal(0, callbacks);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_DoesNotNotifyToolHandlerFinishedForCancelledHandler()
    {
        var inner = new RoundTripFakeChatClient();
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [AIFunctionFactory.Create(() => "tool ok", name: "GetStatus")],
            FunctionInvoker = (_, _) => throw new OperationCanceledException("cancelled")
        };
        var callbacks = 0;

        using var scope = TurnGuidanceRuntimeScope.Set(new TurnGuidanceRuntimeContext
        {
            ThreadId = "thread_1",
            TurnId = "turn_1",
            TryDrainGuidanceMessageAsync = _ => Task.FromResult<ChatMessage?>(null),
            OnToolHandlerFinishedAsync = (_, _, _) =>
            {
                callbacks++;
                return Task.CompletedTask;
            }
        });

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")])));

        Assert.Equal(0, callbacks);
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
        var bridge = new RecordingProviderHistoryBridge();
        var inner = new ProviderHistoryBridgeFakeChatClient(bridge);
        var client = new StreamingFunctionInvokingChatClient(inner);
        var callbackCalls = 0;
        var replacement = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "compacted summary"),
            new(ChatRole.User, "latest user")
        };

        using var scope = PreSamplingCompactionRuntimeScope.Set(new PreSamplingCompactionRuntimeContext
        {
            TryCompactAsync = (messages, _, _) =>
            {
                callbackCalls++;
                Assert.Contains(messages, message => message.Role == ChatRole.User && message.Text == "start");
                return NeutralCompactionResult(replacement);
            }
        });

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        Assert.Equal(1, callbackCalls);
        var call = Assert.Single(inner.Calls);
        Assert.Equal(["assistant:compacted summary", "user:latest user"], call.Select(m => $"{m.Role}:{m.Text}"));
        var providerReplacement = Assert.Single(bridge.Replacements);
        Assert.Equal("compaction", providerReplacement.Reason);
        Assert.Same(replacement, providerReplacement.Messages);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ProviderNativeReplacementKeepsNeutralMessages()
    {
        var bridge = new RecordingProviderHistoryBridge();
        var inner = new ProviderHistoryBridgeFakeChatClient(bridge);
        var client = new StreamingFunctionInvokingChatClient(inner);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "start")
        };
        using var document = JsonDocument.Parse(
            """{"type":"compaction","encrypted_content":"YWJj"}""");
        var threshold = new CompactionThreshold(10, 8, 9, 10, 11, 0.1);

        using var scope = PreSamplingCompactionRuntimeScope.Set(new PreSamplingCompactionRuntimeContext
        {
            TryCompactAsync = (_, _, _) => Task.FromResult<CompactionExecutionResult?>(
                new CompactionExecutionResult(
                    new CompactionStatus(
                        CompactionOutcome.Partial,
                        10,
                        2,
                        threshold,
                        threshold with { Tokens = 2 }),
                    CompactionBackendIds.ChatGptResponsesCompact,
                    new CompactionReplacement.ProviderNative(
                        ProviderHistorySchema.OpenAIResponsesProtocol,
                        [document.RootElement.Clone()],
                        1,
                        "turn_1",
                        2)))
        });

        await foreach (var _ in client.GetStreamingResponseAsync(messages))
        {
        }

        Assert.Empty(bridge.Replacements);
        var call = Assert.Single(inner.Calls);
        Assert.Equal(["user:start"], call.Select(message => $"{message.Role}:{message.Text}"));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_RequestSanitizationDoesNotReplaceCanonicalProviderHistory()
    {
        var bridge = new RecordingProviderHistoryBridge();
        var inner = new ProviderHistoryBridgeFakeChatClient(bridge);
        var client = new StreamingFunctionInvokingChatClient(inner);
        var messages = new List<ChatMessage>
        {
            new(
                ChatRole.Assistant,
                [new FunctionCallContent("call-1", "GetStatus", new Dictionary<string, object?>())]),
            new(
                ChatRole.Tool,
                [
                    new TextReasoningContent("must stay canonical"),
                    new FunctionResultContent("call-1", "tool result")
                ])
        };

        await foreach (var _ in client.GetStreamingResponseAsync(messages))
        {
        }

        Assert.Empty(bridge.Replacements);
        var request = Assert.Single(inner.Calls);
        Assert.Equal(2, request.Count);
        var tool = request[1];
        Assert.Single(tool.Contents);
        Assert.IsType<FunctionResultContent>(tool.Contents[0]);
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
            TryCompactAsync = (_, _, _) => NeutralCompactionResult(replacement),
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
            TryCompactWithSnapshotAsync = (_, snapshot, _, _) =>
            {
                compactionSnapshot = snapshot;
                return NeutralCompactionResult(replacement);
            },
            TryCompactAsync = (_, _, _) => throw new InvalidOperationException("legacy callback should not run")
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
    public async Task GetStreamingResponseAsync_UsesFinalToolFreeRequestAtIterationLimit()
    {
        var inner = new ManyToolCallsFakeChatClient(toolRoundCount: 10);
        var tool = AIFunctionFactory.Create(() => "tool ok", name: "GetStatus");
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            MaximumIterationsPerRequest = 2
        };

        await foreach (var _ in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "start")],
                           new ChatOptions
                           {
                               Tools = [tool],
                               ToolMode = ChatToolMode.RequireAny
                           }))
        {
        }

        Assert.Equal(3, inner.Calls.Count);
        Assert.NotEmpty(inner.Options[0].Tools!);
        Assert.NotEmpty(inner.Options[1].Tools!);
        Assert.Null(inner.Options[2].Tools);
        Assert.Null(inner.Options[2].ToolMode);
    }

    [Fact]
    public void MaximumIterationsPerRequest_RejectsValuesBelowOne()
    {
        var client = new StreamingFunctionInvokingChatClient(new SingleReplyFakeChatClient());

        Assert.Equal(40, client.MaximumIterationsPerRequest);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => client.MaximumIterationsPerRequest = 0);
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
        Assert.Equal(ToolErrorCodes.NotFound, StreamingFunctionInvokingChatClient.GetToolResultErrorCode(result));
        Assert.Null(result.Exception);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_CompletesWhenProviderResponseAfterToolResultIsEmpty()
    {
        var inner = new PostToolEmptyResponseFakeChatClient(emptyResponsesAfterTool: 1);
        var tool = AIFunctionFactory.Create(() => "tool ok", name: "GetStatus");
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool]
        };

        var updates = await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]));

        Assert.Equal(2, inner.Calls.Count);
        Assert.Contains(updates, update => update.Contents.OfType<FunctionResultContent>().Any(result => result.CallId == "call-1"));
        Assert.DoesNotContain(updates, update => update.Text.Contains("done", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ThrowsWhenPostToolProviderResponseContainsError()
    {
        var inner = new PostToolEmptyResponseFakeChatClient(
            emptyResponsesAfterTool: 0,
            errorAfterTool: "provider failed");
        var tool = AIFunctionFactory.Create(() => "tool ok", name: "GetStatus");
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool]
        };

        var ex = await Assert.ThrowsAsync<EmptyProviderResponseException>(async () =>
            await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")])));

        Assert.Contains("provider failed", ex.Message, StringComparison.Ordinal);
        Assert.Equal(2, inner.Calls.Count);
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
    public async Task GetStreamingResponseAsync_AllowsConfiguredLongToolLoops()
    {
        const int toolRoundCount = 105;
        var inner = new ManyToolCallsFakeChatClient(toolRoundCount);
        var tool = AIFunctionFactory.Create(() => "tool ok", name: "GetStatus");
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            MaximumIterationsPerRequest = toolRoundCount + 1
        };

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
            TryCompactWithSnapshotAsync = (_, _, _, _) =>
            {
                return NeutralCompactionResult(null);
            },
            CaptureSnapshotAsync = (value, _) =>
            {
                snapshot = value;
                return Task.CompletedTask;
            },
            TryCompactAsync = (_, _, _) => throw new InvalidOperationException("legacy callback should not run")
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
            TryCompactAsync = (_, _, _) =>
            {
                compactionCalls++;
                return NeutralCompactionResult(
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
    public async Task GetStreamingResponseAsync_HidesToolErrorDetailsByDefault()
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
        Assert.Equal("Error: Function failed.", genericResult.Result);

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
            AdditionalTools = [AIFunctionFactory.Create(ThrowSensitiveError, name: "Fail")],
            IncludeDetailedErrors = true
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
                ProviderId = "test",
                ProviderPreferences = new() { ["test"] = new ModelPreference { Model = "deepseek-reasoner"  } }
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
    public async Task GetStreamingResponseAsync_ThrowsEmptyResponse_WhenProviderEmitsOnlyErrorContent()
    {
        var client = new StreamingFunctionInvokingChatClient(
            new FixedUpdatesFakeChatClient(new ChatResponseUpdate(ChatRole.Assistant, [
                new ErrorContent("provider returned an empty error response")
            ])));

        var ex = await Assert.ThrowsAsync<EmptyProviderResponseException>(() =>
            CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")])));

        Assert.Contains("provider returned an empty error response", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ThrowsContextOverflow_WhenErrorContentSaysPromptTooLong()
    {
        var client = new StreamingFunctionInvokingChatClient(
            new FixedUpdatesFakeChatClient(new ChatResponseUpdate(ChatRole.Assistant, [
                new ErrorContent("context_length_exceeded: input exceeds the context window")
            ])));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")])));

        Assert.IsNotType<EmptyProviderResponseException>(ex);
        Assert.Contains("context_length_exceeded", ex.Message, StringComparison.Ordinal);
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
            ThreadId = turn.ThreadId,
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

    private static Task<CompactionExecutionResult?> NeutralCompactionResult(
        IReadOnlyList<ChatMessage>? messages)
    {
        if (messages is null)
            return Task.FromResult<CompactionExecutionResult?>(null);

        var threshold = new CompactionThreshold(
            Tokens: 1,
            AutoThreshold: 1,
            WarningThreshold: 1,
            ErrorThreshold: 1,
            BlockingLimit: 1,
            PercentLeft: 0);
        return Task.FromResult<CompactionExecutionResult?>(
            new CompactionExecutionResult(
                new CompactionStatus(
                    CompactionOutcome.Partial,
                    EstimatedTokensBefore: 1,
                    EstimatedTokensAfter: 1,
                    threshold,
                    threshold),
                CompactionBackendIds.LocalSummary,
                new CompactionReplacement.Neutral(messages)));
    }

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

    private static HookRunner CreateHookRunner(string workspacePath, HookEvent evt, string command) =>
        new(new HooksFileConfig
        {
            Hooks =
            {
                [evt.ToString()] =
                [
                    new HookMatcherGroup
                    {
                        Hooks =
                        [
                            new HookEntry
                            {
                                Type = "command",
                                Command = command
                            }
                        ]
                    }
                ]
            }
        }, workspacePath);

    private static string JsonAdditionalContextCommand(string eventName, string output)
    {
        var json = "{\"hookSpecificOutput\":{\"hookEventName\":\"" + eventName + "\",\"additionalContext\":\"" + output + "\"}}";
        return OperatingSystem.IsWindows()
            ? $"Write-Output '{json}'"
            : $"printf '%s\\n' '{json}'";
    }

    private static string JsonNoAdditionalContextCommand()
    {
        const string json = "{\"hookSpecificOutput\":{\"hookEventName\":\"PostToolUse\"}}";
        return OperatingSystem.IsWindows()
            ? $"Write-Output '{json}'"
            : $"printf '%s\\n' '{json}'";
    }

    private static string JsonBlockCommand(string reason)
    {
        var json = "{\"decision\":\"block\",\"reason\":\"" + reason + "\"}";
        return OperatingSystem.IsWindows()
            ? $"Write-Output '{json}'; exit 2"
            : $"printf '%s\\n' '{json}'; exit 2";
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string prefix)
        {
            Root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                prefix + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch { }
        }
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

    private sealed class ProviderHistoryBridgeFakeChatClient(
        RecordingProviderHistoryBridge bridge) : IChatClient
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
            yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(IProviderConversationHistoryBridge) ? bridge : null;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingProviderHistoryBridge : IProviderConversationHistoryBridge
    {
        public List<(IReadOnlyList<ChatMessage> Messages, string Reason)> Replacements { get; } = [];

        public ValueTask HistoryReplacedAsync(
            IReadOnlyList<ChatMessage> messages,
            ChatOptions? options,
            string reason,
            CancellationToken cancellationToken)
        {
            Replacements.Add((messages, reason));
            return ValueTask.CompletedTask;
        }

        public void MarkProjectionCovered(IReadOnlyList<ChatMessage> messages)
        {
        }

        public string? BeginAttempt() => null;

        public ValueTask AbortAttemptAsync(string? attemptId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public void EndAttempt(string? attemptId)
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

    private sealed class PostToolEmptyResponseFakeChatClient(
        int emptyResponsesAfterTool,
        string? errorAfterTool = null) : IChatClient
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
            else if (!string.IsNullOrWhiteSpace(errorAfterTool))
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new ErrorContent(errorAfterTool)]);
            }
            else if (Calls.Count <= emptyResponsesAfterTool + 1)
            {
                await Task.CompletedTask;
                yield break;
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
        public List<ChatOptions> Options { get; } = [];

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
            Options.Add(options?.Clone() ?? new ChatOptions());
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
