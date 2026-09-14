using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Sessions;
using DotCraft.Memory;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

#pragma warning disable OPENAI001

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceMemoryConsolidationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AutomaticMemoryForkAndFallbackUseExplicitMessages(bool forceFallback, bool toolContinuation)
    {
        var memory = new MemoryStore(_tempDir);
        var transport = new MemoryTransport(forceFallback, toolContinuation);
        using var adapter = new OpenAIResponsesToolSearchChatClient(
            new ResponsesClient("sk-test"), "gpt-4o-mini", new StaticChatClient("unused"), transport);
        var consolidator = new MemoryForkConsolidator(new MaintenanceForkRunner(adapter),
            new MemoryConsolidator(adapter, memory), memory, "gpt-4o-mini", "gpt-4o-mini", workspaceRoot: _tempDir);
        using var main = new StreamingFunctionInvokingChatClient(new StaticChatClient("understood"));
        await using var factory = CreateAgentFactory(main, consolidator,
            config => config.Providers["openai"].Protocol = DotCraft.Configuration.ModelProviderProtocols.OpenAIResponses);
        var service = CreateService(factory, main);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        var completed = CollectThreadEventsAsync(service, thread.Id, events => events.Any(IsConsolidationTerminal));

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("Remember that the color is blue.")]));
        var events = await completed;

        Assert.Contains(events, item => IsSystemEvent(item, "consolidated"));
        Assert.Equal(forceFallback || toolContinuation ? 2 : 1, transport.Inputs.Count);
        Assert.Contains("memory_consolidation", transport.Inputs[0]);
        if (forceFallback)
            Assert.Contains("Consolidate durable memory", transport.Inputs[1]);
        if (toolContinuation)
            Assert.Contains("function_call_output", transport.Inputs[1]);
        Assert.All(transport.Contexts, context =>
        {
            Assert.Equal(ProviderRequestKind.Memory, context.CurrentIdentity.RequestKind);
            Assert.Equal(thread.Id, context.CurrentIdentity.CurrentThreadId);
            Assert.Null(context.History);
            Assert.Null(context.Compaction);
        });
        Assert.Contains("blue", memory.ReadLongTerm());
        Assert.Single(thread.Turns);
        Assert.DoesNotContain(thread.Turns[0].Items,
            item => item.AsAgentMessage?.Text?.Contains("memory_update", StringComparison.Ordinal) == true);
    }

    private sealed class MemoryTransport(bool forceFallback, bool toolContinuation) : IResponsesToolSearchTransport
    {
        public List<string> Inputs { get; } = [];
        public List<ProviderRequestContext> Contexts { get; } = [];
        public async IAsyncEnumerable<StreamingResponseUpdate> CreateResponseStreamingAsync(
            CreateResponseOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(ModelReaderWriter.Write(options).ToString());
            Inputs.Add(body.RootElement.GetProperty("input").GetRawText());
            Contexts.Add(ProviderRequestContextScope.Current!);
            await Task.Yield();
            if (toolContinuation && Inputs.Count == 1)
            {
                yield return ModelReaderWriter.Read<StreamingResponseUpdate>(BinaryData.FromString("""
                    {"type":"response.output_item.done","sequence_number":1,"output_index":0,
                     "item":{"type":"function_call","id":"fc_memory","call_id":"call_memory",
                             "name":"unavailable_memory_tool","arguments":"{}","status":"completed"}}
                    """))!;
                yield break;
            }
            yield return new StreamingResponseOutputTextDeltaUpdate
            {
                SequenceNumber = 1, ItemId = $"msg_memory_{Inputs.Count}", ContentIndex = 0, OutputIndex = 0,
                Delta = forceFallback && Inputs.Count == 1 ? "No structured result." :
                    """{"memory_update":"Color: blue","history_entry":"[2026-09-14] Learned the color blue."}"""
            };
        }
    }

    [Fact]
    public async Task AutomaticMemoryUsesDetachedContextForEachQueuedTurn()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inheritedValue = new AsyncLocal<string?> { Value = "parent turn" };
        var contexts = new List<(ProviderRequestContext? Context, string? Inherited)>();
        var consolidator = new FakeMemoryConsolidator(MemoryConsolidationResult.Skipped("no_changes"), async () =>
        {
            var context = ProviderRequestContextScope.Current;
            contexts.Add((context, inheritedValue.Value));
            context?.ConversationState?.TryCaptureContinuationState($"memory-{contexts.Count}");
            if (contexts.Count == 1)
            {
                firstStarted.TrySetResult();
                await release.Task;
            }
            else
                secondStarted.TrySetResult();
        });
        var mainTransport = new MainHistoryTransport();
        using var mainAdapter = new OpenAIResponsesToolSearchChatClient(
            new ResponsesClient("sk-test"), "gpt-4o-mini", new StaticChatClient("unused"), mainTransport);
        using var chatClient = new StreamingFunctionInvokingChatClient(mainAdapter);
        await using var factory = CreateAgentFactory(chatClient, consolidator,
            config => config.Providers["openai"].Protocol = DotCraft.Configuration.ModelProviderProtocols.OpenAIResponses);
        var service = CreateService(factory, chatClient);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        var completed = CollectThreadEventsAsync(service, thread.Id,
            events => events.Count(IsConsolidationTerminal) >= 2, TimeSpan.FromSeconds(30));

        try
        {
            await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("first")]));
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("second")]));
        }
        finally
        {
            release.TrySetResult();
        }
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await completed;

        Assert.Equal(2, contexts.Count);
        Assert.All(contexts, item =>
        {
            Assert.Null(item.Inherited);
            Assert.NotNull(item.Context);
            Assert.Equal(ProviderRequestKind.Memory, item.Context.CurrentIdentity.RequestKind);
            Assert.Equal(thread.Id, item.Context.CurrentIdentity.CurrentThreadId);
            Assert.Null(item.Context.History);
            Assert.Null(item.Context.Compaction);
        });
        Assert.Equal("turn_001", contexts[0].Context!.CurrentIdentity.TurnId);
        Assert.Equal("turn_002", contexts[1].Context!.CurrentIdentity.TurnId);
        Assert.NotSame(contexts[0].Context!.ConversationState, contexts[1].Context!.ConversationState);
        Assert.Equal("memory-1", contexts[0].Context!.ConversationState!.ContinuationState);
        Assert.Equal("parent turn", inheritedValue.Value);
        Assert.Equal(2, mainTransport.Contexts.Count);
        for (var index = 0; index < mainTransport.Contexts.Count; index++)
        {
            var mainContext = mainTransport.Contexts[index];
            Assert.Equal($"main-turn_00{index + 1}", mainContext.ConversationState!.ContinuationState);
            Assert.NotSame(mainContext.ConversationState, contexts[index].Context!.ConversationState);
            var history = Assert.IsType<OpenAIResponsesProviderHistoryContext>(mainContext.History);
            var messages = new List<ChatMessage> { new(ChatRole.User, "first"), new(ChatRole.Assistant, "ok") };
            if (index == 1)
                messages.AddRange([new(ChatRole.User, "second"), new(ChatRole.Assistant, "ok")]);
            var snapshot = await history.CaptureInputAsync(ProviderCompactionPhase.PreTurn,
                messages, null, CancellationToken.None);
            Assert.Equal(messages.Count, snapshot.CoveredMessageCount);
            Assert.Equal(messages.Count, snapshot.Items.Count);
            Assert.DoesNotContain(snapshot.Items, item => item.Payload.GetRawText().Contains("memory-"));
        }
    }

    private sealed class MainHistoryTransport : IResponsesToolSearchTransport
    {
        public List<ProviderRequestContext> Contexts { get; } = [];
        public async IAsyncEnumerable<StreamingResponseUpdate> CreateResponseStreamingAsync(
            CreateResponseOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var context = ProviderRequestContextScope.Current!;
            Contexts.Add(context);
            context.ConversationState!.TryCaptureContinuationState($"main-{context.CurrentIdentity.TurnId}");
            await Task.Yield();
            yield return new StreamingResponseOutputTextDeltaUpdate
            {
                SequenceNumber = 1, ItemId = $"msg_{context.CurrentIdentity.TurnId}", ContentIndex = 0,
                OutputIndex = 0, Delta = "ok"
            };
            yield return ModelReaderWriter.Read<StreamingResponseUpdate>(BinaryData.FromString($$$"""
                {"type":"response.output_item.done","sequence_number":2,"output_index":0,
                 "item":{"type":"message","id":"msg_{{{context.CurrentIdentity.TurnId}}}","role":"assistant",
                         "status":"completed","content":[{"type":"output_text","text":"ok","annotations":[]}]}}
                """))!;
        }
    }
}
