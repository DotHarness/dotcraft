using DotCraft.Agents;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceStreamingToolProjectionTests : SessionServiceRuntimeSignalTestBase
{
    [Fact]
    public async Task ReusedArgumentIndexProjectsDistinctToolItemsWithinTheSameTurn()
    {
        const string toolName = "ExampleTool";
        (string CallId, string Path)[] calls = [("call-first", "first.txt"), ("call-second", "second.txt")];
        var updates = new List<ChatResponseUpdate>();
        foreach (var (callId, path) in calls)
        {
            updates.Add(new(ChatRole.Assistant, [new ToolCallArgumentsDeltaContent
            {
                ToolCallIndex = 0,
                ToolName = toolName,
                CallId = callId,
                ArgumentsDelta = "{\"path\":\"" + path + "\"}"
            }]));
            updates.Add(new(ChatRole.Assistant, [new FunctionCallContent(
                callId, toolName, new Dictionary<string, object?> { ["path"] = path })]));
            updates.Add(new(ChatRole.Assistant, [new FunctionResultContent(callId, "Read complete.")]));
        }

        updates.Add(new(ChatRole.Assistant, [new TextContent("Done.")]));

        using var client = new FakeChatClient(updates.ToArray());
        await using var factory = CreateAgentFactory(client);
        var service = CreateService(factory, client);
        var thread = await service.CreateThreadAsync(MakeIdentity());

        var events = await CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("Read both files.")]));

        var previews = events.Where(evt => evt.ToolCallArgumentsDeltaPayload is not null).ToArray();
        Assert.Equal(2, previews.Length);
        Assert.NotEqual(previews[0].ItemId, previews[1].ItemId);
        foreach (var (callId, path) in calls)
        {
            var preview = Assert.Single(previews, evt => evt.ToolCallArgumentsDeltaPayload!.CallId == callId);
            var started = Assert.Single(events, evt => evt.EventType == SessionEventType.ItemStarted
                && evt.ItemPayload?.Payload is ToolCallPayload payload && payload.CallId == callId);
            var completed = Assert.Single(events, evt => evt.EventType == SessionEventType.ItemCompleted
                && evt.ItemPayload?.Payload is ToolCallPayload payload && payload.CallId == callId);

            Assert.Equal(started.ItemId, preview.ItemId);
            Assert.Equal(preview.ItemId, completed.ItemId);
            Assert.Equal(path, Assert.IsType<ToolCallPayload>(completed.ItemPayload!.Payload).Arguments?["path"]?.ToString());
        }
    }
}
