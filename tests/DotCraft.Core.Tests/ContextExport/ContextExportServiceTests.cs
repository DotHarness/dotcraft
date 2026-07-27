using System.Text.Json.Nodes;
using DotCraft.ContextExport;
using DotCraft.Protocol;
using DotCraft.Persistence;
using DotCraft.Tracing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.ContextExport;

public sealed class ContextExportServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;
    private readonly string _craft;
    private readonly WorkspaceStateDatabase _stateRuntime;
    private readonly ThreadStore _threadStore;

    public ContextExportServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ContextExportTests_" + Guid.NewGuid().ToString("N")[..8]);
        _workspace = Path.Combine(_root, "workspace");
        _craft = Path.Combine(_workspace, ".craft");
        Directory.CreateDirectory(_workspace);
        _stateRuntime = new WorkspaceStateDatabase(_craft);
        _threadStore = new ThreadStore(_craft, _stateRuntime);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort cleanup */ }
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
        var thread = CreateThread();
        AddTurnWithToolResult(thread, "SECRET_TOOL_OUTPUT");
        await _threadStore.SaveThreadAsync(thread);

        var result = await new ContextExportService().ExportAsync(new ContextExportOptions
        {
            ThreadId = thread.Id,
            WorkspacePath = _workspace,
            ToolResults = ContextExportToolResultMode.None
        });

        Assert.DoesNotContain("SECRET_TOOL_OUTPUT", result.Markdown);
        Assert.Contains("omitted by `--tool-results none`", result.Markdown);
    }

    [Fact]
    public async Task ExportAsync_UsesSurvivingCompactionCheckpointAndReadsMemory()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "old seed", "old answer");
        AddTurnWithMessages(thread, "recent request", "recent answer");
        await _threadStore.SaveThreadAsync(thread);
        await _threadStore.AppendCompactionCheckpointAsync(
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

    [Theory]
    [InlineData(ContextExportToolResultMode.Summary)]
    [InlineData(ContextExportToolResultMode.Full)]
    public async Task ExportAsync_RedactsAllToolResultBodiesBeforePresentation(ContextExportToolResultMode mode)
    {
        var thread = CreateThread();
        AddTurnWithSensitiveResultPaths(thread);
        await _threadStore.SaveThreadAsync(thread);
        await _threadStore.AppendModelHistoryAsync(
            thread.Id,
            [new ChatMessage(ChatRole.Tool,
                [new FunctionResultContent("call_exact", "{\"cookie\":\"EXACT_RESULT_SECRET\",\"safe\":\"exact-visible\"}")])],
            thread.Turns[0].Id);

        var result = await new ContextExportService().ExportAsync(new ContextExportOptions
        {
            ThreadId = thread.Id,
            WorkspacePath = _workspace,
            ToolResults = mode,
            ToolResultPreviewChars = 10_000
        });

        foreach (var secret in new[]
                 {
                     "COMMAND_RESULT_SECRET",
                     "EXECUTION_RESULT_SECRET",
                     "TOOL_RESULT_SECRET",
                     "DYNAMIC_RESULT_SECRET",
                     "EXACT_RESULT_SECRET"
                 })
        {
            Assert.DoesNotContain(secret, result.Markdown);
        }

        Assert.Contains("command-visible", result.Markdown);
        Assert.Contains("execution-visible", result.Markdown);
        Assert.Contains("tool-visible", result.Markdown);
        Assert.Contains("dynamic-visible", result.Markdown);
        Assert.Contains("exact-visible", result.Markdown);
        Assert.Contains("[redacted]", result.Markdown);
    }

    [Theory]
    [InlineData(ContextExportToolResultMode.Summary)]
    [InlineData(ContextExportToolResultMode.Full)]
    public async Task ExportAsync_OmitsRequestUserInputAnswersFromEveryProjection(ContextExportToolResultMode mode)
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
                            ["one_time_code"] = new() { Answers = ["SESSION_SECRET_ANSWER"] },
                            ["deployment_region"] = new() { Answers = ["SESSION_NORMAL_ANSWER"] }
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
                    Result = "{\"answers\":{\"one_time_code\":{\"answers\":[\"TOOL_SECRET_ANSWER\"]},\"deployment_region\":{\"answers\":[\"TOOL_NORMAL_ANSWER\"]}}}"
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
                    Result = "conversation-unrelated-visible"
                }
            }
        ]);
        await _threadStore.SaveThreadAsync(thread);
        await _threadStore.AppendModelHistoryAsync(
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
                        "{\"answers\":{\"one_time_code\":{\"answers\":[\"HISTORY_SECRET_ANSWER\"]},\"deployment_region\":{\"answers\":[\"HISTORY_NORMAL_ANSWER\"]}}}"),
                    new FunctionResultContent("call_exact_unrelated", "history-unrelated-visible")
                ])
            ],
            turn.Id);

        var result = await new ContextExportService().ExportAsync(new ContextExportOptions
        {
            ThreadId = thread.Id,
            WorkspacePath = _workspace,
            ToolResults = mode,
            ToolResultPreviewChars = 10_000
        });

        foreach (var answer in new[]
                 {
                     "SESSION_SECRET_ANSWER",
                     "SESSION_NORMAL_ANSWER",
                     "TOOL_SECRET_ANSWER",
                     "TOOL_NORMAL_ANSWER",
                     "HISTORY_SECRET_ANSWER",
                     "HISTORY_NORMAL_ANSWER"
                 })
        {
            Assert.DoesNotContain(answer, result.Markdown);
        }

        Assert.Contains("request_export_input", result.Markdown);
        Assert.Contains("Enter the one-time code", result.Markdown);
        Assert.Contains("Choose the deployment region", result.Markdown);
        Assert.Contains("omitted because user-input answers may contain secrets", result.Markdown);
        Assert.Contains("conversation-unrelated-visible", result.Markdown);
        Assert.Contains("history-unrelated-visible", result.Markdown);
    }

    [Fact]
    public async Task ExportAsync_UsesCanonicalExactHistoryAndRedactsInternalModelMetadata()
    {
        const string exactText = "exact model-visible answer";
        const string reasoningSecret = "internal reasoning secret";
        const string protectedSecret = "protected replay secret";
        const string extensionSecret = "provider extension secret";
        var thread = CreateThread();
        AddTurnWithMessages(thread, "visible request", "projected answer");
        await _threadStore.SaveThreadAsync(thread);

        var exactMessage = new ChatMessage(ChatRole.Assistant,
        [
            new TextReasoningContent(reasoningSecret)
            {
                ProtectedData = protectedSecret,
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["providerSecret"] = extensionSecret
                }
            },
            new TextContent(exactText)
        ])
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["messageSecret"] = extensionSecret
            }
        };
        await _threadStore.AppendModelHistoryAsync(thread.Id, [exactMessage], thread.Turns[0].Id);

        var result = await new ContextExportService().ExportAsync(new ContextExportOptions
        {
            ThreadId = thread.Id,
            WorkspacePath = _workspace
        });

        Assert.Contains(exactText, result.Markdown);
        Assert.DoesNotContain("projected answer", result.Markdown.Split("## Conversation")[0]);
        Assert.DoesNotContain(reasoningSecret, result.Markdown);
        Assert.DoesNotContain(protectedSecret, result.Markdown);
        Assert.DoesNotContain(extensionSecret, result.Markdown);
        Assert.Contains("[reasoning omitted]", result.Markdown);
    }

    [Theory]
    [InlineData(ContextExportProfile.Handoff)]
    [InlineData(ContextExportProfile.Transcript)]
    public async Task ExportAsync_OmitsFreeFormThreadMetadata(ContextExportProfile profile)
    {
        var thread = CreateThread();
        thread.Metadata["api_key"] = "METADATA_API_KEY_SECRET";
        thread.Metadata["authorization"] = "Bearer METADATA_BEARER_SECRET";
        thread.Metadata["nested"] = "{\"token\":\"METADATA_JSON_SECRET\"}";
        thread.Metadata["dotcraft.externalCliSessions"] =
            "[{\"sessionId\":\"METADATA_CLI_SESSION_SECRET\",\"workingDirectory\":\"C:/private\"}]";
        thread.Metadata["ordinary"] = "METADATA_ORDINARY_VALUE";
        AddTurnWithMessages(thread, "visible request", "visible answer");
        await _threadStore.SaveThreadAsync(thread);

        var result = await new ContextExportService().ExportAsync(new ContextExportOptions
        {
            ThreadId = thread.Id,
            WorkspacePath = _workspace,
            Profile = profile
        });

        Assert.Contains("visible request", result.Markdown);
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

        var traceStore = new TraceStore(_stateRuntime, 5000);
        traceStore.Record(new TraceEvent
        {
            SessionKey = thread.Id,
            Type = TraceEventType.Error,
            Content = "provider context explosion",
            ModelId = "gpt-test"
        });
        traceStore.WaitForPendingPersistence();

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
        const string visibleText = "VISIBLE_ROLLOUT_MARKER";
        const string nativePartSecret = "NATIVE_PART_SECRET";
        const string argumentSecret = "RAW_ARGUMENT_SECRET";
        const string modelHistorySecret = "MODEL_HISTORY_ONLY_SECRET";
        const string protectedDataSecret = "PROTECTED_DATA_SECRET";
        const string checkpointSecret = "COMPACTION_ONLY_SECRET";

        var thread = CreateThread();
        AddTurnWithMessages(thread, visibleText, "ordinary answer");
        var turn = thread.Turns[0];
        var userPayload = Assert.IsType<UserMessagePayload>(turn.Input!.Payload);
        turn.Input.Payload = userPayload with
        {
            NativeInputParts =
            [
                new SessionWireInputPart { Type = "text", Text = nativePartSecret }
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
                ToolName = "safe-search-tool",
                ProviderFlatName = "safe-search-tool",
                CallId = "call_safe_search",
                Arguments = new JsonObject { ["password"] = argumentSecret }
            }
        });

        await _threadStore.SaveThreadAsync(thread);
        await _threadStore.SaveTurnAsync(thread, turn);
        await _threadStore.AppendModelHistoryAsync(
            thread.Id,
            [
                new ChatMessage(ChatRole.Assistant,
                [
                    new TextReasoningContent("internal reasoning")
                    {
                        ProtectedData = protectedDataSecret
                    },
                    new TextContent(modelHistorySecret)
                ])
            ],
            turn.Id);
        await _threadStore.AppendCompactionCheckpointAsync(
            thread.Id,
            turn.Id,
            [new ChatMessage(ChatRole.Assistant, checkpointSecret)],
            "manual",
            "partial",
            100,
            50);

        var visibleResult = await SearchAsync(visibleText);
        var visibleHit = Assert.Single(visibleResult.Hits);
        var rolloutEvidence = Assert.Single(visibleHit.Evidence, evidence => evidence.Source == "rollout");
        Assert.Contains(visibleText, rolloutEvidence.Preview, StringComparison.Ordinal);
        Assert.DoesNotContain(nativePartSecret, rolloutEvidence.Preview, StringComparison.Ordinal);
        Assert.DoesNotContain(argumentSecret, rolloutEvidence.Preview, StringComparison.Ordinal);

        var toolResult = await SearchAsync("safe-search-tool");
        Assert.Single(toolResult.Hits);
        Assert.Single(toolResult.Hits[0].Evidence, evidence => evidence.Source == "rollout");

        Assert.Empty((await SearchAsync(nativePartSecret)).Hits);
        Assert.Empty((await SearchAsync(argumentSecret)).Hits);
        Assert.Empty((await SearchAsync(modelHistorySecret)).Hits);
        Assert.Empty((await SearchAsync(protectedDataSecret)).Hits);
        Assert.Empty((await SearchAsync(checkpointSecret)).Hits);

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

    private static void AddTurnWithSensitiveResultPaths(SessionThread thread)
    {
        AddTurnWithMessages(thread, "run sensitive tools", "calling tools");
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
                    AggregatedOutput = "command-visible password=COMMAND_RESULT_SECRET"
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
                    ResultPreview = "execution-visible Authorization: Bearer EXECUTION_RESULT_SECRET"
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
                    Result = "{\"token\":\"TOOL_RESULT_SECRET\",\"safe\":\"tool-visible\"}"
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
                        ["cookie"] = "DYNAMIC_RESULT_SECRET",
                        ["safe"] = "dynamic-visible"
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
