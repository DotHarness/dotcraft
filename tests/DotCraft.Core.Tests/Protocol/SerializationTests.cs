using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Commands.Core;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Skills;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Sessions.Protocol;

public class SerializationTests
{
    private static readonly JsonSerializerOptions Opts = SessionJsonOptions.Default;

    // -------------------------------------------------------------------------
    // SessionItem round-trips
    // -------------------------------------------------------------------------

    [Fact]
    public void SessionItem_UserMessage_RoundTrip()
    {
        var item = new SessionItem
        {
            Id = "item_001",
            TurnId = "turn_001",
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2026, 3, 15, 10, 0, 1, TimeSpan.Zero)
        };
        item.Payload = new UserMessagePayload
        {
            Text = "Hello!",
            SenderId = "u123",
            SenderName = "Alice",
            Images =
            [
                new UserMessageImage
                {
                    Path = "/workspace/.craft/attachments/images/hello.png",
                    MimeType = "image/png",
                    FileName = "hello.png"
                }
            ]
        };

        var json = JsonSerializer.Serialize(item, Opts);
        var deserialized = JsonSerializer.Deserialize<SessionItem>(json, Opts);

        Assert.NotNull(deserialized);
        Assert.Equal(item.Id, deserialized.Id);
        Assert.Equal(item.TurnId, deserialized.TurnId);
        Assert.Equal(item.Type, deserialized.Type);
        Assert.Equal(item.Status, deserialized.Status);
        Assert.Equal(item.CreatedAt, deserialized.CreatedAt);
        Assert.Equal(item.CompletedAt, deserialized.CompletedAt);

        var payload = deserialized.AsUserMessage;
        Assert.NotNull(payload);
        Assert.Equal("Hello!", payload.Text);
        Assert.Equal("u123", payload.SenderId);
        Assert.Equal("Alice", payload.SenderName);
        Assert.NotNull(payload.Images);
        Assert.Single(payload.Images!);
        Assert.Equal("/workspace/.craft/attachments/images/hello.png", payload.Images[0].Path);
        Assert.Equal("image/png", payload.Images[0].MimeType);
        Assert.Equal("hello.png", payload.Images[0].FileName);
    }

    [Fact]
    public void SessionItem_UserMessage_RoundTrip_PreservesNativeAndMaterializedInputParts()
    {
        var item = BuildItem(ItemType.UserMessage, ItemStatus.Completed, new UserMessagePayload
        {
            Text = "/code-review $memory @src/foo.ts",
            NativeInputParts =
            [
                new SessionWireInputPart { Type = "commandRef", Name = "code-review", RawText = "/code-review" },
                new SessionWireInputPart { Type = "text", Text = " " },
                new SessionWireInputPart { Type = "skillRef", Name = "memory" },
                new SessionWireInputPart { Type = "text", Text = " " },
                new SessionWireInputPart { Type = "fileRef", Path = "src/foo.ts", DisplayPath = "src/foo.ts" }
            ],
            MaterializedInputParts =
            [
                new SessionWireInputPart { Type = "text", Text = "Expanded review prompt" },
                new SessionWireInputPart { Type = "text", Text = "\n\n" },
                new SessionWireInputPart { Type = "text", Text = "<skill>\n<name>memory</name>\n<path>skills/memory/SKILL.md</path>\n</skill>" }
            ]
        });

        var deserialized = RoundTrip(item);
        var payload = deserialized.AsUserMessage;

        Assert.NotNull(payload);
        Assert.NotNull(payload.NativeInputParts);
        Assert.NotNull(payload.MaterializedInputParts);
        Assert.Collection(payload.NativeInputParts!,
            part =>
            {
                Assert.Equal("commandRef", part.Type);
                Assert.Equal("code-review", part.Name);
                Assert.Equal("/code-review", part.RawText);
            },
            part => Assert.Equal(" ", part.Text),
            part =>
            {
                Assert.Equal("skillRef", part.Type);
                Assert.Equal("memory", part.Name);
            },
            part => Assert.Equal(" ", part.Text),
            part =>
            {
                Assert.Equal("fileRef", part.Type);
                Assert.Equal("src/foo.ts", part.Path);
                Assert.Equal("src/foo.ts", part.DisplayPath);
            });
        Assert.Collection(payload.MaterializedInputParts!,
            part => Assert.Equal("Expanded review prompt", part.Text),
            part => Assert.Equal("\n\n", part.Text),
            part => Assert.Equal("<skill>\n<name>memory</name>\n<path>skills/memory/SKILL.md</path>\n</skill>", part.Text));
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
                new SessionWireInputPart { Type = "skillRef", Name = "$memory" }
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
            new SessionWireInputPart { Type = "text", Text = "Check " },
            new SessionWireInputPart { Type = "fileRef", Path = "src/foo.ts", DisplayPath = "src/foo.ts" },
            new SessionWireInputPart { Type = "text", Text = " then " },
            new SessionWireInputPart { Type = "commandRef", Name = "code-review", ArgsText = "--fast" },
            new SessionWireInputPart { Type = "text", Text = " and " },
            new SessionWireInputPart { Type = "skillRef", Name = "$memory" }
        ]);

        Assert.Equal("Check @src/foo.ts then /code-review --fast and $memory", text);
    }

    [Fact]
    public void BuildDisplayText_PreservesAbsoluteFileRefPaths()
    {
        var text = SessionWireMapper.BuildDisplayText(
        [
            new SessionWireInputPart { Type = "fileRef", Path = "C:\\temp\\notes.txt", DisplayPath = "C:\\temp\\notes.txt" },
            new SessionWireInputPart { Type = "text", Text = "\n" },
            new SessionWireInputPart { Type = "fileRef", Path = "/tmp/brief.md", DisplayPath = "/tmp/brief.md" }
        ]);

        Assert.Equal("@C:\\temp\\notes.txt\n@/tmp/brief.md", text);
    }

    [Fact]
    public void InputMaterialization_AcceptsAbsoluteFileRefPaths()
    {
        var service = new InputMaterializationService(new CommandRegistry(), skillsLoader: null);
        var result = service.Materialize(
        [
            new SessionWireInputPart { Type = "fileRef", Path = "C:\\temp\\notes.txt", DisplayPath = "C:\\temp\\notes.txt" },
            new SessionWireInputPart { Type = "text", Text = "\n\nSummarize it" }
        ]);

        Assert.Collection(result.NativeInputParts,
            part =>
            {
                Assert.Equal("fileRef", part.Type);
                Assert.Equal("C:\\temp\\notes.txt", part.Path);
                Assert.Equal("C:\\temp\\notes.txt", part.DisplayPath);
            },
            part => Assert.Equal("\n\nSummarize it", part.Text));
        Assert.Collection(result.MaterializedInputParts,
            part => Assert.Equal("@C:\\temp\\notes.txt", part.Text),
            part => Assert.Equal("\n\nSummarize it", part.Text));
        Assert.Equal("@C:\\temp\\notes.txt\n\nSummarize it", result.DisplayText);
    }

    [Fact]
    public void BuildDisplayText_PreservesUnderscoreSkillNames()
    {
        var text = SessionWireMapper.BuildDisplayText(
        [
            new SessionWireInputPart { Type = "text", Text = "Use " },
            new SessionWireInputPart { Type = "skillRef", Name = "browser_use" },
            new SessionWireInputPart { Type = "text", Text = " please" }
        ]);

        Assert.Equal("Use $browser_use please", text);
    }

    [Fact]
    public void SessionItem_AgentMessage_RoundTrip()
    {
        var item = BuildItem(ItemType.AgentMessage, ItemStatus.Completed,
            new AgentMessagePayload { Text = "Sure, I can help." });

        var deserialized = RoundTrip(item);
        var payload = deserialized.AsAgentMessage;
        Assert.NotNull(payload);
        Assert.Equal("Sure, I can help.", payload.Text);
    }

    [Fact]
    public void SessionItem_ReasoningContent_RoundTrip()
    {
        var item = BuildItem(ItemType.ReasoningContent, ItemStatus.Completed,
            new ReasoningContentPayload { Text = "Let me think..." });

        var deserialized = RoundTrip(item);
        Assert.NotNull(deserialized.AsReasoningContent);
        Assert.Equal("Let me think...", deserialized.AsReasoningContent!.Text);
    }

    [Fact]
    public void SessionItem_ToolCall_RoundTrip()
    {
        var args = new JsonObject { ["path"] = "/tmp/file.txt" };
        var item = BuildItem(ItemType.ToolCall, ItemStatus.Completed,
            new ToolCallPayload { ToolName = "read_file", Arguments = args, CallId = "call_abc" });

        var deserialized = RoundTrip(item);
        var payload = deserialized.AsToolCall;
        Assert.NotNull(payload);
        Assert.Equal("read_file", payload.ToolName);
        Assert.Equal("call_abc", payload.CallId);
        Assert.NotNull(payload.Arguments);
    }

    [Fact]
    public void SessionItem_ToolExecution_RoundTrip()
    {
        var item = BuildItem(ItemType.ToolExecution, ItemStatus.Completed,
            new ToolExecutionPayload
            {
                CallId = "call_abc",
                ToolName = "WaitAgent",
                Status = "completed",
                Success = true,
                DurationMs = 1234,
                ResultPreview = "done",
                ErrorMessage = null
            });

        var deserialized = RoundTrip(item);
        var payload = deserialized.AsToolExecution;
        Assert.NotNull(payload);
        Assert.Equal("call_abc", payload.CallId);
        Assert.Equal("WaitAgent", payload.ToolName);
        Assert.Equal("completed", payload.Status);
        Assert.True(payload.Success);
        Assert.Equal(1234, payload.DurationMs);
        Assert.Equal("done", payload.ResultPreview);
    }

    [Fact]
    public void SessionItem_ToolResult_RoundTrip()
    {
        var item = BuildItem(ItemType.ToolResult, ItemStatus.Completed,
            new ToolResultPayload { CallId = "call_abc", Result = "file contents", Success = true });

        var deserialized = RoundTrip(item);
        var payload = deserialized.AsToolResult;
        Assert.NotNull(payload);
        Assert.Equal("call_abc", payload.CallId);
        Assert.Equal("file contents", payload.Result);
        Assert.True(payload.Success);
    }

    [Fact]
    public void SessionItem_ApprovalRequest_RoundTrip()
    {
        var item = BuildItem(ItemType.ApprovalRequest, ItemStatus.Completed,
            new ApprovalRequestPayload
            {
                ApprovalType = "file",
                Operation = "write",
                Target = "/etc/config",
                RequestId = "req_001",
                ScopeKey = "file:write"
            });

        var deserialized = RoundTrip(item);
        var payload = deserialized.AsApprovalRequest;
        Assert.NotNull(payload);
        Assert.Equal("file", payload.ApprovalType);
        Assert.Equal("write", payload.Operation);
        Assert.Equal("/etc/config", payload.Target);
        Assert.Equal("req_001", payload.RequestId);
        Assert.Equal("file:write", payload.ScopeKey);
    }

    [Fact]
    public void SessionItem_ApprovalResponse_RoundTrip()
    {
        var item = BuildItem(ItemType.ApprovalResponse, ItemStatus.Completed,
            new ApprovalResponsePayload
            {
                RequestId = "req_001",
                Approved = true,
                Decision = SessionApprovalDecision.AcceptForSession
            });

        var deserialized = RoundTrip(item);
        var payload = deserialized.AsApprovalResponse;
        Assert.NotNull(payload);
        Assert.Equal("req_001", payload.RequestId);
        Assert.True(payload.Approved);
        Assert.Equal(SessionApprovalDecision.AcceptForSession, payload.Decision);
    }

    [Fact]
    public void SessionItem_Error_RoundTrip()
    {
        var item = BuildItem(ItemType.Error, ItemStatus.Completed,
            new ErrorPayload { Message = "Something went wrong", Code = "agent_error", Fatal = true });

        var deserialized = RoundTrip(item);
        var payload = deserialized.AsError;
        Assert.NotNull(payload);
        Assert.Equal("Something went wrong", payload.Message);
        Assert.Equal("agent_error", payload.Code);
        Assert.True(payload.Fatal);
    }

    [Fact]
    public void SessionItem_SystemNotice_RoundTrip()
    {
        var item = BuildItem(ItemType.SystemNotice, ItemStatus.Completed,
            new SystemNoticePayload
            {
                Kind = "compacted",
                Trigger = "auto",
                Mode = "partial",
                TokensBefore = 180_000,
                TokensAfter = 44_000,
                PercentLeftAfter = 0.78,
                ClearedToolResults = 3
            });

        var deserialized = RoundTrip(item);
        var payload = deserialized.AsSystemNotice;
        Assert.NotNull(payload);
        Assert.Equal("compacted", payload!.Kind);
        Assert.Equal("auto", payload.Trigger);
        Assert.Equal("partial", payload.Mode);
        Assert.Equal(180_000, payload.TokensBefore);
        Assert.Equal(44_000, payload.TokensAfter);
        Assert.Equal(0.78, payload.PercentLeftAfter, 3);
        Assert.Equal(3, payload.ClearedToolResults);
    }

    [Fact]
    public void SessionWireEvent_SystemNoticeItem_ProducesSystemNoticePayloadKind()
    {
        var item = BuildItem(ItemType.SystemNotice, ItemStatus.Completed,
            new SystemNoticePayload
            {
                Kind = "compacted",
                Trigger = "reactive",
                Mode = "micro",
                TokensBefore = 200_000,
                TokensAfter = 100_000,
                PercentLeftAfter = 0.5,
                ClearedToolResults = 1
            });
        var evt = new SessionEvent
        {
            EventId = "evt_sn1",
            EventType = SessionEventType.ItemCompleted,
            ThreadId = "thread_001",
            TurnId = "turn_001",
            ItemId = item.Id,
            Timestamp = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
            Payload = item
        };

        var json = JsonSerializer.Serialize(evt.ToWire(), SessionWireJsonOptions.Default);

        Assert.Contains("\"type\":\"systemNotice\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"compacted\"", json, StringComparison.Ordinal);
        Assert.Contains("\"trigger\":\"reactive\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionWireThread_ContextUsage_RoundTripsThroughWire()
    {
        var thread = new SessionThread
        {
            Id = "thread_001",
            WorkspacePath = "/workspace",
            OriginChannel = "cli",
            Status = ThreadStatus.Active,
            CreatedAt = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
            LastActiveAt = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero)
        };
        var wire = thread.ToWire() with
        {
            ContextUsage = new ContextUsageSnapshot
            {
                Tokens = 48_000,
                ContextWindow = 200_000,
                AutoCompactThreshold = 180_000,
                WarningThreshold = 176_000,
                ErrorThreshold = 194_000,
                PercentLeft = 0.76
            }
        };

        var json = JsonSerializer.Serialize(wire, SessionWireJsonOptions.Default);
        Assert.Contains("\"contextUsage\":", json, StringComparison.Ordinal);
        Assert.Contains("\"tokens\":48000", json, StringComparison.Ordinal);
        Assert.Contains("\"contextWindow\":200000", json, StringComparison.Ordinal);
        Assert.Contains("\"autoCompactThreshold\":180000", json, StringComparison.Ordinal);
    }

    [Fact]
    public void UsageDeltaPayload_CumulativeTotals_RoundTrip()
    {
        var payload = new UsageDeltaPayload
        {
            InputTokens = 1200,
            OutputTokens = 350,
            CachedInputTokens = 900,
            LlmCallDelta = 1,
            TotalInputTokens = 14_820,
            TotalOutputTokens = 2_610,
            TurnInputTokens = 73_000,
            TurnLlmCalls = 3
        };

        var json = JsonSerializer.Serialize(payload, Opts);
        var deserialized = JsonSerializer.Deserialize<UsageDeltaPayload>(json, Opts);

        Assert.NotNull(deserialized);
        Assert.Equal(1200, deserialized!.InputTokens);
        Assert.Equal(350, deserialized.OutputTokens);
        Assert.Equal(900, deserialized.CachedInputTokens);
        Assert.Equal(300, deserialized.FreshInputTokens);
        Assert.Equal(1, deserialized.LlmCallDelta);
        Assert.Equal(14_820, deserialized.TotalInputTokens);
        Assert.Equal(2_610, deserialized.TotalOutputTokens);
        Assert.Equal(73_000, deserialized.TurnInputTokens);
        Assert.Equal(3, deserialized.TurnLlmCalls);
        Assert.Contains("\"totalInputTokens\":14820", json, StringComparison.Ordinal);
        Assert.Contains("\"totalOutputTokens\":2610", json, StringComparison.Ordinal);
        Assert.Contains("\"turnInputTokens\":73000", json, StringComparison.Ordinal);
        Assert.Contains("\"turnLlmCalls\":3", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionItem_NullPayload_RoundTrip()
    {
        var item = BuildItem(ItemType.UserMessage, ItemStatus.Started, null);
        var deserialized = RoundTrip(item);
        Assert.Null(deserialized.Payload);
    }

    // -------------------------------------------------------------------------
    // SessionTurn round-trips
    // -------------------------------------------------------------------------

    [Fact]
    public void SessionTurn_RoundTrip_PreservesItemOrder()
    {
        var turn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = "thread_20260315_abc123",
            Status = TurnStatus.Completed,
            StartedAt = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2026, 3, 15, 10, 2, 0, TimeSpan.Zero),
            TokenUsage = new TokenUsageInfo { InputTokens = 100, OutputTokens = 50, TotalTokens = 150 },
            OriginChannel = "cli"
        };

        var userMsg = BuildItem(ItemType.UserMessage, ItemStatus.Completed,
            new UserMessagePayload { Text = "Hello" });
        userMsg.Id = "item_001";
        userMsg.TurnId = "turn_001";

        var agentMsg = BuildItem(ItemType.AgentMessage, ItemStatus.Completed,
            new AgentMessagePayload { Text = "Hi there" });
        agentMsg.Id = "item_002";
        agentMsg.TurnId = "turn_001";

        turn.Input = userMsg;
        turn.Items = [userMsg, agentMsg];

        var json = JsonSerializer.Serialize(turn, Opts);
        var deserialized = JsonSerializer.Deserialize<SessionTurn>(json, Opts);

        Assert.NotNull(deserialized);
        Assert.Equal(turn.Id, deserialized.Id);
        Assert.Equal(turn.ThreadId, deserialized.ThreadId);
        Assert.Equal(TurnStatus.Completed, deserialized.Status);
        Assert.Equal(2, deserialized.Items.Count);
        Assert.Equal("item_001", deserialized.Items[0].Id);
        Assert.Equal("item_002", deserialized.Items[1].Id);
        Assert.NotNull(deserialized.TokenUsage);
        Assert.Equal(100, deserialized.TokenUsage.InputTokens);
        Assert.Equal("cli", deserialized.OriginChannel);
    }

    [Fact]
    public void SessionTurn_NullableFields_RoundTrip()
    {
        var turn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = "thread_x",
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
            // CompletedAt, TokenUsage, Error are null
        };

        var json = JsonSerializer.Serialize(turn, Opts);
        var deserialized = JsonSerializer.Deserialize<SessionTurn>(json, Opts);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.CompletedAt);
        Assert.Null(deserialized.TokenUsage);
        Assert.Null(deserialized.Error);
    }

    [Fact]
    public void SessionTurn_Initiator_RoundTrip()
    {
        var turn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = "thread_x",
            Status = TurnStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            OriginChannel = "qq",
            Initiator = new TurnInitiatorContext
            {
                ChannelName = "wecom",
                UserId = "user-123",
                UserName = "Akihiko",
                UserRole = "admin",
                ChannelContext = "chat:abc",
                GroupId = "chat:abc"
            }
        };

        var json = JsonSerializer.Serialize(turn, Opts);
        var deserialized = JsonSerializer.Deserialize<SessionTurn>(json, Opts);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Initiator);
        Assert.Equal("wecom", deserialized.Initiator!.ChannelName);
        Assert.Equal("user-123", deserialized.Initiator.UserId);
        Assert.Equal("admin", deserialized.Initiator.UserRole);
        Assert.Equal("chat:abc", deserialized.Initiator.ChannelContext);
    }

    // -------------------------------------------------------------------------
    // SessionThread round-trips
    // -------------------------------------------------------------------------

    [Fact]
    public void SessionThread_RoundTrip_FullObject()
    {
        var thread = new SessionThread
        {
            Id = "thread_20260315_a3f2k9",
            WorkspacePath = "/path/to/workspace",
            UserId = "user123",
            OriginChannel = "qq",
            DisplayName = "Help me fix the login bug",
            Status = ThreadStatus.Active,
            CreatedAt = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero),
            LastActiveAt = new DateTimeOffset(2026, 3, 15, 10, 5, 0, TimeSpan.Zero),
            HistoryMode = HistoryMode.Server,
            Metadata = new Dictionary<string, string>
            {
                ["customKey"] = "qq_12345_67890",
                ["qqGroupId"] = "12345"
            }
        };

        var json = JsonSerializer.Serialize(thread, Opts);
        var deserialized = JsonSerializer.Deserialize<SessionThread>(json, Opts);

        Assert.NotNull(deserialized);
        Assert.Equal(thread.Id, deserialized.Id);
        Assert.Equal(thread.WorkspacePath, deserialized.WorkspacePath);
        Assert.Equal(thread.UserId, deserialized.UserId);
        Assert.Equal(thread.OriginChannel, deserialized.OriginChannel);
        Assert.Equal(thread.DisplayName, deserialized.DisplayName);
        Assert.Equal(ThreadStatus.Active, deserialized.Status);
        Assert.Equal(HistoryMode.Server, deserialized.HistoryMode);
        Assert.Equal(2, deserialized.Metadata.Count);
        Assert.Equal("qq_12345_67890", deserialized.Metadata["customKey"]);
        Assert.Empty(deserialized.Turns);
    }

    [Fact]
    public void SessionThread_NullableFields_RoundTrip()
    {
        var thread = new SessionThread
        {
            Id = "thread_20260315_xyz",
            WorkspacePath = "/workspace",
            OriginChannel = "cli",
            Status = ThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow
            // UserId, DisplayName, Configuration are null
        };

        var json = JsonSerializer.Serialize(thread, Opts);
        var deserialized = JsonSerializer.Deserialize<SessionThread>(json, Opts);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.UserId);
        Assert.Null(deserialized.DisplayName);
        Assert.Null(deserialized.Configuration);
    }

    // -------------------------------------------------------------------------
    // SessionEvent round-trips
    // -------------------------------------------------------------------------

    [Fact]
    public void SessionEvent_TurnStarted_RoundTrip()
    {
        var turn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = "thread_abc",
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };

        var evt = new SessionEvent
        {
            EventId = "evt_001",
            EventType = SessionEventType.TurnStarted,
            ThreadId = "thread_abc",
            TurnId = "turn_001",
            Timestamp = DateTimeOffset.UtcNow
        };
        evt.Payload = turn;

        var json = JsonSerializer.Serialize(evt, Opts);
        var deserialized = JsonSerializer.Deserialize<SessionEvent>(json, Opts);

        Assert.NotNull(deserialized);
        Assert.Equal("evt_001", deserialized.EventId);
        Assert.Equal(SessionEventType.TurnStarted, deserialized.EventType);
        Assert.Equal("thread_abc", deserialized.ThreadId);
        Assert.Equal("turn_001", deserialized.TurnId);
        Assert.Null(deserialized.ItemId);

        var turnPayload = deserialized.TurnPayload;
        Assert.NotNull(turnPayload);
        Assert.Equal("turn_001", turnPayload.Id);
    }

    [Fact]
    public void SessionEvent_ItemDelta_RoundTrip()
    {
        var evt = new SessionEvent
        {
            EventId = "evt_005",
            EventType = SessionEventType.ItemDelta,
            ThreadId = "thread_abc",
            TurnId = "turn_001",
            ItemId = "item_002",
            Timestamp = DateTimeOffset.UtcNow
        };
        evt.Payload = new AgentMessageDelta { TextDelta = "Hello " };

        var json = JsonSerializer.Serialize(evt, Opts);
        var deserialized = JsonSerializer.Deserialize<SessionEvent>(json, Opts);

        Assert.NotNull(deserialized);
        Assert.Equal(SessionEventType.ItemDelta, deserialized.EventType);
        var delta = deserialized.DeltaPayload;
        Assert.NotNull(delta);
        Assert.Equal("Hello ", delta.TextDelta);
    }

    [Fact]
    public void SessionEvent_ThreadStatusChanged_RoundTrip()
    {
        var evt = new SessionEvent
        {
            EventId = "evt_010",
            EventType = SessionEventType.ThreadStatusChanged,
            ThreadId = "thread_abc",
            Timestamp = DateTimeOffset.UtcNow
        };
        evt.Payload = new ThreadStatusChangedPayload
        {
            PreviousStatus = ThreadStatus.Active,
            NewStatus = ThreadStatus.Paused
        };

        var json = JsonSerializer.Serialize(evt, Opts);
        var deserialized = JsonSerializer.Deserialize<SessionEvent>(json, Opts);

        Assert.NotNull(deserialized);
        var payload = deserialized.StatusChangedPayload;
        Assert.NotNull(payload);
        Assert.Equal(ThreadStatus.Active, payload.PreviousStatus);
        Assert.Equal(ThreadStatus.Paused, payload.NewStatus);
    }

    [Fact]
    public void NewThreadId_HasCorrectFormat()
    {
        var id = SessionIdGenerator.NewThreadId();
        // e.g. "thread_20260315_a3f2k9"
        Assert.StartsWith("thread_", id);
        var parts = id.Split('_');
        Assert.Equal(3, parts.Length);
        Assert.Equal(8, parts[1].Length); // yyyyMMdd
        Assert.Equal(6, parts[2].Length);
    }

    [Fact]
    public void NewThreadId_IsUnique()
    {
        var ids = Enumerable.Range(0, 100).Select(_ => SessionIdGenerator.NewThreadId()).ToHashSet();
        Assert.Equal(100, ids.Count);
    }

    // -------------------------------------------------------------------------
    // TokenUsageInfo
    // -------------------------------------------------------------------------

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
    public void TokenUsageInfo_RoundTrip()
    {
        var usage = new TokenUsageInfo { InputTokens = 1200, OutputTokens = 800, CachedInputTokens = 300, CacheWriteInputTokens = 100, LlmCallCount = 2, TotalTokens = 2000 };
        var json = JsonSerializer.Serialize(usage, Opts);
        var deserialized = JsonSerializer.Deserialize<TokenUsageInfo>(json, Opts);
        Assert.NotNull(deserialized);
        Assert.Equal(usage.InputTokens, deserialized.InputTokens);
        Assert.Equal(usage.OutputTokens, deserialized.OutputTokens);
        Assert.Equal(usage.CachedInputTokens, deserialized.CachedInputTokens);
        Assert.Equal(usage.CacheWriteInputTokens, deserialized.CacheWriteInputTokens);
        Assert.Equal(usage.LlmCallCount, deserialized.LlmCallCount);
        Assert.Equal(usage.FreshInputTokens, deserialized.FreshInputTokens);
        Assert.Equal(usage.TotalTokens, deserialized.TotalTokens);
    }

    // -------------------------------------------------------------------------
    // ThreadSummary.FromThread
    // -------------------------------------------------------------------------

    [Fact]
    public void ThreadSummary_FromThread_CopiesFields()
    {
        var thread = new SessionThread
        {
            Id = "thread_20260315_abc",
            UserId = "u1",
            OriginChannel = "qq",
            DisplayName = "Test",
            Status = ThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string> { ["k"] = "v" }
        };
        thread.Turns.Add(new SessionTurn { Id = "turn_001", ThreadId = thread.Id, Status = TurnStatus.Completed, StartedAt = DateTimeOffset.UtcNow });
        thread.Turns.Add(new SessionTurn { Id = "turn_002", ThreadId = thread.Id, Status = TurnStatus.Completed, StartedAt = DateTimeOffset.UtcNow });

        var summary = ThreadSummary.FromThread(thread);

        Assert.Equal(thread.Id, summary.Id);
        Assert.Equal(thread.UserId, summary.UserId);
        Assert.Equal(thread.OriginChannel, summary.OriginChannel);
        Assert.Equal(thread.DisplayName, summary.DisplayName);
        Assert.Equal(ThreadStatus.Active, summary.Status);
        Assert.Equal(2, summary.TurnCount);
        Assert.Equal("v", summary.Metadata["k"]);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static SessionItem BuildItem(ItemType type, ItemStatus status, object? payload)
    {
        var item = new SessionItem
        {
            Id = "item_001",
            TurnId = "turn_001",
            Type = type,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow
        };
        item.Payload = payload;
        return item;
    }

    private static SessionItem RoundTrip(SessionItem item)
    {
        var json = JsonSerializer.Serialize(item, Opts);
        var result = JsonSerializer.Deserialize<SessionItem>(json, Opts);
        Assert.NotNull(result);
        return result;
    }

    // -------------------------------------------------------------------------
    // SessionTurn.Input deserialization — regression guard for /load history display
    // -------------------------------------------------------------------------

    [Fact]
    public void SessionTurn_Input_UserMessagePayload_SurvivesRoundTrip()
    {
        var userItem = new SessionItem
        {
            Id = "item_001",
            TurnId = "turn_001",
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = new DateTimeOffset(2026, 3, 15, 7, 32, 21, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2026, 3, 15, 7, 32, 21, TimeSpan.Zero),
            Payload = new UserMessagePayload { Text = "你好" }
        };

        var turn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = "thread_20260315_lbdp2i",
            Status = TurnStatus.Completed,
            StartedAt = new DateTimeOffset(2026, 3, 15, 7, 32, 21, TimeSpan.Zero),
            Input = userItem,
            Items = [userItem]
        };

        var thread = new SessionThread
        {
            Id = "thread_20260315_lbdp2i",
            WorkspacePath = "/workspace",
            OriginChannel = "cli",
            UserId = "local",
            Status = ThreadStatus.Active,
            CreatedAt = turn.StartedAt,
            LastActiveAt = turn.StartedAt,
            Turns = [turn]
        };

        var json = JsonSerializer.Serialize(thread, Opts);
        var loaded = JsonSerializer.Deserialize<SessionThread>(json, Opts);

        Assert.NotNull(loaded);
        Assert.Single(loaded.Turns);

        var loadedTurn = loaded.Turns[0];

        // Verify turn.Input is deserialized with correct type
        Assert.NotNull(loadedTurn.Input);
        Assert.Equal(ItemType.UserMessage, loadedTurn.Input.Type);
        var inputPayload = loadedTurn.Input.Payload as UserMessagePayload;
        Assert.NotNull(inputPayload);
        Assert.Equal("你好", inputPayload.Text);

        // Verify turn.Items[0] also has correct payload
        Assert.Single(loadedTurn.Items);
        var firstItemPayload = loadedTurn.Items[0].Payload as UserMessagePayload;
        Assert.NotNull(firstItemPayload);
        Assert.Equal("你好", firstItemPayload.Text);
    }

    [Fact]
    public void SessionThread_WithMultipleTurns_Input_UserMessagePayload_SurvivesRoundTrip()
    {
        static SessionItem MakeUserItem(string turnId, string text) => new()
        {
            Id = "item_001",
            TurnId = turnId,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new UserMessagePayload { Text = text }
        };

        static SessionItem MakeAgentItem(string turnId, string text) => new()
        {
            Id = "item_002",
            TurnId = turnId,
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new AgentMessagePayload { Text = text }
        };

        var turn1User = MakeUserItem("turn_001", "你好");
        var turn2User = MakeUserItem("turn_002", "你有哪些工具呢");

        var thread = new SessionThread
        {
            Id = "thread_20260315_test",
            WorkspacePath = "/workspace",
            OriginChannel = "cli",
            UserId = "local",
            Status = ThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            Turns =
            [
                new SessionTurn
                {
                    Id = "turn_001",
                    ThreadId = "thread_20260315_test",
                    Status = TurnStatus.Completed,
                    StartedAt = DateTimeOffset.UtcNow,
                    Input = turn1User,
                    Items = [turn1User, MakeAgentItem("turn_001", "你好！我是DotCraft")]
                },
                new SessionTurn
                {
                    Id = "turn_002",
                    ThreadId = "thread_20260315_test",
                    Status = TurnStatus.Completed,
                    StartedAt = DateTimeOffset.UtcNow,
                    Input = turn2User,
                    Items = [turn2User, MakeAgentItem("turn_002", "我有以下工具...")]
                }
            ]
        };

        var json = JsonSerializer.Serialize(thread, Opts);
        var loaded = JsonSerializer.Deserialize<SessionThread>(json, Opts);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Turns.Count);

        for (var i = 0; i < loaded.Turns.Count; i++)
        {
            var t = loaded.Turns[i];
            Assert.NotNull(t.Input);
            Assert.Equal(ItemType.UserMessage, t.Input.Type);
            var payload = t.Input.Payload as UserMessagePayload;
            Assert.NotNull(payload);
        }

        Assert.Equal("你好", (loaded.Turns[0].Input!.Payload as UserMessagePayload)!.Text);
        Assert.Equal("你有哪些工具呢", (loaded.Turns[1].Input!.Payload as UserMessagePayload)!.Text);
    }

    [Fact]
    public void SessionWireEvent_SerializesEnumsAsCamelCase()
    {
        var evt = new SessionEvent
        {
            EventId = "evt_0001",
            EventType = SessionEventType.TurnCompleted,
            ThreadId = "thread_001",
            TurnId = "turn_001",
            Timestamp = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
            Payload = new SessionTurn
            {
                Id = "turn_001",
                ThreadId = "thread_001",
                Status = TurnStatus.Completed,
                OriginChannel = "cli",
                StartedAt = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
                CompletedAt = new DateTimeOffset(2026, 3, 16, 10, 1, 0, TimeSpan.Zero)
            }
        };

        var json = JsonSerializer.Serialize(evt.ToWire(), SessionWireJsonOptions.Default);

        Assert.Contains("\"eventType\":\"turnCompleted\"", json, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"completed\"", json, StringComparison.Ordinal);
        Assert.Contains("\"payloadKind\":\"turn\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionWireEvent_ReasoningDelta_ProducesFlatDeltaShape()
    {
        var evt = new SessionEvent
        {
            EventId = "evt_0002",
            EventType = SessionEventType.ItemDelta,
            ThreadId = "thread_001",
            TurnId = "turn_001",
            ItemId = "item_001",
            Timestamp = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
            Payload = new ReasoningContentDelta { TextDelta = "thinking..." }
        };

        var json = JsonSerializer.Serialize(evt.ToWire(), SessionWireJsonOptions.Default);

        // payloadKind still identifies the internal type; the payload itself is { delta }
        Assert.Contains("\"payloadKind\":\"reasoningContentDelta\"", json, StringComparison.Ordinal);
        Assert.Contains("\"delta\":\"thinking...\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"deltaKind\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"textDelta\"", json, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    // Persistence round-trips for new event payload types
    // -------------------------------------------------------------------------

    [Fact]
    public void SessionEvent_ThreadResumed_RoundTrip()
    {
        var thread = new SessionThread
        {
            Id = "thread_001",
            WorkspacePath = "/workspace",
            OriginChannel = "cli",
            Status = ThreadStatus.Active,
            CreatedAt = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
            LastActiveAt = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero)
        };

        var evt = new SessionEvent
        {
            EventId = "evt_r1",
            EventType = SessionEventType.ThreadResumed,
            ThreadId = "thread_001",
            Timestamp = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
            Payload = new ThreadResumedPayload { Thread = thread, ResumedBy = "vscode" }
        };

        var json = JsonSerializer.Serialize(evt, Opts);
        var deserialized = JsonSerializer.Deserialize<SessionEvent>(json, Opts);

        Assert.NotNull(deserialized);
        Assert.Equal(SessionEventType.ThreadResumed, deserialized.EventType);

        var payload = deserialized.ResumedPayload;
        Assert.NotNull(payload);
        Assert.Equal("thread_001", payload.Thread.Id);
        Assert.Equal("vscode", payload.ResumedBy);

        // TurnPayload accessor should not return anything for thread-level events
        Assert.Null(deserialized.TurnPayload);
    }

    [Fact]
    public void SessionEvent_TurnCancelled_RoundTrip()
    {
        var turn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = "thread_001",
            Status = TurnStatus.Cancelled,
            StartedAt = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero)
        };

        var evt = new SessionEvent
        {
            EventId = "evt_c1",
            EventType = SessionEventType.TurnCancelled,
            ThreadId = "thread_001",
            TurnId = "turn_001",
            Timestamp = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
            Payload = new TurnCancelledPayload { Turn = turn, Reason = "Caller cancelled" }
        };

        var json = JsonSerializer.Serialize(evt, Opts);
        var deserialized = JsonSerializer.Deserialize<SessionEvent>(json, Opts);

        Assert.NotNull(deserialized);
        Assert.Equal(SessionEventType.TurnCancelled, deserialized.EventType);

        var payload = deserialized.TurnCancelledPayload;
        Assert.NotNull(payload);
        Assert.Equal("turn_001", payload.Turn.Id);
        Assert.Equal("Caller cancelled", payload.Reason);

        // TurnPayload accessor should unwrap the nested turn
        Assert.Equal("turn_001", deserialized.TurnPayload?.Id);
    }

    [Fact]
    public void SessionEvent_TurnFailed_RoundTrip()
    {
        var turn = new SessionTurn
        {
            Id = "turn_002",
            ThreadId = "thread_001",
            Status = TurnStatus.Failed,
            StartedAt = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
            Error = "Model context exceeded"
        };

        var evt = new SessionEvent
        {
            EventId = "evt_f1",
            EventType = SessionEventType.TurnFailed,
            ThreadId = "thread_001",
            TurnId = "turn_002",
            Timestamp = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
            Payload = new TurnFailedPayload { Turn = turn, Error = "Model context exceeded" }
        };

        var json = JsonSerializer.Serialize(evt, Opts);
        var deserialized = JsonSerializer.Deserialize<SessionEvent>(json, Opts);

        Assert.NotNull(deserialized);
        Assert.Equal(SessionEventType.TurnFailed, deserialized.EventType);

        var payload = deserialized.TurnFailedPayload;
        Assert.NotNull(payload);
        Assert.Equal("turn_002", payload.Turn.Id);
        Assert.Equal("Model context exceeded", payload.Error);

        // TurnPayload accessor should unwrap the nested turn
        Assert.Equal("turn_002", deserialized.TurnPayload?.Id);
        // Error is accessible via the accessor used by SessionEventHandler
        Assert.Equal("Model context exceeded", deserialized.TurnPayload?.Error);
    }

    // -------------------------------------------------------------------------
    // WireApprovalDecisionConverter
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(SessionApprovalDecision.AcceptOnce, "accept")]
    [InlineData(SessionApprovalDecision.AcceptForSession, "acceptForSession")]
    [InlineData(SessionApprovalDecision.AcceptAlways, "acceptAlways")]
    [InlineData(SessionApprovalDecision.Reject, "decline")]
    [InlineData(SessionApprovalDecision.CancelTurn, "cancel")]
    public void WireApprovalDecision_SerializesAsWireNames(SessionApprovalDecision decision, string expectedWire)
    {
        var json = JsonSerializer.Serialize(decision, SessionWireJsonOptions.Default);
        Assert.Equal($"\"{expectedWire}\"", json);
    }

    [Theory]
    [InlineData("accept", SessionApprovalDecision.AcceptOnce)]
    [InlineData("acceptForSession", SessionApprovalDecision.AcceptForSession)]
    [InlineData("acceptAlways", SessionApprovalDecision.AcceptAlways)]
    [InlineData("decline", SessionApprovalDecision.Reject)]
    [InlineData("cancel", SessionApprovalDecision.CancelTurn)]
    public void WireApprovalDecision_DeserializesFromWireNames(string wireValue, SessionApprovalDecision expected)
    {
        var deserialized = JsonSerializer.Deserialize<SessionApprovalDecision>($"\"{wireValue}\"", SessionWireJsonOptions.Default);
        Assert.Equal(expected, deserialized);
    }

    // -------------------------------------------------------------------------
    // ToWireMethodName
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(SessionEventType.ThreadCreated, "thread/started")]
    [InlineData(SessionEventType.ThreadResumed, "thread/resumed")]
    [InlineData(SessionEventType.ThreadStatusChanged, "thread/statusChanged")]
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
    public void ToWireMethodName_ItemDelta_AgentMessage()
    {
        var evt = new SessionEvent
        {
            EventType = SessionEventType.ItemDelta,
            Payload = new AgentMessageDelta { TextDelta = "hello" }
        };
        Assert.Equal("item/agentMessage/delta", evt.ToWireMethodName());
    }

    [Fact]
    public void ToWireMethodName_ItemDelta_Reasoning()
    {
        var evt = new SessionEvent
        {
            EventType = SessionEventType.ItemDelta,
            Payload = new ReasoningContentDelta { TextDelta = "thinking" }
        };
        Assert.Equal("item/reasoning/delta", evt.ToWireMethodName());
    }

    // -------------------------------------------------------------------------
    // SessionWireInputPart <-> AIContent mapping
    // -------------------------------------------------------------------------

    [Fact]
    public void SessionWireInputPart_Text_ToAIContent()
    {
        var part = new SessionWireInputPart { Type = "text", Text = "Hello world" };
        var content = part.ToAIContent();
        var tc = Assert.IsType<TextContent>(content);
        Assert.Equal("Hello world", tc.Text);
    }

    [Fact]
    public void SessionWireInputPart_Image_ToAIContent_ReturnsPlaceholderText()
    {
        // DataContent requires data: URIs, so image URLs become TextContent placeholders.
        // The AppServer is responsible for fetching image bytes and creating proper DataContent.
        var part = new SessionWireInputPart { Type = "image", Url = "https://example.com/img.png" };
        var content = part.ToAIContent();
        var tc = Assert.IsType<TextContent>(content);
        Assert.Contains("https://example.com/img.png", tc.Text);
    }

    [Fact]
    public void SessionWireInputPart_LocalImage_ToAIContent_ReturnsPlaceholderText()
    {
        // DataContent requires data: URIs, so local paths become TextContent placeholders.
        // The AppServer reads the file and constructs DataContent(bytes, mediaType) before dispatch.
        var part = new SessionWireInputPart { Type = "localImage", Path = "/tmp/screenshot.png" };
        var content = part.ToAIContent();
        var tc = Assert.IsType<TextContent>(content);
        Assert.Contains("/tmp/screenshot.png", tc.Text);
    }

    [Fact]
    public void AIContent_Text_ToWireInputPart()
    {
        var tc = new TextContent("Hello");
        var part = tc.ToWireInputPart();
        Assert.Equal("text", part.Type);
        Assert.Equal("Hello", part.Text);
    }

    [Fact]
    public void AIContent_DataContent_ToWireInputPart()
    {
        // DataContent with a data: URI (base64 inline) maps to the "image" wire type.
        const string dataUri = "data:image/png;base64,iVBORw0KGgo=";
        var dc = new DataContent(dataUri, "image/png");
        var part = dc.ToWireInputPart();
        Assert.Equal("image", part.Type);
        Assert.Equal(dataUri, part.Url);
    }

    // -------------------------------------------------------------------------
    // ToWire(includeTurns) populates Turns and nested Items
    // -------------------------------------------------------------------------

    [Fact]
    public void SessionWireThread_ToWire_WithoutTurns_OmitsTurns()
    {
        var thread = BuildThreadWithTurns();
        var wire = thread.ToWire(includeTurns: false);
        Assert.Null(wire.Turns);
    }

    [Fact]
    public void SessionWireThread_ToWire_WithTurns_PopulatesTurnsAndItems()
    {
        var thread = BuildThreadWithTurns();
        var wire = thread.ToWire(includeTurns: true);

        Assert.NotNull(wire.Turns);
        Assert.Single(wire.Turns);

        var wireTurn = wire.Turns[0];
        Assert.Equal("turn_001", wireTurn.Id);
        Assert.NotNull(wireTurn.Items);
        Assert.Equal(2, wireTurn.Items!.Count);
        Assert.Equal("item_001", wireTurn.Items[0].Id);
        Assert.Equal("item_002", wireTurn.Items[1].Id);
    }

    // -------------------------------------------------------------------------
    // Wire shapes for delta and statusChanged events
    // -------------------------------------------------------------------------

    [Fact]
    public void SessionWireEvent_AgentMessageDelta_ProducesFlatDeltaShape()
    {
        var evt = new SessionEvent
        {
            EventId = "evt_d1",
            EventType = SessionEventType.ItemDelta,
            ThreadId = "thread_001",
            TurnId = "turn_001",
            ItemId = "item_003",
            Timestamp = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
            Payload = new AgentMessageDelta { TextDelta = "hello world" }
        };

        var json = JsonSerializer.Serialize(evt.ToWire(), SessionWireJsonOptions.Default);

        Assert.Contains("\"delta\":\"hello world\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"textDelta\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"deltaKind\"", json, StringComparison.Ordinal);
        Assert.Contains("\"payloadKind\":\"agentMessageDelta\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionWireEvent_ThreadStatusChanged_IncludesThreadId()
    {
        var evt = new SessionEvent
        {
            EventId = "evt_s1",
            EventType = SessionEventType.ThreadStatusChanged,
            ThreadId = "thread_001",
            Timestamp = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
            Payload = new ThreadStatusChangedPayload
            {
                PreviousStatus = ThreadStatus.Active,
                NewStatus = ThreadStatus.Paused
            }
        };

        var json = JsonSerializer.Serialize(evt.ToWire(), SessionWireJsonOptions.Default);

        Assert.Contains("\"threadId\":\"thread_001\"", json, StringComparison.Ordinal);
        Assert.Contains("\"previousStatus\":\"active\"", json, StringComparison.Ordinal);
        Assert.Contains("\"newStatus\":\"paused\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionWireEvent_TurnFailed_ProducesTurnAndError()
    {
        var turn = new SessionTurn
        {
            Id = "turn_002",
            ThreadId = "thread_001",
            Status = TurnStatus.Failed,
            StartedAt = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero)
        };

        var evt = new SessionEvent
        {
            EventId = "evt_f2",
            EventType = SessionEventType.TurnFailed,
            ThreadId = "thread_001",
            TurnId = "turn_002",
            Timestamp = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
            Payload = new TurnFailedPayload { Turn = turn, Error = "Context window exceeded" }
        };

        var json = JsonSerializer.Serialize(evt.ToWire(), SessionWireJsonOptions.Default);

        Assert.Contains("\"error\":\"Context window exceeded\"", json, StringComparison.Ordinal);
        Assert.Contains("\"turn\":", json, StringComparison.Ordinal);
        Assert.Contains("\"payloadKind\":\"turnFailed\"", json, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    // Additional helpers
    // -------------------------------------------------------------------------

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
