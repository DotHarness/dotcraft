using System.Text.Json;
using Microsoft.Extensions.AI;
using DotCraft.AppServer;
using DotCraft.Sessions;
using QueuedTurnInput = DotCraft.Sessions.QueuedTurnInput;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Tests for turn/* methods (spec Section 5) and delta notification correctness.
/// Validates:
/// - turn/start response shape and inline response ordering
/// - deltaKind field in item/*/delta notifications (Fix 1)
/// - streamingSupport=false suppresses item deltas (Fix 4)
/// - messages forwarded to SubmitInputAsync for historyMode=client (Fix 5)
/// - turn/interrupt triggers CancelTurnAsync
/// </summary>
public sealed class AppServerTurnTests : IDisposable
{
    private readonly AppServerTestHarness _h = new();

    public AppServerTurnTests()
    {
        _h.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _h.Dispose();

    // -------------------------------------------------------------------------
    // turn/start — basic flow
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TurnStart_SendsResponseBeforeNotifications()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.EnqueueSubmitEvents(thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await _h.ExecuteRequestAsync(msg);

        // First message must be the turn/start response
        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.True(response.RootElement.GetProperty("result").TryGetProperty("turn", out _),
            "turn/start result must contain a 'turn' field");
    }

    [Fact]
    public async Task TurnStart_ResponseBeforeTurnStartedNotification_Ordering()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.EnqueueSubmitEvents(thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await _h.ExecuteRequestAsync(msg);

        var first = await _h.Transport.ReadNextSentAsync();
        var second = await _h.Transport.ReadNextSentAsync();

        // First: response (has 'result' and 'id'), Second: turn/started notification (has 'method')
        Assert.True(first.RootElement.TryGetProperty("result", out _),
            "First message must be the JSON-RPC response");
        Assert.True(second.RootElement.TryGetProperty("method", out var methodEl));
        Assert.Equal(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStarted, methodEl.GetString());
    }

    [Fact]
    public async Task TurnStart_FullEventSequence_AllNotificationsArriveInOrder()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.EnqueueSubmitEvents(thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await _h.ExecuteRequestAsync(msg);

        // Expected: response, turn/started, item/started, item/agentMessage/delta, item/completed, turn/completed
        var all = await _h.Transport.WaitAndDrainAsync(6, TimeSpan.FromSeconds(10));

        Assert.True(all[0].RootElement.TryGetProperty("result", out _)); // response
        AssertMethod(all[1], DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStarted);
        AssertMethod(all[2], DotCraft.Protocol.AppServer.AppServerMethodNames.ItemStarted);
        AssertMethod(all[3], DotCraft.Protocol.AppServer.AppServerMethodNames.AgentMessageDelta);
        AssertMethod(all[4], DotCraft.Protocol.AppServer.AppServerMethodNames.ItemCompleted);
        AssertMethod(all[5], DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCompleted);
    }

    // -------------------------------------------------------------------------
    // Fix 1: deltaKind must be present in delta notifications (spec Section 2.3)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TurnStart_AgentMessageDelta_IncludesDeltaKind()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.EnqueueSubmitEvents(thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await _h.ExecuteRequestAsync(msg);

        var all = await _h.Transport.WaitAndDrainAsync(6, TimeSpan.FromSeconds(10));

        // all[3] is item/agentMessage/delta
        var deltaNotif = all[3];
        var @params = deltaNotif.RootElement.GetProperty("params");
        Assert.True(@params.TryGetProperty("deltaKind", out var deltaKindEl),
            "Delta notification must include 'deltaKind' field (spec Section 2.3)");
        Assert.Equal("agentMessage", deltaKindEl.GetString());
        Assert.True(@params.TryGetProperty("delta", out _), "Delta notification must include 'delta' field");
    }

    [Fact]
    public async Task TurnStart_ReasoningDelta_IncludesDeltaKindReasoningContent()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        var turn = AppServerTestHarness.MakeTurn(thread.Id);

        // Build event sequence with a reasoning delta instead of agentMessage delta
        var events = new SessionEvent[]
        {
            new() {
                EventId = "e1", EventType = SessionEventType.TurnStarted,
                ThreadId = thread.Id, TurnId = turn.Id, Timestamp = DateTimeOffset.UtcNow, Payload = turn
            },
            new() {
                EventId = "e2", EventType = SessionEventType.ItemDelta,
                ThreadId = thread.Id, TurnId = turn.Id, ItemId = "item_001",
                Timestamp = DateTimeOffset.UtcNow,
                Payload = new ReasoningContentDelta { TextDelta = "thinking..." }
            },
            new() {
                EventId = "e3", EventType = SessionEventType.TurnCompleted,
                ThreadId = thread.Id, TurnId = turn.Id, Timestamp = DateTimeOffset.UtcNow,
                Payload = AppServerTestHarness.MakeCompletedTurn(thread.Id)
            }
        };
        _h.Service.EnqueueSubmitEvents(thread.Id, events);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Think about it" } }
        });
        await _h.ExecuteRequestAsync(msg);

        // response, turn/started, item/reasoning/delta, turn/completed
        var all = await _h.Transport.WaitAndDrainAsync(4, TimeSpan.FromSeconds(10));

        var reasoningDelta = all[2];
        AssertMethod(reasoningDelta, DotCraft.Protocol.AppServer.AppServerMethodNames.ReasoningDelta);
        var @params = reasoningDelta.RootElement.GetProperty("params");
        Assert.Equal("reasoningContent", @params.GetProperty("deltaKind").GetString());
    }

    // -------------------------------------------------------------------------
    // Fix 4: streamingSupport=false suppresses delta notifications
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TurnStart_StreamingDisabled_DeltasAreSuppressed()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(streamingSupport: false);

        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await harness.ExecuteRequestAsync(msg);

        // With streamingSupport=false, deltas are suppressed.
        // Expected: response, turn/started, item/started, item/completed, turn/completed (5, not 6)
        var all = await harness.Transport.WaitAndDrainAsync(5, TimeSpan.FromSeconds(10));

        var methods = all
            .Skip(1) // skip the response
            .Select(d => d.RootElement.TryGetProperty("method", out var m) ? m.GetString() : null)
            .ToList();

        Assert.DoesNotContain(DotCraft.Protocol.AppServer.AppServerMethodNames.AgentMessageDelta, methods);
        Assert.DoesNotContain(DotCraft.Protocol.AppServer.AppServerMethodNames.ReasoningDelta, methods);
        Assert.Contains(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCompleted, methods);
    }

    [Fact]
    public async Task TurnStart_LocalImageMetadata_IsAttachedToDataContent()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.EnqueueSubmitEvents(thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var localImagePath = Path.Combine(_h.Identity.WorkspacePath, ".craft", "attachments", "images", "test.png");
        Directory.CreateDirectory(Path.GetDirectoryName(localImagePath)!);
        await File.WriteAllBytesAsync(localImagePath, [0x89, 0x50, 0x4E, 0x47]);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[]
            {
                new
                {
                    type = "localImage",
                    path = localImagePath,
                    mimeType = "image/png",
                    fileName = "test.png"
                }
            }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        var dataContent = Assert.IsType<DataContent>(_h.Service.LastSubmittedContent.Single());
        Assert.NotNull(dataContent.AdditionalProperties);
        Assert.Equal(localImagePath, dataContent.AdditionalProperties!["localImage.path"]?.ToString());
        Assert.Equal("image/png", dataContent.AdditionalProperties!["localImage.mimeType"]?.ToString());
        Assert.Equal("test.png", dataContent.AdditionalProperties!["localImage.fileName"]?.ToString());
    }

    [Fact]
    public async Task TurnStart_InlineImageDataUrl_IsDecodedWithoutNetworkAccess()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.EnqueueSubmitEvents(thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "image", url = "data:image/png;base64,AQID" } }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var dataContent = Assert.IsType<DataContent>(_h.Service.LastSubmittedContent.Single());
        Assert.Equal("image/png", dataContent.MediaType);
        Assert.Equal([1, 2, 3], dataContent.Data.ToArray());
    }

    [Theory]
    [InlineData("http://example.com/image.png")]
    [InlineData("HTTPS://example.com/image.png")]
    public async Task TurnStart_RemoteImageUrl_ReturnsInvalidParams(string url)
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "image", url } }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
        Assert.Equal(
            SessionInputPartResolver.RemoteImageUrlError,
            response.RootElement.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(
            SessionInputPartResolver.RemoteImageUrlErrorCode,
            response.RootElement.GetProperty("error").GetProperty("data").GetProperty("code").GetString());
        Assert.Empty(thread.QueuedInputs);
    }

    [Theory]
    [InlineData("data:text/plain;base64,SGVsbG8=")]
    [InlineData("data:image/png,AAAA")]
    [InlineData("data:image/png;base64,%%%")]
    [InlineData("ftp://example.com/image.png")]
    [InlineData("http://example.com/image.png")]
    [InlineData("HTTPS://example.com/image.png")]
    public async Task TurnEnqueue_InvalidInlineImage_ReturnsInvalidParamsWithoutPersisting(string url)
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnEnqueue, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "image", url } }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
        Assert.Empty(thread.QueuedInputs);
    }

    [Fact]
    public async Task TurnEnqueue_InlineImageDataUrl_PersistsValidatedSnapshot()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        const string dataUrl = "data:image/jpeg;base64,AQID";
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnEnqueue, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "image", url = dataUrl } }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var queued = Assert.Single(thread.QueuedInputs);
        Assert.Equal(dataUrl, Assert.Single(queued.MaterializedInputParts).Url);
    }

    [Fact]
    public async Task TurnStart_ToolCallArgumentsDelta_EmitsNotificationWithExpectedShape()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.EnqueueSubmitEvents(
            thread.Id,
            AppServerTestHarness.BuildStreamingToolCallEventSequence(thread.Id));

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Write a file" } }
        });
        await _h.ExecuteRequestAsync(msg);

        var all = await _h.Transport.WaitAndDrainAsync(7, TimeSpan.FromSeconds(10));
        AssertMethod(all[3], DotCraft.Protocol.AppServer.AppServerMethodNames.ToolArgumentsDelta);
        var @params = all[3].RootElement.GetProperty("params");
        Assert.Equal("toolCallArguments", @params.GetProperty("deltaKind").GetString());
        Assert.Equal("WriteFile", @params.GetProperty("toolName").GetString());
        Assert.Equal("call_001", @params.GetProperty("callId").GetString());
        Assert.True(@params.GetProperty("delta").GetString()?.Length > 0);
    }

    [Fact]
    public async Task TurnStart_ToolCallArgumentsDelta_StreamingDisabled_IsSuppressed()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(streamingSupport: false);
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(
            thread.Id,
            AppServerTestHarness.BuildStreamingToolCallEventSequence(thread.Id));

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Write a file" } }
        });
        await harness.ExecuteRequestAsync(msg);

        var all = await harness.Transport.WaitAndDrainAsync(5, TimeSpan.FromSeconds(10));
        var methods = all
            .Skip(1)
            .Select(d => d.RootElement.TryGetProperty("method", out var m) ? m.GetString() : null)
            .ToList();
        Assert.DoesNotContain(DotCraft.Protocol.AppServer.AppServerMethodNames.ToolArgumentsDelta, methods);
    }

    [Fact]
    public async Task TurnStart_ToolCallArgumentsDelta_NotificationOptOut_IsSuppressed()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(optOutMethods: [DotCraft.Protocol.AppServer.AppServerMethodNames.ToolArgumentsDelta]);
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(
            thread.Id,
            AppServerTestHarness.BuildStreamingToolCallEventSequence(thread.Id));

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Write a file" } }
        });
        await harness.ExecuteRequestAsync(msg);

        var all = await harness.Transport.WaitAndDrainAsync(5, TimeSpan.FromSeconds(10));
        var methods = all
            .Skip(1)
            .Select(d => d.RootElement.TryGetProperty("method", out var m) ? m.GetString() : null)
            .ToList();
        Assert.DoesNotContain(DotCraft.Protocol.AppServer.AppServerMethodNames.ToolArgumentsDelta, methods);
    }

    // -------------------------------------------------------------------------
    // Fix 5: messages field forwarded to SubmitInputAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TurnStart_WithMessages_DoesNotReturnError()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.EnqueueSubmitEvents(thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        // historyMode=client thread providing conversation history
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } },
            messages = new[]
            {
                new { role = "user", content = new[] { new { type = "text", text = "Previous message" } } }
            }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
    }

    [Fact]
    public async Task TurnStart_BuiltInCommandRef_ReturnsInvalidParamsAndDoesNotSubmitInput()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[]
            {
                new
                {
                    type = "commandRef",
                    name = "new",
                    rawText = "/new"
                }
            }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
        Assert.Empty(_h.Service.LastSubmittedContent);
    }

    [Fact]
    public async Task TurnStart_MixedTextAndBuiltInCommandRef_ReturnsInvalidParamsAndDoesNotSubmitInput()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new object[]
            {
                new { type = "text", text = "Please " },
                new
                {
                    type = "commandRef",
                    name = "help",
                    rawText = "/help"
                }
            }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
        Assert.Empty(_h.Service.LastSubmittedContent);
    }

    [Fact]
    public async Task TurnStart_CustomCommandRef_MaterializesExpandedPrompt()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"turn_start_custom_{Guid.NewGuid():N}");
        var workspaceCraftPath = Path.Combine(tempRoot, ".craft");

        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceCraftPath, "commands"));
            await File.WriteAllTextAsync(
                Path.Combine(workspaceCraftPath, "commands", "code-review.md"),
                """
                ---
                description: Review changed files
                ---
                Review these files carefully: $ARGUMENTS
                """);

            using var harness = new AppServerTestHarness(workspaceCraftPath: workspaceCraftPath);
            await harness.InitializeAsync();

            var thread = await harness.Service.CreateThreadAsync(harness.Identity);
            harness.Service.EnqueueSubmitEvents(thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

            var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
            {
                threadId = thread.Id,
                input = new[]
                {
                    new
                    {
                        type = "commandRef",
                        name = "code-review",
                        rawText = "/code-review src/foo.cs"
                    }
                }
            });
            await harness.ExecuteRequestAsync(msg);

            var response = await harness.Transport.ReadNextSentAsync();
            AppServerTestHarness.AssertIsSuccessResponse(response);

            var textContent = Assert.IsType<TextContent>(Assert.Single(harness.Service.LastSubmittedContent));
            Assert.Equal("Review these files carefully: src/foo.cs", textContent.Text);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    // -------------------------------------------------------------------------
    // turn/interrupt
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TurnInterrupt_CallsCancelTurnAsync()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        // Add a running turn to the thread so validation passes (Issue E fix)
        var runningTurn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = thread.Id,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
        thread.Turns.Add(runningTurn);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnInterrupt, new
        {
            threadId = thread.Id,
            turnId = "turn_001"
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);

        Assert.Single(_h.Service.CancelledTurns);
        Assert.Equal(thread.Id, _h.Service.CancelledTurns[0].threadId);
        Assert.Equal("turn_001", _h.Service.CancelledTurns[0].turnId);
    }

    [Fact]
    public async Task ThreadMaintenanceInterrupt_CallsCancelThreadMaintenanceAsync()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadMaintenanceInterrupt, new
        {
            threadId = thread.Id
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);

        Assert.Single(_h.Service.CancelledMaintenances);
        Assert.Equal(thread.Id, _h.Service.CancelledMaintenances[0]);
    }

    [Fact]
    public async Task TurnEnqueue_ReturnsQueuedInputAndFullQueue()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnEnqueue, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Run this next" } }
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        var result = doc.RootElement.GetProperty("result");
        Assert.Equal("Run this next", result.GetProperty("queuedInput").GetProperty("displayText").GetString());
        Assert.Single(result.GetProperty("queuedInputs").EnumerateArray());
    }

    [Fact]
    public async Task TurnSteer_AddsGuidanceToExistingTurnWithoutStartingAnother()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        thread.Turns.Add(new SessionTurn
        {
            Id = "turn_001",
            ThreadId = thread.Id,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnSteer, new
        {
            threadId = thread.Id,
            expectedTurnId = "turn_001",
            input = new[] { new { type = "text", text = "Use this now" } }
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        Assert.Equal("turn_001", doc.RootElement.GetProperty("result").GetProperty("turnId").GetString());
        Assert.Single(thread.Turns);
        var guidance = Assert.Single(thread.QueuedInputs);
        Assert.Equal("guidancePending", guidance.Status);
        Assert.Equal("turn_001", guidance.ReadyAfterTurnId);
        Assert.Null(_h.Transport.TryReadSent());
    }

    [Fact]
    public async Task TurnSteer_MismatchedTurnDoesNotQueueInput()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        thread.Turns.Add(new SessionTurn
        {
            Id = "turn_002",
            ThreadId = thread.Id,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnSteer, new
        {
            threadId = thread.Id,
            expectedTurnId = "turn_001",
            input = new[] { new { type = "text", text = "Wrong turn" } }
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
        Assert.Empty(thread.QueuedInputs);
    }

    [Fact]
    public async Task TurnQueueRemove_ReturnsRemainingQueue()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        var queued = await _h.Service.EnqueueTurnInputAsync(
            thread.Id,
            [new TextContent("remove me")],
            inputSnapshot: new SessionInputSnapshot
            {
                NativeInputParts = [new SessionInputPart { Type = "text", Text = "remove me" }],
                MaterializedInputParts = [new SessionInputPart { Type = "text", Text = "remove me" }],
                DisplayText = "remove me"
            });

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnQueueRemove, new
        {
            threadId = thread.Id,
            queuedInputId = queued.Id
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        Assert.Empty(doc.RootElement.GetProperty("result").GetProperty("queuedInputs").EnumerateArray());
    }

    [Fact]
    public async Task TurnQueueReorder_ReturnsReorderedQueue()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        var first = await EnqueueTextAsync(thread.Id, "first");
        var second = await EnqueueTextAsync(thread.Id, "second");
        var third = await EnqueueTextAsync(thread.Id, "third");

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnQueueReorder, new
        {
            threadId = thread.Id,
            orderedQueuedInputIds = new[] { third.Id, first.Id, second.Id }
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        var queuedTexts = doc.RootElement
            .GetProperty("result")
            .GetProperty("queuedInputs")
            .EnumerateArray()
            .Select(input => input.GetProperty("displayText").GetString() ?? string.Empty)
            .ToArray();
        Assert.Equal(["third", "first", "second"], queuedTexts);
    }

    [Fact]
    public async Task TurnQueueReorder_RejectsMissingDuplicateAndUnknownIds()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        var first = await EnqueueTextAsync(thread.Id, "first");
        var second = await EnqueueTextAsync(thread.Id, "second");

        await AssertInvalidReorderAsync(thread.Id, [first.Id]);
        await AssertInvalidReorderAsync(thread.Id, [first.Id, first.Id]);
        await AssertInvalidReorderAsync(thread.Id, [second.Id, "queued_unknown"]);
    }

    [Fact]
    public async Task TurnEnqueue_PreservesSentAsGoal()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnEnqueue, new
        {
            threadId = thread.Id,
            input = new object[] { new { type = "text", text = "Ship the goal" } },
            sentAsGoal = true
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        var result = doc.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("queuedInput").GetProperty("sentAsGoal").GetBoolean());
        var queued = Assert.Single(result.GetProperty("queuedInputs").EnumerateArray());
        Assert.True(queued.GetProperty("sentAsGoal").GetBoolean());
    }

    [Fact]
    public async Task TurnQueueUpdate_MarksQueuedInputGuidancePending()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        thread.Turns.Add(new SessionTurn
        {
            Id = "turn_001",
            ThreadId = thread.Id,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });
        var queued = await _h.Service.EnqueueTurnInputAsync(
            thread.Id,
            [new TextContent("Use this hint")],
            inputSnapshot: new SessionInputSnapshot
            {
                NativeInputParts = [new SessionInputPart { Type = "text", Text = "Use this hint" }],
                MaterializedInputParts = [new SessionInputPart { Type = "text", Text = "Use this hint" }],
                DisplayText = "Use this hint"
            });

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnQueueUpdate, new
        {
            threadId = thread.Id,
            expectedTurnId = "turn_001",
            queuedInputId = queued.Id,
            status = "guidancePending"
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        var resultQueued = Assert.Single(doc.RootElement.GetProperty("result").GetProperty("queuedInputs").EnumerateArray());
        Assert.Equal("guidancePending", resultQueued.GetProperty("status").GetString());
        Assert.Empty(thread.Turns[0].Items);
    }

    [Fact]
    public async Task TurnQueueUpdate_RestoresGuidancePendingInputToQueuedIdempotently()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        thread.Turns.Add(new SessionTurn
        {
            Id = "turn_001",
            ThreadId = thread.Id,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });
        var queued = await EnqueueTextAsync(thread.Id, "Use this hint");
        await _h.Service.UpdateQueuedTurnInputAsync(thread.Id, queued.Id, "turn_001", "guidancePending");

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnQueueUpdate, new
        {
            threadId = thread.Id,
            expectedTurnId = "turn_001",
            queuedInputId = queued.Id,
            status = "queued"
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        var resultQueued = Assert.Single(doc.RootElement.GetProperty("result").GetProperty("queuedInputs").EnumerateArray());
        Assert.Equal("queued", resultQueued.GetProperty("status").GetString());
        Assert.Equal("Use this hint", resultQueued.GetProperty("displayText").GetString());
        Assert.Empty(thread.Turns[0].Items);

        var retry = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnQueueUpdate, new
        {
            threadId = thread.Id,
            expectedTurnId = "turn_001",
            queuedInputId = queued.Id,
            status = "queued"
        });
        await _h.ExecuteRequestAsync(retry);
        var retryDoc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(retryDoc);
        var retried = Assert.Single(retryDoc.RootElement.GetProperty("result").GetProperty("queuedInputs").EnumerateArray());
        Assert.Equal("queued", retried.GetProperty("status").GetString());
    }

    // -------------------------------------------------------------------------
    // turn/start — empty input validation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TurnStart_EmptyInput_ReturnsInvalidParams()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = Array.Empty<object>()
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.InvalidParamsCode);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private Task<QueuedTurnInput> EnqueueTextAsync(string threadId, string text) =>
        _h.Service.EnqueueTurnInputAsync(
            threadId,
            [new TextContent(text)],
            inputSnapshot: new SessionInputSnapshot
            {
                NativeInputParts = [new SessionInputPart { Type = "text", Text = text }],
                MaterializedInputParts = [new SessionInputPart { Type = "text", Text = text }],
                DisplayText = text
            });

    private async Task AssertInvalidReorderAsync(string threadId, string[] orderedQueuedInputIds)
    {
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnQueueReorder, new
        {
            threadId,
            orderedQueuedInputIds
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.InvalidParamsCode);
    }

    private static void AssertMethod(JsonDocument doc, string expectedMethod)
    {
        Assert.True(doc.RootElement.TryGetProperty("method", out var methodEl),
            $"Expected notification with method '{expectedMethod}' but got no 'method' property. " +
            $"Document: {doc.RootElement}");
        Assert.Equal(expectedMethod, methodEl.GetString());
    }
}
