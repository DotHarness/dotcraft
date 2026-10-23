using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Commands.Core;
using DotCraft.Plugins;
using DotCraft.Skills;
using Microsoft.Extensions.AI;
using DotCraft.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using ApprovalRequestPayload = DotCraft.Sessions.ApprovalRequestPayload;
using ApprovalResponsePayload = DotCraft.Sessions.ApprovalResponsePayload;
using ErrorPayload = DotCraft.Sessions.ErrorPayload;
using ImageGenerationPayload = DotCraft.Sessions.ImageGenerationPayload;
using WebSearchPayload = DotCraft.Sessions.WebSearchPayload;
using ReasoningContentPayload = DotCraft.Sessions.ReasoningContentPayload;
using SystemNoticePayload = DotCraft.Sessions.SystemNoticePayload;
using TokenUsageInfo = DotCraft.Sessions.TokenUsageInfo;
using ToolCallPayload = DotCraft.Sessions.ToolCallPayload;
using ToolExecutionPayload = DotCraft.Sessions.ToolExecutionPayload;
using ToolResultPayload = DotCraft.Sessions.ToolResultPayload;
using TurnInitiatorContext = DotCraft.Sessions.TurnInitiatorContext;
using UserMessageImage = DotCraft.Sessions.UserMessageImage;
using UserMessagePayload = DotCraft.Sessions.UserMessagePayload;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public class SerializationTests
{
    private static readonly JsonSerializerOptions Opts = SessionJsonOptions.Default;

    public static TheoryData<ItemType, object?> ItemPayloads => new()
    {
        {
            ItemType.UserMessage, new UserMessagePayload
            {
                Text = "/code-review $memory @src/foo.ts",
                SenderId = "u123",
                SenderName = "Alice",
                Images = [new UserMessageImage { Path = "/workspace/.craft/attachments/images/hello.png", MimeType = "image/png", FileName = "hello.png" }],
                NativeInputParts =
                [
                    new SessionInputPart { Type = "commandRef", Name = "code-review", RawText = "/code-review" },
                    new SessionInputPart { Type = "skillRef", Name = "memory" },
                    new SessionInputPart { Type = "fileRef", Path = "src/foo.ts", DisplayPath = "src/foo.ts" }
                ],
                MaterializedInputParts = [new SessionInputPart { Type = "text", Text = "Expanded review prompt" }]
            }
        },
        { ItemType.AgentMessage, new AgentMessagePayload { Text = "Sure, I can help." } },
        { ItemType.ReasoningContent, new ReasoningContentPayload { Text = "Let me think..." } },
        {
            ItemType.ToolCall, new ToolCallPayload
            {
                Namespace = "workspace",
                ToolName = "read_file",
                ProviderFlatName = "workspace__read_file",
                Arguments = new JsonObject { ["path"] = "/tmp/file.txt" },
                CallId = "call_abc"
            }
        },
        {
            ItemType.ToolExecution, new ToolExecutionPayload
            {
                CallId = "call_abc",
                ToolName = "ListAgents",
                Status = "completed",
                Success = true,
                DurationMs = 1234,
                ResultPreview = "done"
            }
        },
        {
            ItemType.ImageGeneration, new ImageGenerationPayload
            {
                CallId = "ig_abc",
                Status = "completed",
                RevisedPrompt = "A red square",
                Result = "AQID",
                MediaType = "image/png",
                SavedPath = "<workspace>/.craft/generated_images/thread/ig_abc.png"
            }
        },
        {
            ItemType.ToolResult, new ToolResultPayload
            {
                CallId = "call_image",
                Namespace = "workspace",
                ToolName = "read_file",
                ProviderFlatName = "workspace__read_file",
                Result = "Image: sample.png (3 bytes, image/png)",
                ContentItems =
                [
                    new PluginFunctionContentItem { Type = "text", Text = "Image: sample.png" },
                    new PluginFunctionContentItem { Type = "image", MediaType = "image/png", DataBase64 = "AQID" }
                ],
                Success = true
            }
        },
        {
            ItemType.ApprovalRequest, new ApprovalRequestPayload
            {
                ApprovalType = "file",
                Operation = "write",
                Target = "/etc/config",
                RequestId = "req_001",
                ScopeKey = "file:write"
            }
        },
        {
            ItemType.ApprovalResponse, new ApprovalResponsePayload
            {
                RequestId = "req_001",
                Approved = true,
                Decision = SessionApprovalDecision.AcceptForSession
            }
        },
        { ItemType.Error, new ErrorPayload { Message = "Something went wrong", Code = "agent_error", Fatal = true } },
        {
            ItemType.SystemNotice, new SystemNoticePayload
            {
                Kind = "compacted",
                Trigger = "auto",
                Mode = "partial",
                TokensBefore = 180_000,
                TokensAfter = 44_000,
                PercentLeftAfter = 0.78,
                ClearedToolResults = 3
            }
        },
        { ItemType.UserMessage, null }
    };

    [Theory]
    [MemberData(nameof(ItemPayloads))]
    public void SessionItem_RoundTripsPayloadAsItsTypedPayload(ItemType type, object? payload)
    {
        var item = BuildItem(type, ItemStatus.Completed, payload);

        var json = JsonSerializer.Serialize(item, Opts);
        var deserialized = JsonSerializer.Deserialize<SessionItem>(json, Opts);

        Assert.NotNull(deserialized);
        if (payload is null)
            Assert.Null(deserialized.Payload);
        else
            Assert.IsType(payload.GetType(), deserialized.Payload);
        Assert.Equal(json, JsonSerializer.Serialize(deserialized, Opts));
    }

    [Fact]
    public void ToolCallPayload_MissingProviderFlatName_IsRejected()
    {
        const string json = """
            {"toolName":"read_file","callId":"call_abc","arguments":{}}
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ToolCallPayload>(json, Opts));
    }

    [Fact]
    public void Materialize_SkillRef_WritesSkillMetadataWithoutBody()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-skill-materialize-" + Guid.NewGuid().ToString("N"));
        try
        {
            var skillDir = Path.Combine(root, "skills", "memory");
            Directory.CreateDirectory(skillDir);
            var skillPath = Path.Combine(skillDir, "SKILL.md");
            File.WriteAllText(skillPath, "---\nname: memory\ndescription: test\n---\n\nUse memory.\n");

            var service = new InputMaterializationService(new CommandRegistry(), new SkillsLoader(root));
            var result = service.Materialize(
            [
                new SessionInputPart { Type = "skillRef", Name = "$memory" }
            ]);

            var materialized = Assert.Single(result.MaterializedInputParts);
            Assert.Equal("text", materialized.Type);
            Assert.Contains("<skill>", materialized.Text);
            Assert.Contains("<name>memory</name>", materialized.Text);
            Assert.Contains($"<path>{skillPath}</path>", materialized.Text);
            Assert.Contains("</skill>", materialized.Text);
            Assert.DoesNotContain("Use memory.", materialized.Text);
            Assert.DoesNotContain("[Requested " + "Skill:", materialized.Text);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildDisplayText_UsesNativeTagSyntaxForStructuredParts()
    {
        var text = SessionWireMapper.BuildDisplayText(
        [
            new SessionInputPart { Type = "text", Text = "Check " },
            new SessionInputPart { Type = "fileRef", Path = "src/foo.ts", DisplayPath = "src/foo.ts" },
            new SessionInputPart { Type = "text", Text = " then " },
            new SessionInputPart { Type = "commandRef", Name = "code-review", ArgsText = "--fast" },
            new SessionInputPart { Type = "text", Text = " and " },
            new SessionInputPart { Type = "skillRef", Name = "$memory" },
            new SessionInputPart { Type = "text", Text = " " },
            new SessionInputPart { Type = "skillRef", Name = "browser_use" },
            new SessionInputPart { Type = "text", Text = "\n" },
            new SessionInputPart { Type = "fileRef", Path = "Z:\\synthetic\\notes.txt", DisplayPath = "Z:\\synthetic\\notes.txt" },
            new SessionInputPart { Type = "text", Text = "\n" },
            new SessionInputPart { Type = "fileRef", Path = "/synthetic/brief.md", DisplayPath = "/synthetic/brief.md" }
        ]);

        Assert.Equal(
            "Check @src/foo.ts then /code-review --fast and $memory $browser_use\n@Z:\\synthetic\\notes.txt\n@/synthetic/brief.md",
            text);
    }

    [Fact]
    public void InputMaterialization_AcceptsAbsoluteFileRefPaths()
    {
        var service = new InputMaterializationService(new CommandRegistry(), skillsLoader: null);
        var result = service.Materialize(
        [
            new SessionInputPart { Type = "fileRef", Path = "Z:\\synthetic\\notes.txt", DisplayPath = "Z:\\synthetic\\notes.txt" },
            new SessionInputPart { Type = "text", Text = "\n\nSummarize it" }
        ]);

        Assert.Collection(result.NativeInputParts,
            part =>
            {
                Assert.Equal("fileRef", part.Type);
                Assert.Equal("Z:\\synthetic\\notes.txt", part.Path);
                Assert.Equal("Z:\\synthetic\\notes.txt", part.DisplayPath);
            },
            part => Assert.Equal("\n\nSummarize it", part.Text));
        Assert.Collection(result.MaterializedInputParts,
            part => Assert.Equal("@Z:\\synthetic\\notes.txt", part.Text),
            part => Assert.Equal("\n\nSummarize it", part.Text));
        Assert.Equal("@Z:\\synthetic\\notes.txt\n\nSummarize it", result.DisplayText);
    }

    [Fact]
    public void SessionItem_WebSearch_MapsToTheTypedContractPayload()
    {
        var item = BuildItem(ItemType.WebSearch, ItemStatus.Completed,
            new WebSearchPayload
            {
                CallId = "ws_abc",
                Status = "completed",
                Query = "https://example.com/a",
                Action = new JsonObject { ["type"] = "openPage", ["url"] = "https://example.com/a" },
                Results = [new JsonObject { ["domain"] = "example.com", ["ref_id"] = "turn1fetch0" }],
                Arguments = new JsonObject { ["open"] = new JsonArray(new JsonObject { ["ref_id"] = "https://example.com/a" }) },
                Output = "page text"
            });

        Assert.Equal("page text", RoundTrip(item).AsWebSearch!.Output);
        var contract = AppServerContractMapper.ToContract(item.ToWire());
        Assert.Equal("webSearch", contract.PayloadKind);
        Assert.True(DotCraft.Protocol.SessionItemPayloadParser.Parse(contract)
            .TryGet<DotCraft.Protocol.AppServer.WebSearchPayload>(out var payload));
        Assert.Equal("ws_abc", payload!.CallId);
        Assert.Equal("completed", payload.Status);
        Assert.Equal("openPage", payload.Action!.Type);
        Assert.Equal("https://example.com/a", payload.Action.Url);
        Assert.Equal("https://example.com/a", payload.Query);
        Assert.Equal("example.com", Assert.Single(payload.Results!).GetProperty("domain").GetString());
        Assert.Equal("page text", payload.Output);
    }

    [Fact]
    public void SessionThread_WithTurns_RoundTripsInputPayloadsAndTurnFields()
    {
        var thread = BuildThreadWithTurns();
        thread.UserId = "local";
        thread.DisplayName = "Help me fix the login bug";
        thread.HistoryMode = HistoryMode.Server;
        thread.Metadata = new Dictionary<string, string> { ["qqGroupId"] = "12345" };
        var turn = thread.Turns[0];
        turn.Input = turn.Items[0];
        turn.TokenUsage = new TokenUsageInfo { InputTokens = 100, OutputTokens = 50, TotalTokens = 150 };
        turn.Initiator = new TurnInitiatorContext
        {
            ChannelName = "wecom",
            UserId = "user-123",
            UserName = "Akihiko",
            UserRole = "admin",
            ChannelContext = "chat:abc",
            GroupId = "chat:abc"
        };
        var secondInput = BuildItem(ItemType.UserMessage, ItemStatus.Completed, new UserMessagePayload { Text = "你有哪些工具呢" });
        secondInput.TurnId = "turn_002";
        thread.Turns.Add(new SessionTurn
        {
            Id = "turn_002",
            ThreadId = thread.Id,
            Status = TurnStatus.Running,
            StartedAt = new DateTimeOffset(2026, 3, 16, 10, 2, 0, TimeSpan.Zero),
            Input = secondInput,
            Items = [secondInput]
        });

        var json = JsonSerializer.Serialize(thread, Opts);
        var loaded = JsonSerializer.Deserialize<SessionThread>(json, Opts);

        Assert.NotNull(loaded);
        Assert.Equal(json, JsonSerializer.Serialize(loaded, Opts));
        Assert.Equal("Hello", Assert.IsType<UserMessagePayload>(loaded.Turns[0].Input!.Payload).Text);
        Assert.Equal("你有哪些工具呢", Assert.IsType<UserMessagePayload>(loaded.Turns[1].Input!.Payload).Text);
        Assert.Equal(["item_001", "item_002"], loaded.Turns[0].Items.Select(item => item.Id));
        Assert.Equal("admin", loaded.Turns[0].Initiator!.UserRole);
        Assert.Null(loaded.Turns[1].CompletedAt);
        Assert.Null(loaded.Turns[1].TokenUsage);
        Assert.Null(loaded.Configuration);
    }

    [Fact]
    public void SessionEvent_RoundTripsPayloadsAndUnwrapsNestedTurns()
    {
        var thread = BuildThreadWithTurns();
        var cancelledTurn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = "thread_001",
            Status = TurnStatus.Cancelled,
            StartedAt = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero)
        };
        var failedTurn = new SessionTurn
        {
            Id = "turn_002",
            ThreadId = "thread_001",
            Status = TurnStatus.Failed,
            StartedAt = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
            Error = "Model context exceeded"
        };

        var resumed = RoundTrip(BuildEvent(SessionEventType.ThreadResumed, new ThreadResumedPayload { Thread = thread, ResumedBy = "vscode" }));
        Assert.Equal("vscode", resumed.ResumedPayload!.ResumedBy);
        Assert.Equal("thread_001", resumed.ResumedPayload.Thread.Id);
        Assert.Null(resumed.TurnPayload);

        var cancelled = RoundTrip(BuildEvent(SessionEventType.TurnCancelled, new TurnCancelledPayload { Turn = cancelledTurn, Reason = "Caller cancelled" }));
        Assert.Equal("Caller cancelled", cancelled.TurnCancelledPayload!.Reason);
        Assert.Equal("turn_001", cancelled.TurnPayload?.Id);

        var failed = RoundTrip(BuildEvent(SessionEventType.TurnFailed, new TurnFailedPayload { Turn = failedTurn, Error = "Model context exceeded" }));
        Assert.Equal("Model context exceeded", failed.TurnFailedPayload!.Error);
        Assert.Equal("turn_002", failed.TurnPayload?.Id);
        Assert.Equal("Model context exceeded", failed.TurnPayload?.Error);

        var delta = RoundTrip(BuildEvent(SessionEventType.ItemDelta, new AgentMessageDelta { TextDelta = "Hello " }));
        Assert.Equal("Hello ", delta.DeltaPayload!.TextDelta);

        var status = RoundTrip(BuildEvent(SessionEventType.ThreadStatusChanged, new ThreadStatusChangedPayload
        {
            PreviousStatus = ThreadStatus.Active,
            NewStatus = ThreadStatus.Archived
        }));
        Assert.Equal(ThreadStatus.Archived, status.StatusChangedPayload!.NewStatus);
    }

    [Fact]
    public void SequenceHelpers_ContinueAfterMaximumValidHistoricalId()
    {
        var turns = new[]
        {
            new SessionTurn { Id = "turn_002", ThreadId = "thread_1" },
            new SessionTurn { Id = "turn_invalid", ThreadId = "thread_1" },
            new SessionTurn { Id = "turn_009", ThreadId = "thread_1" }
        };
        var items = new[]
        {
            new SessionItem { Id = "item_004", TurnId = "turn_009" },
            new SessionItem { Id = "item_invalid", TurnId = "turn_009" },
            new SessionItem { Id = "item_011", TurnId = "turn_009" }
        };

        Assert.Equal(10, SessionIdGenerator.NextTurnSequence(turns));
        Assert.Equal(11, SessionIdGenerator.LastItemSequence(items));

        var thread = new SessionThread
        {
            Id = "thread_1",
            Turns = [turns[0]],
            TurnSequenceHighWatermark = 9
        };
        Assert.Equal(10, SessionIdGenerator.ReserveNextTurnSequence(thread));
        thread.Turns.Clear();
        Assert.Equal(11, SessionIdGenerator.ReserveNextTurnSequence(thread));
        Assert.DoesNotContain(
            "turnSequenceHighWatermark",
            JsonSerializer.Serialize(thread, SessionJsonOptions.Default),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TokenUsageInfo_Addition()
    {
        var a = new TokenUsageInfo { InputTokens = 100, OutputTokens = 50, CachedInputTokens = 20, CacheWriteInputTokens = 5, LlmCallCount = 1, TotalTokens = 150 };
        var b = new TokenUsageInfo { InputTokens = 200, OutputTokens = 80, CachedInputTokens = 30, CacheWriteInputTokens = 7, LlmCallCount = 2, TotalTokens = 280 };
        var sum = a + b;
        Assert.Equal(300, sum.InputTokens);
        Assert.Equal(130, sum.OutputTokens);
        Assert.Equal(50, sum.CachedInputTokens);
        Assert.Equal(12, sum.CacheWriteInputTokens);
        Assert.Equal(3, sum.LlmCallCount);
        Assert.Equal(238, sum.FreshInputTokens);
        Assert.Equal(430, sum.TotalTokens);
    }

    [Fact]
    public void SessionWireEvent_ProducesContractShapes()
    {
        var completed = WireJson(BuildEvent(SessionEventType.TurnCompleted, new SessionTurn
        {
            Id = "turn_001",
            ThreadId = "thread_001",
            Status = TurnStatus.Completed,
            OriginChannel = "cli",
            StartedAt = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2026, 3, 16, 10, 1, 0, TimeSpan.Zero)
        }));
        Assert.Contains("\"eventType\":\"turnCompleted\"", completed, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"completed\"", completed, StringComparison.Ordinal);
        Assert.Contains("\"payloadKind\":\"turn\"", completed, StringComparison.Ordinal);

        var agentDelta = WireJson(BuildEvent(SessionEventType.ItemDelta, new AgentMessageDelta { TextDelta = "hello world" }));
        Assert.Contains("\"payloadKind\":\"agentMessageDelta\"", agentDelta, StringComparison.Ordinal);
        Assert.Contains("\"delta\":\"hello world\"", agentDelta, StringComparison.Ordinal);
        Assert.DoesNotContain("\"textDelta\"", agentDelta, StringComparison.Ordinal);
        Assert.DoesNotContain("\"deltaKind\"", agentDelta, StringComparison.Ordinal);

        var reasoningDelta = WireJson(BuildEvent(SessionEventType.ItemDelta, new ReasoningContentDelta { TextDelta = "thinking..." }));
        Assert.Contains("\"payloadKind\":\"reasoningContentDelta\"", reasoningDelta, StringComparison.Ordinal);
        Assert.Contains("\"delta\":\"thinking...\"", reasoningDelta, StringComparison.Ordinal);
        Assert.DoesNotContain("\"textDelta\"", reasoningDelta, StringComparison.Ordinal);

        var status = WireJson(BuildEvent(SessionEventType.ThreadStatusChanged, new ThreadStatusChangedPayload
        {
            PreviousStatus = ThreadStatus.Active,
            NewStatus = ThreadStatus.Archived
        }));
        Assert.Contains("\"threadId\":\"thread_001\"", status, StringComparison.Ordinal);
        Assert.Contains("\"previousStatus\":\"active\"", status, StringComparison.Ordinal);
        Assert.Contains("\"newStatus\":\"archived\"", status, StringComparison.Ordinal);

        var failed = WireJson(BuildEvent(SessionEventType.TurnFailed, new TurnFailedPayload
        {
            Turn = new SessionTurn { Id = "turn_002", ThreadId = "thread_001", Status = TurnStatus.Failed },
            Error = "Context window exceeded"
        }));
        Assert.Contains("\"error\":\"Context window exceeded\"", failed, StringComparison.Ordinal);
        Assert.Contains("\"turn\":", failed, StringComparison.Ordinal);
        Assert.Contains("\"payloadKind\":\"turnFailed\"", failed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SessionApprovalDecision.AcceptOnce, "accept")]
    [InlineData(SessionApprovalDecision.AcceptForSession, "acceptForSession")]
    [InlineData(SessionApprovalDecision.AcceptAlways, "acceptAlways")]
    [InlineData(SessionApprovalDecision.Reject, "decline")]
    [InlineData(SessionApprovalDecision.CancelTurn, "cancel")]
    public void WireApprovalDecision_UsesWireNames(SessionApprovalDecision decision, string expectedWire)
    {
        Assert.Equal($"\"{expectedWire}\"", JsonSerializer.Serialize(decision, SessionWireJsonOptions.Default));
        Assert.Equal(decision, JsonSerializer.Deserialize<SessionApprovalDecision>($"\"{expectedWire}\"", SessionWireJsonOptions.Default));
    }

    [Theory]
    [InlineData(SessionEventType.ThreadCreated, "thread/started")]
    [InlineData(SessionEventType.ThreadResumed, "thread/resumed")]
    [InlineData(SessionEventType.TurnStarted, "turn/started")]
    [InlineData(SessionEventType.TurnCompleted, "turn/completed")]
    [InlineData(SessionEventType.TurnFailed, "turn/failed")]
    [InlineData(SessionEventType.TurnCancelled, "turn/cancelled")]
    [InlineData(SessionEventType.ItemStarted, "item/started")]
    [InlineData(SessionEventType.ItemCompleted, "item/completed")]
    [InlineData(SessionEventType.ApprovalRequested, "item/approval/request")]
    [InlineData(SessionEventType.ApprovalResolved, "item/approval/resolved")]
    public void ToWireMethodName_MapsEventTypeToMethodName(SessionEventType eventType, string expected)
    {
        var evt = new SessionEvent { EventType = eventType };
        Assert.Equal(expected, evt.ToWireMethodName());
    }

    [Fact]
    public void ToWireMethodName_ItemDelta_DependsOnDeltaKind()
    {
        Assert.Equal("item/agentMessage/delta", new SessionEvent
        {
            EventType = SessionEventType.ItemDelta,
            Payload = new AgentMessageDelta { TextDelta = "hello" }
        }.ToWireMethodName());
        Assert.Equal("item/reasoning/delta", new SessionEvent
        {
            EventType = SessionEventType.ItemDelta,
            Payload = new ReasoningContentDelta { TextDelta = "thinking" }
        }.ToWireMethodName());
    }

    [Fact]
    public void SessionInputPart_MapsToAndFromAIContent()
    {
        Assert.Equal("Hello world", Assert.IsType<TextContent>(new SessionInputPart { Type = "text", Text = "Hello world" }.ToAIContent()).Text);

        var inline = Assert.IsType<DataContent>(new SessionInputPart { Type = "image", Url = "data:image/png;base64,AQID" }.ToAIContent());
        Assert.Equal("image/png", inline.MediaType);
        Assert.Equal([1, 2, 3], inline.Data.ToArray());

        var remote = Assert.IsType<TextContent>(new SessionInputPart { Type = "image", Url = "https://example.com/img.png" }.ToAIContent());
        Assert.Equal(SessionInputPartResolver.RemoteImageOmittedText, remote.Text);
        Assert.DoesNotContain("example.com", remote.Text);

        var local = Assert.IsType<TextContent>(new SessionInputPart { Type = "localImage", Path = "/synthetic/screenshot.png" }.ToAIContent());
        Assert.Contains("/synthetic/screenshot.png", local.Text);

        var textPart = new TextContent("Hello").ToWireInputPart();
        Assert.Equal("text", textPart.Type);
        Assert.Equal("Hello", textPart.Text);

        const string dataUri = "data:image/png;base64,iVBORw0KGgo=";
        var imagePart = new DataContent(dataUri, "image/png").ToWireInputPart();
        Assert.Equal("image", imagePart.Type);
        Assert.Equal(dataUri, imagePart.Url);
    }

    [Fact]
    public void SessionWireThread_ToWire_IncludesTurnsOnlyWhenRequested()
    {
        var thread = BuildThreadWithTurns();
        Assert.Null(thread.ToWire(includeTurns: false).Turns);

        var wireTurn = Assert.Single(thread.ToWire(includeTurns: true).Turns!);
        Assert.Equal("turn_001", wireTurn.Id);
        Assert.Equal(["item_001", "item_002"], wireTurn.Items!.Select(item => item.Id));
    }

    private static SessionItem BuildItem(ItemType type, ItemStatus status, object? payload)
    {
        var item = new SessionItem
        {
            Id = "item_001",
            TurnId = "turn_001",
            Type = type,
            Status = status,
            CreatedAt = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero)
        };
        item.Payload = payload;
        return item;
    }

    private static SessionEvent BuildEvent(SessionEventType type, object payload) => new()
    {
        EventId = "evt_001",
        EventType = type,
        ThreadId = "thread_001",
        TurnId = "turn_001",
        Timestamp = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
        Payload = payload
    };

    private static SessionItem RoundTrip(SessionItem item)
    {
        var result = JsonSerializer.Deserialize<SessionItem>(JsonSerializer.Serialize(item, Opts), Opts);
        Assert.NotNull(result);
        return result;
    }

    private static SessionEvent RoundTrip(SessionEvent evt)
    {
        var result = JsonSerializer.Deserialize<SessionEvent>(JsonSerializer.Serialize(evt, Opts), Opts);
        Assert.NotNull(result);
        Assert.Equal(evt.EventType, result.EventType);
        return result;
    }

    private static string WireJson(SessionEvent evt) =>
        JsonSerializer.Serialize(evt.ToWire(), SessionWireJsonOptions.Default);

    private static SessionThread BuildThreadWithTurns()
    {
        var item1 = new SessionItem
        {
            Id = "item_001",
            TurnId = "turn_001",
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
            Payload = new UserMessagePayload { Text = "Hello" }
        };
        var item2 = new SessionItem
        {
            Id = "item_002",
            TurnId = "turn_001",
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = new DateTimeOffset(2026, 3, 16, 10, 1, 0, TimeSpan.Zero),
            Payload = new AgentMessagePayload { Text = "Hi there" }
        };
        var turn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = "thread_001",
            Status = TurnStatus.Completed,
            StartedAt = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
            Items = [item1, item2]
        };
        return new SessionThread
        {
            Id = "thread_001",
            WorkspacePath = "/workspace",
            OriginChannel = "cli",
            Status = ThreadStatus.Active,
            CreatedAt = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
            LastActiveAt = new DateTimeOffset(2026, 3, 16, 10, 1, 0, TimeSpan.Zero),
            Turns = [turn]
        };
    }
}
