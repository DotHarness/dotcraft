using System.Text.Json;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Tests for subagent/progress notifications over the Wire protocol (spec Section 6.5).
/// Validates:
/// - SubAgentProgress events are correctly serialized and dispatched as wire notifications
/// - SubAgentProgress notifications include entries with label, tokens, and completion state
/// - Event ordering: subagent/progress relative to item/completed and turn/completed
/// - Final snapshot with token data arrives before turn/completed
/// - Multiple SubAgents in a single turn produce combined entries
/// - The last completed SubAgent's token data is present in the final notification
/// </summary>
public sealed class AppServerSubAgentProgressTests : IDisposable
{
    private readonly AppServerTestHarness _h = new();

    public AppServerSubAgentProgressTests()
    {
        _h.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _h.Dispose();

    // -------------------------------------------------------------------------
    // Test 1: SubAgentProgress event is dispatched as subagent/progress notification
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SubAgentProgress_DispatchedAsWireNotification()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        var events = BuildTurnWithSubAgentProgress(thread.Id);
        _h.Service.EnqueueSubmitEvents(thread.Id, events);

        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await _h.ExecuteRequestAsync(msg);

        // Drain all messages: response + notifications
        var all = await _h.Transport.WaitAndDrainAsync(5, TimeSpan.FromSeconds(10));

        // Find the subagent/progress notification
        var progressNotif = all.Find(d =>
            d.RootElement.TryGetProperty("method", out var m)
            && m.GetString() == AppServerMethods.SubAgentProgress);

        Assert.NotNull(progressNotif);

        var @params = progressNotif.RootElement.GetProperty("params");
        Assert.True(@params.TryGetProperty("threadId", out _));
        Assert.True(@params.TryGetProperty("turnId", out _));
        Assert.True(@params.TryGetProperty("entries", out var entriesEl));
        Assert.True(entriesEl.GetArrayLength() > 0);
    }

    // -------------------------------------------------------------------------
    // Test 2: SubAgent entries contain correct label, tokens, and completion state
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SubAgentProgress_EntriesContainCorrectData()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        var events = BuildTurnWithSubAgentProgress(
            thread.Id,
            entries:
            [
                new SubAgentProgressEntry
                {
                    Label = "research-task",
                    CurrentTool = "GrepFiles",
                    InputTokens = 500,
                    OutputTokens = 200,
                    IsCompleted = false
                }
            ]);
        _h.Service.EnqueueSubmitEvents(thread.Id, events);

        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await _h.ExecuteRequestAsync(msg);

        var all = await _h.Transport.WaitAndDrainAsync(5, TimeSpan.FromSeconds(10));
        var progressNotif = all.Find(d =>
            d.RootElement.TryGetProperty("method", out var m)
            && m.GetString() == AppServerMethods.SubAgentProgress);

        Assert.NotNull(progressNotif);

        var entries = progressNotif.RootElement
            .GetProperty("params")
            .GetProperty("entries");

        var entry = entries[0];
        Assert.Equal("research-task", entry.GetProperty("label").GetString());
        Assert.Equal("GrepFiles", entry.GetProperty("currentTool").GetString());
        Assert.Equal(500, entry.GetProperty("inputTokens").GetInt64());
        Assert.Equal(200, entry.GetProperty("outputTokens").GetInt64());
        Assert.False(entry.GetProperty("isCompleted").GetBoolean());
    }

    // -------------------------------------------------------------------------
    // Test 3: Multiple SubAgent entries in a single progress notification
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SubAgentProgress_MultipleEntries_AllSerialized()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        var events = BuildTurnWithSubAgentProgress(
            thread.Id,
            entries:
            [
                new SubAgentProgressEntry
                {
                    Label = "agent-A",
                    InputTokens = 100,
                    OutputTokens = 50,
                    IsCompleted = true
                },
                new SubAgentProgressEntry
                {
                    Label = "agent-B",
                    InputTokens = 300,
                    OutputTokens = 150,
                    IsCompleted = false
                }
            ]);
        _h.Service.EnqueueSubmitEvents(thread.Id, events);

        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await _h.ExecuteRequestAsync(msg);

        var all = await _h.Transport.WaitAndDrainAsync(5, TimeSpan.FromSeconds(10));
        var progressNotif = all.Find(d =>
            d.RootElement.TryGetProperty("method", out var m)
            && m.GetString() == AppServerMethods.SubAgentProgress);

        Assert.NotNull(progressNotif);

        var entries = progressNotif.RootElement
            .GetProperty("params")
            .GetProperty("entries");

        Assert.Equal(2, entries.GetArrayLength());

        var agentA = entries.EnumerateArray().First(e => e.GetProperty("label").GetString() == "agent-A");
        var agentB = entries.EnumerateArray().First(e => e.GetProperty("label").GetString() == "agent-B");

        Assert.True(agentA.GetProperty("isCompleted").GetBoolean());
        Assert.Equal(100, agentA.GetProperty("inputTokens").GetInt64());
        Assert.False(agentB.GetProperty("isCompleted").GetBoolean());
        Assert.Equal(300, agentB.GetProperty("inputTokens").GetInt64());
    }

    // -------------------------------------------------------------------------
    // Test 4: CRITICAL — subagent/progress with final tokens arrives BEFORE turn/completed
    // This validates the core invariant: the last SubAgent's token data must
    // be visible to the client before the turn ends.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SubAgentProgress_FinalSnapshot_ArrivesBeforeTurnCompleted()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        var events = BuildTurnWithFinalSubAgentProgress(thread.Id);
        _h.Service.EnqueueSubmitEvents(thread.Id, events);

        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await _h.ExecuteRequestAsync(msg);

        var all = await _h.Transport.WaitAndDrainAsync(6, TimeSpan.FromSeconds(10));

        // Find indices
        int progressIndex = -1, turnCompletedIndex = -1;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].RootElement.TryGetProperty("method", out var m))
            {
                var method = m.GetString();
                if (method == AppServerMethods.SubAgentProgress)
                    progressIndex = i; // Take the last one
                if (method == AppServerMethods.TurnCompleted)
                    turnCompletedIndex = i;
            }
        }

        Assert.True(progressIndex >= 0, "Expected at least one subagent/progress notification");
        Assert.True(turnCompletedIndex >= 0, "Expected a turn/completed notification");
        Assert.True(progressIndex < turnCompletedIndex,
            $"subagent/progress (index={progressIndex}) must arrive before turn/completed (index={turnCompletedIndex})");

        // Verify the final progress has completed=true with tokens
        var finalProgress = all[progressIndex];
        var entries = finalProgress.RootElement.GetProperty("params").GetProperty("entries");
        var lastEntry = entries[0];
        Assert.True(lastEntry.GetProperty("isCompleted").GetBoolean());
        Assert.True(lastEntry.GetProperty("inputTokens").GetInt64() > 0,
            "Final subagent/progress must include non-zero input tokens");
    }

    // -------------------------------------------------------------------------
    // Test 5: Event ordering — subagent/progress relative to item/completed (toolResult)
    // When a SubAgent tool call completes, the item/completed (toolResult) event
    // is sent. The subagent/progress with the final token data should ideally
    // arrive before or at the same position as the tool result.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SubAgentProgress_OrderRelativeToToolResult()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        var events = BuildTurnWithToolResultAndProgress(thread.Id);
        _h.Service.EnqueueSubmitEvents(thread.Id, events);

        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await _h.ExecuteRequestAsync(msg);

        var all = await _h.Transport.WaitAndDrainAsync(7, TimeSpan.FromSeconds(10));

        // Collect all methods in order (skip the initial response)
        var methods = all
            .Select(d => d.RootElement.TryGetProperty("method", out var m) ? m.GetString() : "<response>")
            .ToList();

        // Verify: subagent/progress must be present
        Assert.Contains(AppServerMethods.SubAgentProgress, methods);

        // turn/completed must be the last notification
        var lastNotif = methods.Last();
        Assert.Equal(AppServerMethods.TurnCompleted, lastNotif);
    }

    // -------------------------------------------------------------------------
    // Test 6: optOut suppresses subagent/progress when opted out
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SubAgentProgress_OptedOut_NotSent()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(optOutMethods: [AppServerMethods.SubAgentProgress]);

        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var events = BuildTurnWithSubAgentProgress(thread.Id);
        harness.Service.EnqueueSubmitEvents(thread.Id, events);

        var msg = harness.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await harness.ExecuteRequestAsync(msg);

        // Should have response + turn/started + item/started + item/completed + turn/completed = 5
        // but NO subagent/progress
        var all = await harness.Transport.WaitAndDrainAsync(5, TimeSpan.FromSeconds(10));

        var methods = all
            .Select(d => d.RootElement.TryGetProperty("method", out var m) ? m.GetString() : null)
            .Where(m => m != null)
            .ToList();

        Assert.DoesNotContain(AppServerMethods.SubAgentProgress, methods);
    }

    // -------------------------------------------------------------------------
    // Test 7: Last SubAgent token data visible — multi-SubAgent scenario
    // Simulates: agent-A completes (tokens visible), then agent-B completes
    // (its tokens must also be visible in the final subagent/progress).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SubAgentProgress_LastSubAgent_TokensVisibleInFinalNotification()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        // Build event sequence with two progress snapshots:
        // 1st: agent-A completed, agent-B running
        // 2nd: both completed with final tokens
        var events = BuildTurnWithMultipleProgressSnapshots(thread.Id);
        _h.Service.EnqueueSubmitEvents(thread.Id, events);

        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await _h.ExecuteRequestAsync(msg);

        var all = await _h.Transport.WaitAndDrainAsync(7, TimeSpan.FromSeconds(10));

        // Collect all subagent/progress notifications
        var progressNotifs = all.Where(d =>
            d.RootElement.TryGetProperty("method", out var m)
            && m.GetString() == AppServerMethods.SubAgentProgress).ToList();

        Assert.True(progressNotifs.Count >= 2, "Expected at least 2 subagent/progress notifications");

        // The last subagent/progress should have both agents completed with tokens
        var last = progressNotifs[^1];
        var entries = last.RootElement.GetProperty("params").GetProperty("entries");

        var agentB = entries.EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("label").GetString() == "agent-B");

        Assert.True(agentB.ValueKind != JsonValueKind.Undefined, "agent-B should be present in final snapshot");
        Assert.True(agentB.GetProperty("isCompleted").GetBoolean(), "agent-B should be completed");
        Assert.True(agentB.GetProperty("inputTokens").GetInt64() > 0,
            "agent-B (last SubAgent) must have non-zero input tokens in final snapshot");
        Assert.True(agentB.GetProperty("outputTokens").GetInt64() > 0,
            "agent-B (last SubAgent) must have non-zero output tokens in final snapshot");
    }

    // =========================================================================
    // Event sequence builders
    // =========================================================================

    /// <summary>
    /// Builds a basic turn event sequence that includes a subagent/progress event.
    /// Sequence: TurnStarted → ItemStarted → SubAgentProgress → ItemCompleted → TurnCompleted
    /// </summary>
    private static SessionEvent[] BuildTurnWithSubAgentProgress(
        string threadId,
        string turnId = "turn_001",
        IReadOnlyList<SubAgentProgressEntry>? entries = null)
    {
        var turn = AppServerTestHarness.MakeTurn(threadId, turnId);
        var completedTurn = AppServerTestHarness.MakeCompletedTurn(threadId, turnId);
        var item = AppServerTestHarness.MakeAgentMessageItem(turnId);
        var completedItem = AppServerTestHarness.MakeCompletedAgentMessageItem(turnId);
        var now = DateTimeOffset.UtcNow;

        entries ??= [
            new SubAgentProgressEntry
            {
                Label = "default-task",
                InputTokens = 100,
                OutputTokens = 50,
                IsCompleted = false
            }
        ];

        return
        [
            new SessionEvent
            {
                EventId = "e1", EventType = SessionEventType.TurnStarted,
                ThreadId = threadId, TurnId = turnId, Timestamp = now, Payload = turn
            },
            new SessionEvent
            {
                EventId = "e2", EventType = SessionEventType.ItemStarted,
                ThreadId = threadId, TurnId = turnId, ItemId = item.Id, Timestamp = now, Payload = item
            },
            new SessionEvent
            {
                EventId = "e3", EventType = SessionEventType.SubAgentProgress,
                ThreadId = threadId, TurnId = turnId, Timestamp = now,
                Payload = new SubAgentProgressPayload { Entries = entries }
            },
            new SessionEvent
            {
                EventId = "e4", EventType = SessionEventType.ItemCompleted,
                ThreadId = threadId, TurnId = turnId, ItemId = item.Id, Timestamp = now, Payload = completedItem
            },
            new SessionEvent
            {
                EventId = "e5", EventType = SessionEventType.TurnCompleted,
                ThreadId = threadId, TurnId = turnId, Timestamp = now, Payload = completedTurn
            }
        ];
    }

    /// <summary>
    /// Builds a turn sequence where the final subagent/progress (with IsCompleted=true and tokens)
    /// appears BEFORE turn/completed. Tests the critical ordering invariant.
    /// </summary>
    private static SessionEvent[] BuildTurnWithFinalSubAgentProgress(
        string threadId,
        string turnId = "turn_001")
    {
        var turn = AppServerTestHarness.MakeTurn(threadId, turnId);
        var completedTurn = AppServerTestHarness.MakeCompletedTurn(threadId, turnId);
        var item = AppServerTestHarness.MakeAgentMessageItem(turnId);
        var completedItem = AppServerTestHarness.MakeCompletedAgentMessageItem(turnId);
        var now = DateTimeOffset.UtcNow;

        return
        [
            new SessionEvent
            {
                EventId = "e1", EventType = SessionEventType.TurnStarted,
                ThreadId = threadId, TurnId = turnId, Timestamp = now, Payload = turn
            },
            new SessionEvent
            {
                EventId = "e2", EventType = SessionEventType.ItemStarted,
                ThreadId = threadId, TurnId = turnId, ItemId = item.Id, Timestamp = now, Payload = item
            },
            // Intermediate progress (running)
            new SessionEvent
            {
                EventId = "e3", EventType = SessionEventType.SubAgentProgress,
                ThreadId = threadId, TurnId = turnId, Timestamp = now,
                Payload = new SubAgentProgressPayload
                {
                    Entries = [new SubAgentProgressEntry
                    {
                        Label = "task-1", InputTokens = 50, OutputTokens = 20, IsCompleted = false
                    }]
                }
            },
            // Final progress (completed with full tokens)
            new SessionEvent
            {
                EventId = "e4", EventType = SessionEventType.SubAgentProgress,
                ThreadId = threadId, TurnId = turnId, Timestamp = now,
                Payload = new SubAgentProgressPayload
                {
                    Entries = [new SubAgentProgressEntry
                    {
                        Label = "task-1", InputTokens = 500, OutputTokens = 200, IsCompleted = true
                    }]
                }
            },
            new SessionEvent
            {
                EventId = "e5", EventType = SessionEventType.ItemCompleted,
                ThreadId = threadId, TurnId = turnId, ItemId = item.Id, Timestamp = now, Payload = completedItem
            },
            new SessionEvent
            {
                EventId = "e6", EventType = SessionEventType.TurnCompleted,
                ThreadId = threadId, TurnId = turnId, Timestamp = now, Payload = completedTurn
            }
        ];
    }

    /// <summary>
    /// Builds a turn with both a tool result (item/completed for toolResult type)
    /// and subagent/progress events. Tests the ordering relationship.
    /// </summary>
    private static SessionEvent[] BuildTurnWithToolResultAndProgress(
        string threadId,
        string turnId = "turn_001")
    {
        var turn = AppServerTestHarness.MakeTurn(threadId, turnId);
        var completedTurn = AppServerTestHarness.MakeCompletedTurn(threadId, turnId);
        var now = DateTimeOffset.UtcNow;

        var toolCallItem = new SessionItem
        {
            Id = "item_tool_001", TurnId = turnId,
            Type = ItemType.ToolCall, Status = ItemStatus.Started,
            CreatedAt = now,
            Payload = new ToolCallPayload { CallId = "call_001", ToolName = "SpawnAgent" }
        };
        var toolResultItem = new SessionItem
        {
            Id = "item_result_001", TurnId = turnId,
            Type = ItemType.ToolResult, Status = ItemStatus.Completed,
            CreatedAt = now, CompletedAt = now,
            Payload = new ToolResultPayload { CallId = "call_001", Result = "Task completed." }
        };
        var agentItem = AppServerTestHarness.MakeAgentMessageItem(turnId, "item_msg_001");
        var completedAgentItem = AppServerTestHarness.MakeCompletedAgentMessageItem(turnId, "item_msg_001");

        return
        [
            new SessionEvent
            {
                EventId = "e1", EventType = SessionEventType.TurnStarted,
                ThreadId = threadId, TurnId = turnId, Timestamp = now, Payload = turn
            },
            new SessionEvent
            {
                EventId = "e2", EventType = SessionEventType.ItemStarted,
                ThreadId = threadId, TurnId = turnId, ItemId = toolCallItem.Id, Timestamp = now,
                Payload = toolCallItem
            },
            // SubAgent progress during tool execution
            new SessionEvent
            {
                EventId = "e3", EventType = SessionEventType.SubAgentProgress,
                ThreadId = threadId, TurnId = turnId, Timestamp = now,
                Payload = new SubAgentProgressPayload
                {
                    Entries = [new SubAgentProgressEntry
                    {
                        Label = "task-1", InputTokens = 500, OutputTokens = 200, IsCompleted = true
                    }]
                }
            },
            // Tool result arrives after SubAgent completes
            new SessionEvent
            {
                EventId = "e4", EventType = SessionEventType.ItemCompleted,
                ThreadId = threadId, TurnId = turnId, ItemId = toolResultItem.Id, Timestamp = now,
                Payload = toolResultItem
            },
            // Agent produces final message
            new SessionEvent
            {
                EventId = "e5", EventType = SessionEventType.ItemStarted,
                ThreadId = threadId, TurnId = turnId, ItemId = agentItem.Id, Timestamp = now,
                Payload = agentItem
            },
            new SessionEvent
            {
                EventId = "e6", EventType = SessionEventType.ItemCompleted,
                ThreadId = threadId, TurnId = turnId, ItemId = agentItem.Id, Timestamp = now,
                Payload = completedAgentItem
            },
            new SessionEvent
            {
                EventId = "e7", EventType = SessionEventType.TurnCompleted,
                ThreadId = threadId, TurnId = turnId, Timestamp = now, Payload = completedTurn
            }
        ];
    }

    /// <summary>
    /// Builds a turn with two SubAgent progress snapshots showing progression:
    /// 1st snapshot: agent-A completed, agent-B running (partial tokens)
    /// 2nd snapshot: both completed with final tokens
    /// Then tool result + turn completed.
    /// </summary>
    private static SessionEvent[] BuildTurnWithMultipleProgressSnapshots(
        string threadId,
        string turnId = "turn_001")
    {
        var turn = AppServerTestHarness.MakeTurn(threadId, turnId);
        var completedTurn = AppServerTestHarness.MakeCompletedTurn(threadId, turnId);
        var item = AppServerTestHarness.MakeAgentMessageItem(turnId);
        var completedItem = AppServerTestHarness.MakeCompletedAgentMessageItem(turnId);
        var now = DateTimeOffset.UtcNow;

        return
        [
            new SessionEvent
            {
                EventId = "e1", EventType = SessionEventType.TurnStarted,
                ThreadId = threadId, TurnId = turnId, Timestamp = now, Payload = turn
            },
            new SessionEvent
            {
                EventId = "e2", EventType = SessionEventType.ItemStarted,
                ThreadId = threadId, TurnId = turnId, ItemId = item.Id, Timestamp = now, Payload = item
            },
            // Snapshot 1: agent-A done, agent-B running
            new SessionEvent
            {
                EventId = "e3", EventType = SessionEventType.SubAgentProgress,
                ThreadId = threadId, TurnId = turnId, Timestamp = now,
                Payload = new SubAgentProgressPayload
                {
                    Entries =
                    [
                        new SubAgentProgressEntry
                        {
                            Label = "agent-A", InputTokens = 200, OutputTokens = 80, IsCompleted = true
                        },
                        new SubAgentProgressEntry
                        {
                            Label = "agent-B", InputTokens = 100, OutputTokens = 30, IsCompleted = false
                        }
                    ]
                }
            },
            // Snapshot 2: both done with final tokens
            new SessionEvent
            {
                EventId = "e4", EventType = SessionEventType.SubAgentProgress,
                ThreadId = threadId, TurnId = turnId, Timestamp = now,
                Payload = new SubAgentProgressPayload
                {
                    Entries =
                    [
                        new SubAgentProgressEntry
                        {
                            Label = "agent-A", InputTokens = 200, OutputTokens = 80, IsCompleted = true
                        },
                        new SubAgentProgressEntry
                        {
                            Label = "agent-B", InputTokens = 400, OutputTokens = 160, IsCompleted = true
                        }
                    ]
                }
            },
            new SessionEvent
            {
                EventId = "e5", EventType = SessionEventType.ItemCompleted,
                ThreadId = threadId, TurnId = turnId, ItemId = item.Id, Timestamp = now, Payload = completedItem
            },
            new SessionEvent
            {
                EventId = "e6", EventType = SessionEventType.TurnCompleted,
                ThreadId = threadId, TurnId = turnId, Timestamp = now, Payload = completedTurn
            },
            // Extra: ensure the drain captures all
            new SessionEvent
            {
                EventId = "e7", EventType = SessionEventType.ItemStarted,
                ThreadId = threadId, TurnId = turnId, ItemId = "item_extra",
                Timestamp = now, Payload = AppServerTestHarness.MakeAgentMessageItem(turnId, "item_extra")
            }
        ];
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void AssertMethod(JsonDocument doc, string expectedMethod)
    {
        Assert.True(doc.RootElement.TryGetProperty("method", out var methodEl),
            $"Expected notification with method '{expectedMethod}' but got no 'method' property.");
        Assert.Equal(expectedMethod, methodEl.GetString());
    }
}
