using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceInterruptionTests
{
    [Theory]
    [InlineData(false, false, false)]
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
}
