using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Xunit;

#pragma warning disable OPENAI001

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceInterruptionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponsesCancellation_RetainsCompletedAndPartialOutputAfterColdResume(bool disposeOnCancellation)
    {
        var transport = new InterruptedResponsesTransport();
        using var adapter = new OpenAIResponsesToolSearchChatClient(
            new ResponsesClient("sk-test"), "gpt-test", new InterruptibleClient(), transport);
        using var retry = new StreamRetryingChatClient(adapter, new StreamRetryOptions(1, TimeSpan.FromSeconds(30)));
        await using var factory = Factory(ModelProviderProtocols.OpenAIResponses);
        var store = new ThreadStore(root);
        var service = Service(factory, new InterruptibleClient { Block = false }, store);
        var thread = await service.CreateThreadAsync(Identity());
        await Drain(service.SubmitInputAsync(thread.Id, [new TextContent("initial")]));
        await SeedCompactedHistoryAsync(new SessionPersistenceService(store), thread);
        var agent = new StreamingFunctionInvokingChatClient(retry).AsAIAgent(new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "tool-result", name: "GetStatus")]
        });
        service = new SessionService(factory, agent, new SessionPersistenceService(store), new SessionGate());
        thread = (await service.GetThreadAsync(thread.Id))!;
        using var cts = new CancellationTokenSource();
        var run = Task.Run(async () =>
        {
            await foreach (var evt in service.SubmitInputAsync(thread.Id, [new TextContent("work")], ct: cts.Token))
            {
                if (disposeOnCancellation && evt.DeltaPayload?.TextDelta.Contains("partial-text") == true)
                {
                    cts.Cancel();
                    break;
                }
            }
        });
        await transport.PartialDelivered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (!disposeOnCancellation)
            await service.CancelTurnAsync(thread.Id, thread.Turns[^1].Id);
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        service = Service(factory, retry, new ThreadStore(root));
        await service.GetThreadAsync(thread.Id);
        await Drain(service.SubmitInputAsync(thread.Id, [new TextContent("continue")]));
        var input = transport.Inputs[^1];
        Assert.Equal(1, input.Split("completed-text").Length - 1);
        Assert.Equal(1, input.Split("partial-text").Length - 1);
        using var request = JsonDocument.Parse(input);
        var items = request.RootElement.GetProperty("input").EnumerateArray().ToArray();
        Assert.Equal("opaque-compaction", items[0].GetProperty("encrypted_content").GetString());
        Assert.Single(items, item => item.GetProperty("type").GetString() == "function_call");
        Assert.Single(items, item => item.GetProperty("type").GetString() == "function_call_output"
            && item.GetProperty("output").GetString()!.Contains("tool-result"));
        Assert.True(input.IndexOf("completed-text", StringComparison.Ordinal) < input.IndexOf("partial-text", StringComparison.Ordinal));
        Assert.Equal(disposeOnCancellation ? 0 : 1, items.Count(item => item.GetRawText().Contains("turn_aborted")));
        if (!disposeOnCancellation)
        {
            Assert.True(input.IndexOf("partial-text", StringComparison.Ordinal) < input.IndexOf("turn_aborted", StringComparison.Ordinal));
        }
    }

    private sealed class InterruptedResponsesTransport : IResponsesToolSearchTransport
    {
        public TaskCompletionSource PartialDelivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Inputs { get; } = [];

        public async IAsyncEnumerable<StreamingResponseUpdate> CreateResponseStreamingAsync(
            CreateResponseOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Inputs.Add(ModelReaderWriter.Write(options).ToString());
            if (Inputs.Count == 1)
            {
                yield return ModelReaderWriter.Read<StreamingResponseUpdate>(BinaryData.FromString("""
                    {"type":"response.output_item.done","sequence_number":1,"output_index":0,
                     "item":{"type":"function_call","id":"fc_test","call_id":"call_test","name":"GetStatus",
                             "arguments":"{}","status":"completed"}}
                    """))!;
                yield break;
            }
            if (Inputs.Count > 2)
            {
                yield return new StreamingResponseOutputTextDeltaUpdate
                {
                    SequenceNumber = 1, ItemId = "msg_next", OutputIndex = 0, ContentIndex = 0, Delta = "ok"
                };
                yield break;
            }
            yield return new StreamingResponseOutputTextDeltaUpdate
            {
                SequenceNumber = 1, ItemId = "msg_done", OutputIndex = 0, ContentIndex = 0, Delta = "completed-text"
            };
            yield return ModelReaderWriter.Read<StreamingResponseUpdate>(BinaryData.FromString("""
                {"type":"response.output_item.done","sequence_number":2,"output_index":0,
                 "item":{"type":"message","id":"msg_done","role":"assistant","status":"completed",
                         "content":[{"type":"output_text","text":"completed-text","annotations":[]}]}}
                """))!;
            yield return new StreamingResponseOutputTextDeltaUpdate
            {
                SequenceNumber = 3, ItemId = "msg_partial", OutputIndex = 1, ContentIndex = 0, Delta = "partial-text"
            };
            PartialDelivered.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    public async Task NativeCompactedHistory_PreservesPrefixAndDeliversInterruptionAfterResume(
        bool forkActive, bool cancelBeforeSession, bool ephemeral)
    {
        var client = new InterruptibleClient { Block = false };
        await using var factory = Factory(ModelProviderProtocols.OpenAIResponses);
        var store = new ThreadStore(root);
        var persistence = new SessionPersistenceService(store);
        var service = Service(factory, client, store);
        var source = await service.CreateThreadAsync(Identity());
        await Drain(service.SubmitInputAsync(source.Id, [new TextContent("initial")]));
        Assert.NotNull(client.NativeInput);
        await SeedCompactedHistoryAsync(persistence, source);
        client.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Block = true;
        if (cancelBeforeSession)
        {
            Assert.IsType<ThreadRuntime>(service.DebugGetRuntime(source.Id)).AgentLock = new SemaphoreSlim(0, 1);
            service.ThreadRuntimeSignalForBroadcast = (_, signal, _) =>
            {
                if (signal == SessionThreadRuntimeSignal.TurnStarted) client.Started.TrySetResult();
            };
        }
        var run = Drain(service.SubmitInputAsync(source.Id, [new TextContent("interruptible")]));
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (!cancelBeforeSession) Assert.Contains("opaque-compaction", client.NativeInput!.ToJsonString());
        var target = forkActive ? await service.ForkThreadAsync(source.Id, new ThreadForkOptions { Ephemeral = ephemeral }) : source;
        await service.CancelTurnAsync(source.Id, source.Turns[^1].Id);
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        client.Block = false;
        if (!ephemeral)
        {
            service = Service(factory, client, new ThreadStore(root));
            await service.GetThreadAsync(target.Id);
        }
        await Drain(service.SubmitInputAsync(target.Id, [new TextContent("continue")]));
        var input = client.NativeInput!;
        Assert.Equal("compaction", input[0]!["type"]!.GetValue<string>());
        Assert.Equal("opaque-compaction", input[0]!["encrypted_content"]!.GetValue<string>());
        var marker = Assert.Single(input, item => item!.ToJsonString().Contains("turn_aborted"));
        Assert.Equal("developer", marker!["role"]!.GetValue<string>());
        Assert.Single(client.Messages, IsMarker);
    }

    private static async Task SeedCompactedHistoryAsync(SessionPersistenceService persistence, SessionThread source)
    {
        var window = persistence.GetOrCreateResponsesContextWindow(source.Id);
        await persistence.ReplaceProviderHistoryAsync(new ProviderHistoryReplacedPayload
        {
            SchemaVersion = ProviderHistorySchema.CurrentSchemaVersion,
            ThreadId = source.Id,
            Protocol = ModelProviderProtocols.OpenAIResponses,
            GenerationId = window.CurrentWindowId,
            ContextWindowId = window.CurrentWindowId,
            CoveredThroughTurnId = source.Turns[^1].Id,
            Reason = ProviderHistoryReasons.RemoteCompaction,
            Entries = [new ProviderHistoryEntry
            {
                EntryId = "native-summary",
                Item = JsonSerializer.SerializeToElement(new { type = "compaction", encrypted_content = "opaque-compaction" })
            }]
        });
    }
}
