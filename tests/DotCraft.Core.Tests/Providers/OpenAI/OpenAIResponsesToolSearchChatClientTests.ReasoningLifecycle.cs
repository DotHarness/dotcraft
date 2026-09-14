using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Persistence;
using DotCraft.Sessions;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Xunit;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;

namespace DotCraft.Tests.Agents;

public sealed partial class OpenAIResponsesToolSearchChatClientTests
{
    [Fact]
    public async Task ReasoningReplay_CheckpointColdResumeAndForkKeepMetadataOutOfDisplayAndDiagnostics()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotcraft_reasoning_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var store = new ThreadStore(root);
        try
        {
            var native = ReasoningItem("rs_lifecycle", true, true, true);
            var transport = new FakeToolSearchTransport(ReasoningEvents(native, 0));
            using var client = CreateClient(new FakeChatClient(new ChatResponse([])), transport);
            var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "inspect")]);
            var thread = new SessionThread
            {
                Id = "thread_lifecycle", WorkspacePath = root, OriginChannel = "test",
                Turns = [new SessionTurn
                {
                    Id = "turn_lifecycle", ThreadId = "thread_lifecycle", Status = TurnStatus.Completed,
                    StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow
                }]
            };
            await store.SaveThreadAsync(thread);
            await store.AppendCompactionCheckpointAsync(thread.Id, "turn_lifecycle", response.Messages.ToArray(),
                "auto", "partial", 1000, 100);
            await store.FlushAndCloseAsync();

            var cold = new ThreadStore(root);
            try
            {
                var restored = await cold.LoadModelHistoryAsync(thread.Id);
                using var request = JsonDocument.Parse(CreateRequestJson("test", restored, null));
                AssertReplay(native, Assert.Single(request.RootElement.GetProperty("input").EnumerateArray()));

                var fork = new SessionThread { Id = "thread_fork", Turns = thread.Turns };
                var materialized = await cold.BuildForkModelHistoryMaterializationAsync(thread, fork);
                Assert.True(materialized.HasCompatibleCheckpoint);
                using var forkRequest = JsonDocument.Parse(CreateRequestJson("test", materialized.History, null));
                AssertReplay(native, Assert.Single(forkRequest.RootElement.GetProperty("input").EnumerateArray()));
                Assert.NotSame(Assert.Single(restored).Contents[0], Assert.Single(materialized.History).Contents[0]);

                var page = await cold.ListThreadItemsAsync(thread.Id, null, null, 10, ThreadHistorySortDirection.Ascending);
                Assert.DoesNotContain(ResponsesReasoningMetadata.Key, JsonSerializer.Serialize(page));
                using var connection = new WorkspaceStateDatabase(root).OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT thread_snapshot_json, persisted_runtime_json FROM thread_history_projection_state WHERE thread_id = $id";
                command.Parameters.AddWithValue("$id", thread.Id);
                using var reader = command.ExecuteReader();
                Assert.True(reader.Read());
                for (var i = 0; i < 2; i++)
                {
                    var projected = reader.IsDBNull(i) ? "" : reader.GetString(i);
                    Assert.DoesNotContain(ResponsesReasoningMetadata.Key, projected);
                    Assert.DoesNotContain("opaque", projected);
                }
            }
            finally
            {
                await cold.FlushAndCloseAsync();
            }

            var traces = new TraceStore();
            var collector = new TraceCollector(traces);
            collector.RecordMaintenanceForkResponse("reasoning-diagnostic", MaintenanceForkTaskKind.ContextCompaction,
                response, fallbackReason: null);
            var events = traces.GetEvents("reasoning-diagnostic");
            Assert.NotEmpty(events);
            var diagnostics = JsonSerializer.Serialize(events);
            Assert.DoesNotContain(ResponsesReasoningMetadata.Key, diagnostics);
            Assert.DoesNotContain("opaque", diagnostics);
        }
        finally
        {
            await store.FlushAndCloseAsync();
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }
}
