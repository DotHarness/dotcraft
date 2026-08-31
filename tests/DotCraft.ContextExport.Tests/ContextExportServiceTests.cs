using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;
using DynamicToolCallPayload = DotCraft.Sessions.DynamicToolCallPayload;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using ToolCallPayload = DotCraft.Sessions.ToolCallPayload;
using ToolExecutionPayload = DotCraft.Sessions.ToolExecutionPayload;
using ToolResultPayload = DotCraft.Sessions.ToolResultPayload;
using UserInputRequestPayload = DotCraft.Sessions.UserInputRequestPayload;
using UserMessagePayload = DotCraft.Sessions.UserMessagePayload;
using Xunit;

namespace DotCraft.ContextExport.Tests;

public sealed class ContextExportServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;
    private readonly string _craft;
    private readonly ContextExportTestWorkspace _fixture;
    private readonly ThreadStore _threadStore;

    public ContextExportServiceTests()
    {
        _fixture = new ContextExportTestWorkspace();
        _root = _fixture.Root;
        _workspace = _fixture.Workspace;
        _craft = _fixture.Craft;
        _threadStore = _fixture.ThreadStore;
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }

    [Fact]
    public async Task ExportAsync_RejectsRolloutPathOutsideCraftDirectory()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "request", "answer");
        await _threadStore.SaveThreadAsync(thread);

        var statePath = Path.Combine(_craft, "state.db");
        await using (var connection = new SqliteConnection($"Data Source={statePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE threads SET rollout_path = $path WHERE thread_id = $threadId";
            command.Parameters.AddWithValue("$path", Path.Combine(_root, "outside.jsonl"));
            command.Parameters.AddWithValue("$threadId", thread.Id);
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<KeyNotFoundException>(() => new ContextExportService().ExportAsync(new ContextExportOptions
        {
            ThreadId = thread.Id,
            WorkspacePath = _workspace
        }));
    }

    [Fact]
    public async Task ExportAsync_AfterRollback_OmitsRolledBackTurnAndListsContinuityEvent()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "keep this request", "keep this answer");
        AddTurnWithMessages(thread, "remove this request", "remove this answer");
        await _threadStore.SaveThreadAsync(thread);

        thread.Turns.RemoveAt(1);
        thread.LastActiveAt = DateTimeOffset.UtcNow.AddMinutes(1);
        await _threadStore.RollbackThreadAsync(thread, 1);

        var result = await new ContextExportService().ExportAsync(new ContextExportOptions
        {
            ThreadId = thread.Id,
            WorkspacePath = _workspace
        });

        Assert.Contains("keep this request", result.Markdown);
        Assert.Contains("keep this answer", result.Markdown);
        Assert.DoesNotContain("remove this request", result.Markdown);
        Assert.DoesNotContain("remove this answer", result.Markdown);
        Assert.Contains("Rollback", result.Markdown);
    }

    [Fact]
    public async Task ExportAsync_WithToolResultsNone_OmitsPersistedResultBody()
    {
        const string resultBody = "tool-output-body-marker";
        var thread = CreateThread();
        AddTurnWithToolResult(thread, resultBody);
        await _threadStore.SaveThreadAsync(thread);

        var result = await new ContextExportService().ExportAsync(new ContextExportOptions
        {
            ThreadId = thread.Id,
            WorkspacePath = _workspace,
            ToolResults = ContextExportToolResultMode.None
        });

        Assert.DoesNotContain(resultBody, result.Markdown);
        Assert.Contains("omitted by `--tool-results none`", result.Markdown);
    }

    [Fact]
    public async Task ExportAsync_UsesSurvivingCompactionCheckpointAndReadsMemory()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "old seed", "old answer");
        AddTurnWithMessages(thread, "recent request", "recent answer");
        await _threadStore.SaveThreadAsync(thread);
        await _fixture.Persistence.AppendCompactionCheckpointAsync(
            thread.Id,
            thread.Turns[0].Id,
            [new ChatMessage(ChatRole.Assistant, "compacted summary")],
            "manual",
            "partial",
            1000,
            100);

        var memoryDir = Path.Combine(_craft, "memory");
        Directory.CreateDirectory(memoryDir);
        await File.WriteAllTextAsync(Path.Combine(memoryDir, "MEMORY.md"), "Remember: use readonly export.");
        await File.WriteAllTextAsync(Path.Combine(memoryDir, "HISTORY.md"), "old event\n\nrecent memory event");

        var result = await new ContextExportService().ExportAsync(new ContextExportOptions
        {
            ThreadId = thread.Id,
            WorkspacePath = _workspace
        });

        Assert.Contains("compacted summary", result.Markdown);
        Assert.Contains("recent request", result.Markdown);
        Assert.Contains("Remember: use readonly export.", result.Markdown);
        Assert.Contains("recent memory event", result.Markdown);
        Assert.Contains("Compaction", result.Markdown);
    }

    [Fact]
    public async Task ExportAsync_WithToolResultsFull_PreservesPersistedResultBodiesVerbatim()
    {
        const string exactResultBody = "{\"detail\":\"exact-body-marker\",\"status\":\"ok\"}";
        var thread = CreateThread();
        AddTurnWithResultBodies(thread);
        await _threadStore.SaveThreadAsync(thread);
        await _fixture.AppendModelHistoryAsync(
            thread.Id,
            [new ChatMessage(ChatRole.Tool,
                [new FunctionResultContent("call_exact", exactResultBody)])],
            thread.Turns[0].Id);

        var result = await new ContextExportService().ExportAsync(new ContextExportOptions
        {
            ThreadId = thread.Id,
            WorkspacePath = _workspace,
            ToolResults = ContextExportToolResultMode.Full,
            ToolResultPreviewChars = 10_000
        });

        // Every persisted body shape reaches the document with its content unchanged.
        foreach (var body in new[]
                 {
                     "ordinary command output command-body-marker",
                     "ordinary execution preview execution-body-marker",
                     "{\"detail\":\"tool-body-marker\",\"status\":\"ok\"}",
                     "dynamic-body-marker",
                     exactResultBody
                 })
        {
            Assert.Contains(body, result.Markdown);
        }
    }

    [Fact]
    public async Task ExportAsync_WithToolResultsSummary_TruncatesBodyWithoutRewritingThePrefix()
    {
        const string resultBody = "ordinary result prefix tail-not-included";
        var thread = CreateThread();
        AddTurnWithToolResult(thread, resultBody);
        await _threadStore.SaveThreadAsync(thread);

        var result = await new ContextExportService().ExportAsync(new ContextExportOptions
        {
            ThreadId = thread.Id,
            WorkspacePath = _workspace,
            ToolResults = ContextExportToolResultMode.Summary,
            ToolResultPreviewChars = 22
        });

        Assert.Contains("ordinary result prefix ...", result.Markdown);
        Assert.DoesNotContain("tail-not-included", result.Markdown);
    }

    [Fact]
    public async Task ExportAsync_RequestUserInputAnswersFollowFullOutputScope()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "collect deployment details", "asking for input");
        var turn = thread.Turns[0];
        var now = turn.StartedAt;
        turn.Items.AddRange(
        [
            new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(10),
                TurnId = turn.Id,
                Type = ItemType.ToolCall,
                Status = ItemStatus.Completed,
                CreatedAt = now.AddMilliseconds(200),
                CompletedAt = now.AddMilliseconds(200),
                Payload = new ToolCallPayload
                {
                    ToolName = "RequestUserInput",
                    ProviderFlatName = "RequestUserInput",
                    CallId = "call_user_input",
                    Arguments = new JsonObject { ["questionCount"] = 2 }
                }
            },
            new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(11),
                TurnId = turn.Id,
                Type = ItemType.UserInputRequest,
                Status = ItemStatus.Completed,
                CreatedAt = now.AddMilliseconds(300),
                CompletedAt = now.AddMilliseconds(300),
                Payload = new UserInputRequestPayload
                {
                    RequestId = "request_export_input",
                    IsBlocking = false,
                    Questions =
                    [
                        new RequestUserInputQuestion
                        {
                            Id = "one_time_code",
                            Header = "Code",
                            Question = "Enter the one-time code",
                            IsSecret = true
                        },
                        new RequestUserInputQuestion
                        {
                            Id = "deployment_region",
                            Header = "Region",
                            Question = "Choose the deployment region"
                        }
                    ]
                }
            },
            new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(12),
                TurnId = turn.Id,
                Type = ItemType.UserInputResponse,
                Status = ItemStatus.Completed,
                CreatedAt = now.AddMilliseconds(400),
                CompletedAt = now.AddMilliseconds(400),
                Payload = new UserInputResponsePayload
                {
                    RequestId = "request_export_input",
                    Response = new RequestUserInputResponse
                    {
                        Answers = new Dictionary<string, RequestUserInputAnswer>(StringComparer.Ordinal)
                        {
                            ["one_time_code"] = new() { Answers = ["session-masked-answer"] },
                            ["deployment_region"] = new() { Answers = ["session-plain-answer"] }
                        }
                    }
                }
            },
            new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(13),
                TurnId = turn.Id,
                Type = ItemType.ToolResult,
                Status = ItemStatus.Completed,
                CreatedAt = now.AddMilliseconds(500),
                CompletedAt = now.AddMilliseconds(500),
                Payload = new ToolResultPayload
                {
                    CallId = "call_user_input",
                    ProviderFlatName = "RequestUserInput",
                    Success = true,
                    Result = "{\"answers\":{\"one_time_code\":{\"answers\":[\"tool-masked-answer\"]},\"deployment_region\":{\"answers\":[\"tool-plain-answer\"]}}}"
                }
            },
            new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(14),
                TurnId = turn.Id,
                Type = ItemType.ToolResult,
                Status = ItemStatus.Completed,
                CreatedAt = now.AddMilliseconds(600),
                CompletedAt = now.AddMilliseconds(600),
                Payload = new ToolResultPayload
                {
                    CallId = "call_unrelated",
                    ProviderFlatName = "ReadFile",
                    ToolName = "ReadFile",
                    Success = true,
                    Result = "conversation-unrelated-body"
                }
            }
        ]);
        await _threadStore.SaveThreadAsync(thread);
        await _fixture.AppendModelHistoryAsync(
            thread.Id,
            [
                new ChatMessage(ChatRole.Assistant,
                [
                    new FunctionCallContent("call_exact_input", "RequestUserInput", new Dictionary<string, object?>()),
                    new FunctionCallContent("call_exact_unrelated", "ReadFile", new Dictionary<string, object?>())
                ]),
                new ChatMessage(ChatRole.Tool,
                [
                    new FunctionResultContent(
                        "call_exact_input",
                        "{\"answers\":{\"one_time_code\":{\"answers\":[\"history-masked-answer\"]},\"deployment_region\":{\"answers\":[\"history-plain-answer\"]}}}"),
                    new FunctionResultContent("call_exact_unrelated", "history-unrelated-body")
                ])
            ],
            turn.Id);

        var result = await new ContextExportService().ExportAsync(new ContextExportOptions
        {
            ThreadId = thread.Id,
            WorkspacePath = _workspace,
            ToolResults = ContextExportToolResultMode.Full,
            ToolResultPreviewChars = 10_000
        });

        // Answers to an `IsSecret` question follow the selected scope like any other body.
        foreach (var answer in new[]
                 {
                     "session-masked-answer",
                     "session-plain-answer",
                     "tool-masked-answer",
                     "tool-plain-answer",
                     "history-masked-answer",
                     "history-plain-answer"
                 })
        {
            Assert.Contains(answer, result.Markdown);
        }

        Assert.Contains("request_export_input", result.Markdown);
        Assert.Contains("Enter the one-time code", result.Markdown);
        Assert.Contains("Choose the deployment region", result.Markdown);
        Assert.Contains("conversation-unrelated-body", result.Markdown);
        Assert.Contains("history-unrelated-body", result.Markdown);
    }

    [Fact]
    public async Task ExportAsync_UsesCanonicalExactHistoryAndOmitsInternalModelMetadata()
    {
        const string exactText = "exact model-visible answer";
        const string reasoningText = "internal reasoning text";
        const string protectedReplayData = "protected replay payload";
        const string providerExtensionValue = "provider extension value";
        var thread = CreateThread();
        AddTurnWithMessages(thread, "visible request", "projected answer");
        await _threadStore.SaveThreadAsync(thread);

        var exactMessage = new ChatMessage(ChatRole.Assistant,
        [
            new TextReasoningContent(reasoningText)
            {
                ProtectedData = protectedReplayData,
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["providerDetail"] = providerExtensionValue
                }
            },
            new TextContent(exactText)
        ])
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["messageDetail"] = providerExtensionValue
            }
        };
        await _fixture.AppendModelHistoryAsync(thread.Id, [exactMessage], thread.Turns[0].Id);

        var result = await new ContextExportService().ExportAsync(new ContextExportOptions
        {
            ThreadId = thread.Id,
            WorkspacePath = _workspace
        });

        // Reasoning, protected replay data, and provider extensions sit outside the
        // displayable projection, so the document carries only the model-visible text.
        Assert.Contains(exactText, result.Markdown);
        Assert.DoesNotContain("projected answer", result.Markdown.Split("## Conversation")[0]);
        Assert.DoesNotContain(reasoningText, result.Markdown);
        Assert.DoesNotContain(protectedReplayData, result.Markdown);
        Assert.DoesNotContain(providerExtensionValue, result.Markdown);
        Assert.Contains("[reasoning omitted]", result.Markdown);
    }

    [Theory]
    [InlineData(ContextExportProfile.Handoff)]
    [InlineData(ContextExportProfile.Transcript)]
    public async Task ExportAsync_RendersOnlyTheKnownThreadMetadataFields(ContextExportProfile profile)
    {
        var thread = CreateThread();
        thread.Metadata["ordinary"] = "METADATA_ORDINARY_VALUE";
        thread.Metadata["nested"] = "{\"detail\":\"METADATA_NESTED_VALUE\"}";
        thread.Metadata["dotcraft.externalCliSessions"] =
            "[{\"sessionId\":\"METADATA_CLI_SESSION_VALUE\",\"workingDirectory\":\"C:/workspaces/demo\"}]";
        AddTurnWithMessages(thread, "visible request", "visible answer");
        await _threadStore.SaveThreadAsync(thread);

        var result = await new ContextExportService().ExportAsync(new ContextExportOptions
        {
            ThreadId = thread.Id,
            WorkspacePath = _workspace,
            Profile = profile
        });

        // The document has a fixed metadata field set; the free-form bag is not one of them.
        Assert.Contains("visible request", result.Markdown);
        Assert.Contains("- Status:", result.Markdown);
        Assert.Contains("- Origin Channel:", result.Markdown);
        Assert.DoesNotContain("METADATA_", result.Markdown);
        Assert.DoesNotContain("dotcraft.externalCliSessions", result.Markdown);
        Assert.DoesNotContain("- Metadata:", result.Markdown);
    }

    [Fact]
    public async Task SearchAsync_TraceEventMatch_ReturnsBoundThreadEvidence()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "provider issue", "failed");
        await _threadStore.SaveThreadAsync(thread);

        await _fixture.AppendTraceEventAsync(thread.Id, "provider context explosion", "gpt-test");

        var result = await new ContextSearchService().SearchAsync(new ContextSearchOptions
        {
            WorkspacePath = _workspace,
            Query = "context explosion",
            Limit = 5
        });

        var hit = Assert.Single(result.Hits);
        Assert.Equal(thread.Id, hit.ThreadId);
        Assert.Contains(hit.Evidence, evidence => evidence.Source == "trace_events");
    }

    [Fact]
    public async Task SearchAsync_RolloutSearch_UsesDeduplicatedDisplayableItemsAndSkipsInternalHistory()
    {
        const string visibleText = "rollout-visible-marker";
        const string nativePartMarker = "NATIVE_PART_MARKER";
        const string argumentMarker = "RAW_ARGUMENT_MARKER";
        const string modelHistoryMarker = "MODEL_HISTORY_ONLY_MARKER";
        const string protectedDataMarker = "PROTECTED_DATA_MARKER";
        const string checkpointMarker = "COMPACTION_ONLY_MARKER";

        var thread = CreateThread();
        AddTurnWithMessages(thread, visibleText, "ordinary answer");
        var turn = thread.Turns[0];
        var userPayload = Assert.IsType<UserMessagePayload>(turn.Input!.Payload);
        turn.Input.Payload = userPayload with
        {
            NativeInputParts =
            [
                new SessionInputPart { Type = "text", Text = nativePartMarker }
            ]
        };
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(3),
            TurnId = turn.Id,
            Type = ItemType.ToolCall,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolCallPayload
            {
                ToolName = "demo-search-tool",
                ProviderFlatName = "demo-search-tool",
                CallId = "call_demo_search",
                Arguments = new JsonObject { ["query"] = argumentMarker }
            }
        });

        await _threadStore.SaveThreadAsync(thread);
        await _fixture.AppendTurnStateAsync(thread, turn);
        await _fixture.AppendModelHistoryAsync(
            thread.Id,
            [
                new ChatMessage(ChatRole.Assistant,
                [
                    new TextReasoningContent("internal reasoning")
                    {
                        ProtectedData = protectedDataMarker
                    },
                    new TextContent(modelHistoryMarker)
                ])
            ],
            turn.Id);
        await _fixture.Persistence.AppendCompactionCheckpointAsync(
            thread.Id,
            turn.Id,
            [new ChatMessage(ChatRole.Assistant, checkpointMarker)],
            "manual",
            "partial",
            100,
            50);

        // Evidence keeps the displayable item text intact.
        var visibleResult = await SearchAsync(visibleText);
        var visibleHit = Assert.Single(visibleResult.Hits);
        var rolloutEvidence = Assert.Single(visibleHit.Evidence, evidence => evidence.Source == "rollout");
        Assert.Contains(visibleText, rolloutEvidence.Preview, StringComparison.Ordinal);
        Assert.DoesNotContain(nativePartMarker, rolloutEvidence.Preview, StringComparison.Ordinal);
        Assert.DoesNotContain(argumentMarker, rolloutEvidence.Preview, StringComparison.Ordinal);

        // One rollout line per displayable item, even after the turn is replayed.
        var toolResult = await SearchAsync("demo-search-tool");
        Assert.Single(toolResult.Hits);
        Assert.Single(toolResult.Hits[0].Evidence, evidence => evidence.Source == "rollout");

        // Native input parts, raw call arguments, model history, and checkpoints are
        // outside the displayable projection the index is built from.
        Assert.Empty((await SearchAsync(nativePartMarker)).Hits);
        Assert.Empty((await SearchAsync(argumentMarker)).Hits);
        Assert.Empty((await SearchAsync(modelHistoryMarker)).Hits);
        Assert.Empty((await SearchAsync(protectedDataMarker)).Hits);
        Assert.Empty((await SearchAsync(checkpointMarker)).Hits);

        Task<ContextSearchResult> SearchAsync(string query) =>
            new ContextSearchService().SearchAsync(new ContextSearchOptions
            {
                WorkspacePath = _workspace,
                Query = query,
                Limit = 5
            });
    }

    [Fact]
    public async Task SearchAsync_WhenCraftDirectoryMissing_DoesNotCreateWorkspaceState()
    {
        var workspace = Path.Combine(_root, "empty-workspace");
        Directory.CreateDirectory(workspace);

        var result = await new ContextSearchService().SearchAsync(new ContextSearchOptions
        {
            WorkspacePath = workspace,
            Query = "anything"
        });

        Assert.Empty(result.Hits);
        Assert.NotEmpty(result.Warnings);
        Assert.False(Directory.Exists(Path.Combine(workspace, ".craft")));
    }

    private SessionThread CreateThread() => new()
    {
        Id = SessionIdGenerator.NewThreadId(),
        WorkspacePath = _workspace,
        UserId = "user1",
        OriginChannel = "cli",
        Status = ThreadStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        LastActiveAt = DateTimeOffset.UtcNow,
        HistoryMode = HistoryMode.Server
    };

    private static void AddTurnWithMessages(
        SessionThread thread,
        string userText,
        string agentText)
    {
        var now = DateTimeOffset.UtcNow.AddSeconds(thread.Turns.Count);
        var turn = new SessionTurn
        {
            Id = SessionIdGenerator.NewTurnId(thread.Turns.Count + 1),
            ThreadId = thread.Id,
            Status = TurnStatus.Completed,
            StartedAt = now,
            CompletedAt = now.AddSeconds(1)
        };
        var userItem = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(1),
            TurnId = turn.Id,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new UserMessagePayload { Text = userText }
        };
        turn.Input = userItem;
        turn.Items.Add(userItem);
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(2),
            TurnId = turn.Id,
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now.AddMilliseconds(100),
            CompletedAt = now.AddMilliseconds(100),
            Payload = new AgentMessagePayload { Text = agentText }
        });
        thread.Turns.Add(turn);
        thread.LastActiveAt = turn.CompletedAt.Value;
    }

    private static void AddTurnWithResultBodies(SessionThread thread)
    {
        AddTurnWithMessages(thread, "run tools", "calling tools");
        var turn = thread.Turns[0];
        var now = turn.StartedAt;
        turn.Items.AddRange(
        [
            new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(10),
                TurnId = turn.Id,
                Type = ItemType.CommandExecution,
                Status = ItemStatus.Completed,
                CreatedAt = now,
                CompletedAt = now,
                Payload = new CommandExecutionPayload
                {
                    Command = "demo",
                    WorkingDirectory = ".",
                    Status = "completed",
                    AggregatedOutput = "ordinary command output command-body-marker"
                }
            },
            new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(11),
                TurnId = turn.Id,
                Type = ItemType.ToolExecution,
                Status = ItemStatus.Completed,
                CreatedAt = now,
                CompletedAt = now,
                Payload = new ToolExecutionPayload
                {
                    ToolName = "demo",
                    CallId = "call_execution",
                    Status = "completed",
                    Success = true,
                    ResultPreview = "ordinary execution preview execution-body-marker"
                }
            },
            new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(12),
                TurnId = turn.Id,
                Type = ItemType.ToolResult,
                Status = ItemStatus.Completed,
                CreatedAt = now,
                CompletedAt = now,
                Payload = new ToolResultPayload
                {
                    CallId = "call_result",
                    Success = true,
                    Result = "{\"detail\":\"tool-body-marker\",\"status\":\"ok\"}"
                }
            },
            new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(14),
                TurnId = turn.Id,
                Type = ItemType.DynamicToolCall,
                Status = ItemStatus.Completed,
                CreatedAt = now,
                CompletedAt = now,
                Payload = new DynamicToolCallPayload
                {
                    ToolName = "dynamic",
                    ProviderFlatName = "dynamic",
                    CallId = "call_dynamic",
                    Status = "completed",
                    Success = true,
                    StructuredContent = new JsonObject
                    {
                        ["detail"] = "dynamic-body-marker",
                        ["status"] = "ok"
                    }
                }
            }
        ]);
    }

    private static void AddTurnWithToolResult(SessionThread thread, string resultText)
    {
        AddTurnWithMessages(thread, "run tool", "calling tool");
        var turn = thread.Turns[0];
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(3),
            TurnId = turn.Id,
            Type = ItemType.ToolCall,
            Status = ItemStatus.Completed,
            CreatedAt = turn.StartedAt.AddMilliseconds(200),
            CompletedAt = turn.StartedAt.AddMilliseconds(200),
            Payload = new ToolCallPayload
            {
                ToolName = "ReadFile",
                CallId = "call_1",
                Arguments = new JsonObject { ["path"] = "demo.txt" }
            }
        });
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(4),
            TurnId = turn.Id,
            Type = ItemType.ToolResult,
            Status = ItemStatus.Completed,
            CreatedAt = turn.StartedAt.AddMilliseconds(300),
            CompletedAt = turn.StartedAt.AddMilliseconds(300),
            Payload = new ToolResultPayload
            {
                CallId = "call_1",
                Result = resultText,
                Success = true
            }
        });
    }
}
