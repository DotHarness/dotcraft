using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Memory;
using DotCraft.Tools;
using DotCraft.Tests.Sessions.Protocol.AppServer;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using ToolCallPayload = DotCraft.Sessions.ToolCallPayload;
using UserMessagePayload = DotCraft.Sessions.UserMessagePayload;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

[CollectionDefinition(WelcomeSuggestionServiceTestCollection.Name, DisableParallelization = true)]
public sealed class WelcomeSuggestionServiceTestCollection
{
    public const string Name = "WelcomeSuggestionServiceTests";
}

[Collection(WelcomeSuggestionServiceTestCollection.Name)]
public sealed class WelcomeSuggestionServiceTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;
    private const int RefreshTimeoutMs = 15_000;

    private readonly string _workspacePath;
    private readonly string _craftPath;
    private readonly ThreadStore _threadStore;
    private readonly SessionPersistenceService _persistence;
    private readonly MemoryStore _memoryStore;
    private readonly TestableSessionService _sessionService;

    public WelcomeSuggestionServiceTests()
    {
        _workspacePath = Path.Combine(Path.GetTempPath(), "welcome-suggest-tests", Guid.NewGuid().ToString("N"));
        _craftPath = Path.Combine(_workspacePath, ".craft");
        Directory.CreateDirectory(_craftPath);

        _threadStore = new ThreadStore(_craftPath);
        _persistence = new SessionPersistenceService(_threadStore);
        _memoryStore = new MemoryStore(_craftPath);
        _sessionService = new TestableSessionService(_threadStore);
    }

    [Fact]
    public async Task SuggestAsync_WhenHistoryIsInsufficient_ReturnsNoneAndSkipsModel()
    {
        await CreateThreadWithMessagesAsync("Need help", "/new", "thanks");

        var service = CreateService();

        var result = await service.SuggestAsync(new WelcomeSuggestionRequest
        {
            Identity = CreateIdentity(),
            MaxItems = 4
        });

        Assert.Equal("none", result.Source);
        Assert.Empty(result.Items);
        Assert.Empty(_sessionService.LastSubmittedContent);
        Assert.Null(_sessionService.LastSubmittedMessages);
    }

    [Fact]
    public async Task SuggestAsync_WhenWorkspaceConfigDisablesSuggestions_ReturnsNone()
    {
        Directory.CreateDirectory(_craftPath);
        await File.WriteAllTextAsync(
            Path.Combine(_craftPath, "config.json"),
            """
            {
              "WelcomeSuggestions": {
                "Enabled": false
              }
            }
            """);

        await CreateThreadWithMessagesAsync(
            "Review the Desktop welcome flow and identify where dynamic quick suggestions should plug in.",
            "Trace how thread history and workspace memory are loaded for the current workspace.");

        var service = CreateService();

        var result = await service.SuggestAsync(new WelcomeSuggestionRequest
        {
            Identity = CreateIdentity(),
            MaxItems = 4
        });

        Assert.Equal("none", result.Source);
        Assert.Empty(result.Items);
        Assert.Empty(_sessionService.LastSubmittedContent);
    }

    [Fact]
    public async Task SuggestAsync_FiltersNoise_UsesServerManagedTempThread_AndDeletesTempThread()
    {
        var thread = await CreateThreadWithMessagesAsync(
            "Review the Desktop welcome flow and identify where dynamic quick suggestions should plug in.",
            "/new",
            "thanks",
            "Review the Desktop welcome flow and identify where dynamic quick suggestions should plug in.",
            "Trace how thread history and workspace memory are loaded for the current workspace.");
        await CreateThreadWithMessagesAsync(
            "Map the current workspace memory loading path and suggest how to reuse it for welcome suggestions.",
            "继续",
            "Make sure the generated suggestions feel like likely next tasks, not generic categories.");

        _memoryStore.WriteLongTerm("The team is working on Desktop dynamic welcome suggestions.");
        _memoryStore.AppendHistory("Recent focus: thread history, welcome shortcuts, and workspace memory integration.");

        var initialThreadCount = (await _threadStore.LoadIndexAsync()).Count;
        var initialActiveFileCount = Directory.EnumerateFiles(Path.Combine(_craftPath, "threads", "active"), "*.jsonl").Count();

        _sessionService.SubmitInputHandler = (threadId, _, _) =>
        {
            return
            [
                new SessionEvent
                {
                    EventId = "evt_1",
                    EventType = SessionEventType.ItemCompleted,
                    ThreadId = threadId,
                    TurnId = "turn_001",
                    ItemId = "item_001",
                    Timestamp = DateTimeOffset.UtcNow,
                    Payload = new SessionItem
                    {
                        Id = "item_001",
                        TurnId = "turn_001",
                        Type = ItemType.ToolCall,
                        Status = ItemStatus.Completed,
                        CreatedAt = DateTimeOffset.UtcNow,
                        CompletedAt = DateTimeOffset.UtcNow,
                        Payload = new ToolCallPayload
                        {
                            ToolName = WelcomeSuggestionMethods.ToolName,
                            CallId = "call_001",
                            Arguments = new JsonObject
                            {
                                ["items"] = new JsonArray
                                {
                                    new JsonObject
                                    {
                                        ["title"] = "Review ConversationWelcome.tsx flow",
                                        ["prompt"] = "Review desktop/src/renderer/components/conversation/ConversationWelcome.tsx and point out where dynamic quick suggestions should be injected.",
                                        ["reason"] = "The recent history repeatedly mentions Desktop welcome flow work."
                                    },
                                    new JsonObject
                                    {
                                        ["title"] = "Trace thread history",
                                        ["prompt"] = "Trace how current-workspace thread history is loaded so we can reuse it to generate welcome suggestions.",
                                        ["reason"] = "Recent threads focus on thread history loading."
                                    },
                                    new JsonObject
                                    {
                                        ["title"] = "Reuse workspace memory",
                                        ["prompt"] = "Audit how MEMORY.md and HISTORY.md are loaded today and propose the cleanest way to feed them into welcome suggestions.",
                                        ["reason"] = "Workspace memory appears in both recent messages and HISTORY.md."
                                    },
                                    new JsonObject
                                    {
                                        ["title"] = "Tune welcome/suggestions wording",
                                        ["prompt"] = "Design four prompts for welcome/suggestions that reference specific artifacts such as WelcomeSuggestionService.cs instead of generic feature categories.",
                                        ["reason"] = "Recent user intent explicitly asks for next-task style suggestions."
                                    }
                                }
                            }
                        }
                    }
                }
            ];
        };

        var service = CreateService();
        service.ScheduleRefresh(_workspacePath);
        await WaitForAsync(() => File.Exists(GetPersistedCachePath()), timeoutMs: RefreshTimeoutMs);

        var result = await service.SuggestAsync(new WelcomeSuggestionRequest
        {
            Identity = CreateIdentity(),
            MaxItems = 4
        });

        Assert.Equal("dynamic", result.Source);
        Assert.Equal(4, result.Items.Count);
        Assert.NotEmpty(_sessionService.LastSubmittedContent);
        Assert.Null(_sessionService.LastSubmittedMessages);
        Assert.Contains(
            "Inspect workspace MEMORY.md and HISTORY.md, infer the likely next tasks",
            string.Concat(_sessionService.LastSubmittedContent.OfType<TextContent>().Select(item => item.Text)));

        var remainingThreads = await _threadStore.LoadIndexAsync();
        Assert.Equal(initialThreadCount, remainingThreads.Count);
        Assert.DoesNotContain(remainingThreads, summary =>
            string.Equals(summary.OriginChannel, WelcomeSuggestionConstants.ChannelName, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(remainingThreads, summary => summary.Id == thread.Id);
        Assert.Equal(initialActiveFileCount, Directory.EnumerateFiles(Path.Combine(_craftPath, "threads", "active"), "*.jsonl").Count());
    }

    [Fact]
    public async Task ReadWelcomeWorkspaceMemory_ExtractsHighlights()
    {
        _memoryStore.WriteLongTerm("""
            The current focus is improving Desktop welcome suggestions.
            Make the generated prompts specific to thread history and memory from ConversationWelcome.tsx.
            """);
        _memoryStore.AppendHistory("""
            Welcome suggestion output should mention concrete modules like WelcomeSuggestionService.cs or settings keys in .craft/config.json instead of generic onboarding.
            """);

        var methods = new WelcomeSuggestionToolMethods(_memoryStore);

        var resultJson = await methods.ReadWelcomeWorkspaceMemory();
        var result = JsonSerializer.Deserialize<WelcomeWorkspaceMemoryResult>(resultJson, JsonOptions)!;

        Assert.NotEmpty(result.MemoryHighlights);
    }

    [Fact]
    public async Task WelcomeSuggestionTools_ReturnJsonStrings()
    {
        _memoryStore.WriteLongTerm("Desktop welcome suggestions should use workspace memory.");

        var methods = new WelcomeSuggestionToolMethods(_memoryStore);

        var memoryJson = await methods.ReadWelcomeWorkspaceMemory();
        var memory = JsonSerializer.Deserialize<WelcomeWorkspaceMemoryResult>(memoryJson, JsonOptions)!;
        Assert.Contains("workspace memory", memory.Memory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WelcomeSuggestionTools_ExposeStringReturnSchema()
    {
        var methods = new WelcomeSuggestionToolMethods(_memoryStore);
        var toolMethods = new[]
        {
            nameof(WelcomeSuggestionToolMethods.ReadWelcomeWorkspaceMemory)
        };

        foreach (var methodName in toolMethods)
        {
            var method = typeof(WelcomeSuggestionToolMethods).GetMethod(methodName)!;
            Assert.Equal(typeof(Task<string>), method.ReturnType);

            var function = methodName switch
            {
                nameof(WelcomeSuggestionToolMethods.ReadWelcomeWorkspaceMemory) =>
                    AIFunctionFactory.Create(methods.ReadWelcomeWorkspaceMemory),
                _ => throw new InvalidOperationException(methodName)
            };
            var rawSchema = Assert.NotNull(function.ReturnJsonSchema).GetRawText();
            Assert.Contains("\"string\"", rawSchema, StringComparison.Ordinal);
            Assert.DoesNotContain("threadId", rawSchema, StringComparison.Ordinal);
            Assert.DoesNotContain("memoryHighlights", rawSchema, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SuggestAsync_WithNoPersistedCache_ReturnsNoneWithoutCallingModel()
    {
        await CreateThreadWithMessagesAsync(
            "Review how welcome suggestions are generated from workspace history and memory.",
            "Tighten the prompt so suggestions mention specific modules and tasks.");

        var service = CreateService();
        var result = await service.SuggestAsync(new WelcomeSuggestionRequest
        {
            Identity = CreateIdentity(),
            MaxItems = 4
        });

        Assert.Equal("none", result.Source);
        Assert.Empty(result.Items);
        Assert.Empty(_sessionService.LastSubmittedContent);
    }

    [Fact]
    public async Task SuggestAsync_WithPersistedCache_FromNewServiceInstance_ReturnsDynamicWithoutCallingModel()
    {
        await WritePersistedCacheAsync("persisted-snapshot");

        var service = CreateService();
        var result = await service.SuggestAsync(new WelcomeSuggestionRequest
        {
            Identity = CreateIdentity(),
            MaxItems = 4
        });

        Assert.Equal("dynamic", result.Source);
        Assert.Equal("persisted-snapshot", result.Fingerprint);
        Assert.Equal(4, result.Items.Count);
        Assert.Empty(_sessionService.LastSubmittedContent);
    }

    [Fact]
    public async Task ScheduleRefresh_WithThreadHistoryOnly_SkipsModelGeneration()
    {
        await CreateThreadWithMessagesAsync(
            "Review how welcome suggestions are generated from workspace history and memory.",
            "Tighten the prompt so suggestions mention specific modules and tasks.");

        var submitCount = 0;
        _sessionService.SubmitInputHandler = (_, _, _) =>
        {
            Interlocked.Increment(ref submitCount);
            return [];
        };

        var service = CreateService();
        service.ScheduleRefresh(_workspacePath);
        await Task.Delay(1500);

        Assert.Equal(0, submitCount);
    }

    [Fact]
    public async Task ScheduleRefresh_WithMemoryOnlyEvidence_GeneratesPersistedCache()
    {
        _memoryStore.WriteLongTerm("The workspace is reducing Desktop welcome suggestion cost by using memory-only evidence.");
        _memoryStore.AppendHistory("Decision: refresh welcome suggestions after successful memory consolidation.");

        _sessionService.SubmitInputHandler = (threadId, _, _) =>
        {
            return
            [
                new SessionEvent
                {
                    EventId = "evt_memory_only",
                    EventType = SessionEventType.ItemCompleted,
                    ThreadId = threadId,
                    TurnId = "turn_memory_only",
                    ItemId = "item_memory_only",
                    Timestamp = DateTimeOffset.UtcNow,
                    Payload = new SessionItem
                    {
                        Id = "item_memory_only",
                        TurnId = "turn_memory_only",
                        Type = ItemType.ToolCall,
                        Status = ItemStatus.Completed,
                        CreatedAt = DateTimeOffset.UtcNow,
                        CompletedAt = DateTimeOffset.UtcNow,
                        Payload = new ToolCallPayload
                        {
                            ToolName = WelcomeSuggestionMethods.ToolName,
                            CallId = "call_memory_only",
                            Arguments = new JsonObject
                            {
                                ["items"] = BuildConcreteItems()
                            }
                        }
                    }
                }
            ];
        };

        var service = CreateService();
        service.ScheduleRefresh(_workspacePath);
        await WaitForAsync(() => File.Exists(GetPersistedCachePath()), timeoutMs: RefreshTimeoutMs);

        var result = await service.SuggestAsync(new WelcomeSuggestionRequest
        {
            Identity = CreateIdentity(),
            MaxItems = 4
        });

        Assert.Equal("dynamic", result.Source);
        Assert.Equal(4, result.Items.Count);
    }

    [Fact]
    public async Task SuggestAsync_WithOldPersistedCacheSchema_ReturnsNone()
    {
        await WritePersistedCacheAsync("old-thread-history-snapshot", schemaVersion: 1);

        var service = CreateService();
        var result = await service.SuggestAsync(new WelcomeSuggestionRequest
        {
            Identity = CreateIdentity(),
            MaxItems = 4
        });

        Assert.Equal("none", result.Source);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task SuggestAsync_WhenModelReturnsGenericSuggestions_ReturnsNone()
    {
        await CreateThreadWithMessagesAsync(
            "Review how welcome suggestions are generated from workspace history and memory.",
            "Tighten the prompt so suggestions mention specific modules and tasks.");
        _memoryStore.WriteLongTerm("Workspace memory says welcome suggestions should be concrete and implementation-specific.");

        _sessionService.SubmitInputHandler = (threadId, _, _) =>
        {
            return
            [
                new SessionEvent
                {
                    EventId = "evt_generic",
                    EventType = SessionEventType.ItemCompleted,
                    ThreadId = threadId,
                    TurnId = "turn_001",
                    ItemId = "item_001",
                    Timestamp = DateTimeOffset.UtcNow,
                    Payload = new SessionItem
                    {
                        Id = "item_001",
                        TurnId = "turn_001",
                        Type = ItemType.ToolCall,
                        Status = ItemStatus.Completed,
                        CreatedAt = DateTimeOffset.UtcNow,
                        CompletedAt = DateTimeOffset.UtcNow,
                        Payload = new ToolCallPayload
                        {
                            ToolName = WelcomeSuggestionMethods.ToolName,
                            CallId = "call_generic",
                            Arguments = new JsonObject
                            {
                                ["items"] = new JsonArray
                                {
                                    new JsonObject
                                    {
                                        ["title"] = "Explore features",
                                        ["prompt"] = "What features does this app offer?",
                                        ["reason"] = "Generic onboarding."
                                    },
                                    new JsonObject
                                    {
                                        ["title"] = "Learn the basics",
                                        ["prompt"] = "Teach me the basics.",
                                        ["reason"] = "Generic onboarding."
                                    },
                                    new JsonObject
                                    {
                                        ["title"] = "Setup",
                                        ["prompt"] = "Help me set up my workspace.",
                                        ["reason"] = "Generic onboarding."
                                    },
                                    new JsonObject
                                    {
                                        ["title"] = "Project",
                                        ["prompt"] = "Help me start a new project.",
                                        ["reason"] = "Generic onboarding."
                                    }
                                }
                            }
                        }
                    }
                }
            ];
        };

        var service = CreateService();
        service.ScheduleRefresh(_workspacePath);
        await WaitForAsync(() => _sessionService.LastSubmittedContent.Count > 0, timeoutMs: RefreshTimeoutMs);

        var result = await service.SuggestAsync(new WelcomeSuggestionRequest
        {
            Identity = CreateIdentity(),
            MaxItems = 4
        });

        Assert.Equal("none", result.Source);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ScheduleRefresh_WhenDisposedBeforeDebounce_KeepsPersistedSnapshot()
    {
        await WritePersistedCacheAsync("preserved-before-dispose");
        var before = await File.ReadAllTextAsync(GetPersistedCachePath());
        await CreateThreadWithMessagesAsync(
            "Review how welcome suggestions reuse workspace history and memory in Desktop.",
            "Trace the welcome suggestion service and tighten its cache refresh behavior.");
        _memoryStore.WriteLongTerm("Desktop welcome suggestions should be refreshed from memory only.");

        var service = CreateService();
        service.ScheduleRefresh(_workspacePath);
        await service.DisposeAsync();

        var after = await File.ReadAllTextAsync(GetPersistedCachePath());
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task ScheduleRefresh_WithMatchingPersistedFingerprint_SkipsModelGeneration()
    {
        await CreateThreadWithMessagesAsync(
            "Review how welcome suggestions reuse workspace history and memory in Desktop.",
            "Trace the welcome suggestion service and tighten its cache refresh behavior.");
        _memoryStore.WriteLongTerm("Desktop welcome suggestions should be refreshed from memory only.");

        var submitCount = 0;
        _sessionService.SubmitInputHandler = (_, _, _) =>
        {
            Interlocked.Increment(ref submitCount);
            return
            [
                new SessionEvent
                {
                    EventId = "evt_initial_refresh",
                    EventType = SessionEventType.ItemCompleted,
                    ThreadId = "thread_initial_refresh",
                    TurnId = "turn_initial_refresh",
                    ItemId = "item_initial_refresh",
                    Timestamp = DateTimeOffset.UtcNow,
                    Payload = new SessionItem
                    {
                        Id = "item_initial_refresh",
                        TurnId = "turn_initial_refresh",
                        Type = ItemType.ToolCall,
                        Status = ItemStatus.Completed,
                        CreatedAt = DateTimeOffset.UtcNow,
                        CompletedAt = DateTimeOffset.UtcNow,
                        Payload = new ToolCallPayload
                        {
                            ToolName = WelcomeSuggestionMethods.ToolName,
                            CallId = "call_initial_refresh",
                            Arguments = new JsonObject
                            {
                                ["items"] = BuildConcreteItems()
                            }
                        }
                    }
                }
            ];
        };

        var firstService = CreateService();
        firstService.ScheduleRefresh(_workspacePath);
        await WaitForAsync(
            () => File.Exists(GetPersistedCachePath()),
            timeoutMs: RefreshTimeoutMs,
            describeFailure: () =>
                $"submitCount={submitCount}, lastSubmittedContent={_sessionService.LastSubmittedContent.Count}, cachePath={GetPersistedCachePath()}");
        Assert.Equal(1, submitCount);

        _sessionService.SubmitInputHandler = (_, _, _) =>
        {
            Interlocked.Increment(ref submitCount);
            return [];
        };

        var secondService = CreateService();
        secondService.ScheduleRefresh(_workspacePath);
        await Task.Delay(1500);

        Assert.Equal(1, submitCount);
    }

    [Fact]
    public async Task ScheduleRefresh_WhenGenerationFails_KeepsPersistedSnapshot()
    {
        await WritePersistedCacheAsync("preserved-after-failure");
        var before = await File.ReadAllTextAsync(GetPersistedCachePath());
        await CreateThreadWithMessagesAsync(
            "Review how welcome suggestions reuse workspace history and memory in Desktop.",
            "Trace the welcome suggestion service and tighten its cache refresh behavior.");
        _memoryStore.WriteLongTerm("Desktop welcome suggestions should be refreshed from memory only.");

        _sessionService.SubmitInputHandler = (_, _, _) => [];

        var service = CreateService();
        service.ScheduleRefresh(_workspacePath);
        await WaitForAsync(() => _sessionService.LastSubmittedContent.Count > 0, timeoutMs: RefreshTimeoutMs);

        var after = await File.ReadAllTextAsync(GetPersistedCachePath());
        Assert.Equal(before, after);

        var result = await service.SuggestAsync(new WelcomeSuggestionRequest
        {
            Identity = CreateIdentity(),
            MaxItems = 4
        });
        Assert.Equal("dynamic", result.Source);
        Assert.Equal("preserved-after-failure", result.Fingerprint);
    }

    [Fact]
    public async Task ScheduleRefresh_OnMemoryConsolidated_WritesPersistedCache_AndSubsequentSuggestServesIt()
    {
        await CreateThreadWithMessagesAsync(
            "Review how welcome suggestions reuse workspace history and memory in Desktop.",
            "Trace the welcome suggestion service and tighten its cache refresh behavior.");
        _memoryStore.WriteLongTerm("Desktop welcome suggestions should use only MEMORY.md and HISTORY.md.");

        _sessionService.SubmitInputHandler = (threadId, _, _) =>
        {
            return
            [
                new SessionEvent
                {
                    EventId = "evt_refresh_cache",
                    EventType = SessionEventType.ItemCompleted,
                    ThreadId = threadId,
                    TurnId = "turn_refresh_cache",
                    ItemId = "item_refresh_cache",
                    Timestamp = DateTimeOffset.UtcNow,
                    Payload = new SessionItem
                    {
                        Id = "item_refresh_cache",
                        TurnId = "turn_refresh_cache",
                        Type = ItemType.ToolCall,
                        Status = ItemStatus.Completed,
                        CreatedAt = DateTimeOffset.UtcNow,
                        CompletedAt = DateTimeOffset.UtcNow,
                        Payload = new ToolCallPayload
                        {
                            ToolName = WelcomeSuggestionMethods.ToolName,
                            CallId = "call_refresh_cache",
                            Arguments = new JsonObject
                            {
                                ["items"] = new JsonArray
                                {
                                    new JsonObject
                                    {
                                        ["title"] = "Review ConversationWelcome.tsx flow",
                                        ["prompt"] = "Review desktop/src/renderer/components/conversation/ConversationWelcome.tsx and point out where dynamic quick suggestions should be injected."
                                    },
                                    new JsonObject
                                    {
                                        ["title"] = "Trace WelcomeSuggestionService.cs",
                                        ["prompt"] = "Trace src/DotCraft.Core/Protocol/WelcomeSuggestionService.cs to document how cache-only welcome/suggestions responses are served."
                                    },
                                    new JsonObject
                                    {
                                        ["title"] = "Inspect welcome/suggestions contract",
                                        ["prompt"] = "Inspect specs/protocols/appserver-protocol.md and verify the welcome/suggestions semantics for source and fingerprint."
                                    },
                                    new JsonObject
                                    {
                                        ["title"] = "Audit workspace/config/update flow",
                                        ["prompt"] = "Audit workspace/config/update handling for WelcomeSuggestions.Enabled and list the notification flow."
                                    }
                                }
                            }
                        }
                    }
                }
            ];
        };

        var service = CreateService();
        service.ScheduleRefresh(_workspacePath);
        await WaitForAsync(() => _sessionService.LastSubmittedContent.Count > 0, timeoutMs: RefreshTimeoutMs);

        var cachePath = Path.Combine(_workspacePath, ".craft", "cache", "welcome-suggestions.json");
        await WaitForAsync(() => File.Exists(cachePath), timeoutMs: RefreshTimeoutMs);

        var result = await service.SuggestAsync(new WelcomeSuggestionRequest
        {
            Identity = CreateIdentity(),
            MaxItems = 4
        });

        Assert.Equal("dynamic", result.Source);
        Assert.Equal(4, result.Items.Count);
    }

    [Fact]
    public async Task ScheduleRefresh_Debounces_MultipleTriggersProduceOneRun()
    {
        await CreateThreadWithMessagesAsync(
            "Review welcome suggestion debounce behavior.",
            "Ensure repeated turn-complete signals coalesce into one refresh.");
        _memoryStore.WriteLongTerm("Welcome suggestion refreshes are triggered after memory consolidation.");

        var submitCount = 0;
        _sessionService.SubmitInputHandler = (_, _, _) =>
        {
            Interlocked.Increment(ref submitCount);
            return
            [
                new SessionEvent
                {
                    EventId = "evt_debounce",
                    EventType = SessionEventType.ItemCompleted,
                    ThreadId = "thread_debounce",
                    TurnId = "turn_debounce",
                    ItemId = "item_debounce",
                    Timestamp = DateTimeOffset.UtcNow,
                    Payload = new SessionItem
                    {
                        Id = "item_debounce",
                        TurnId = "turn_debounce",
                        Type = ItemType.ToolCall,
                        Status = ItemStatus.Completed,
                        CreatedAt = DateTimeOffset.UtcNow,
                        CompletedAt = DateTimeOffset.UtcNow,
                        Payload = new ToolCallPayload
                        {
                            ToolName = WelcomeSuggestionMethods.ToolName,
                            CallId = "call_debounce",
                            Arguments = new JsonObject
                            {
                                ["items"] = BuildConcreteItems()
                            }
                        }
                    }
                }
            ];
        };

        var service = CreateService();
        service.ScheduleRefresh(_workspacePath);
        service.ScheduleRefresh(_workspacePath);
        service.ScheduleRefresh(_workspacePath);
        await WaitForAsync(() => submitCount > 0, timeoutMs: RefreshTimeoutMs);
        await Task.Delay(1200);

        Assert.Equal(1, submitCount);
    }

    [Fact]
    public async Task ScheduleRefresh_IgnoresInternalWelcomeThreads()
    {
        var submitCount = 0;
        _sessionService.SubmitInputHandler = (_, _, _) =>
        {
            Interlocked.Increment(ref submitCount);
            return [];
        };

        var internalThread = await _sessionService.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = WelcomeSuggestionConstants.ChannelName,
            UserId = WelcomeSuggestionConstants.InternalUserId,
            WorkspacePath = _workspacePath,
            ChannelContext = "internal:welcome"
        });
        await _threadStore.SaveThreadAsync(internalThread);

        var service = CreateService();
        service.ScheduleRefresh(_workspacePath, internalThread.Id);
        await Task.Delay(1500);

        Assert.Equal(0, submitCount);
    }

    [Fact]
    public async Task ScheduleRefresh_WhenWorkspaceConfigDisablesSuggestions_SkipsRefresh()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_craftPath, "config.json"),
            """
            {
              "WelcomeSuggestions": {
                "Enabled": false
              }
            }
            """);
        await CreateThreadWithMessagesAsync(
            "Review welcome suggestion refresh policy.",
            "Ensure config disable state stops background generation.");

        var submitCount = 0;
        _sessionService.SubmitInputHandler = (_, _, _) =>
        {
            Interlocked.Increment(ref submitCount);
            return [];
        };

        var service = CreateService();
        service.ScheduleRefresh(_workspacePath);
        await Task.Delay(1500);

        Assert.Equal(0, submitCount);
    }

    private static JsonArray BuildConcreteItems() =>
    [
        new JsonObject
        {
            ["title"] = "Review ConversationWelcome.tsx flow",
            ["prompt"] = "Review desktop/src/renderer/components/conversation/ConversationWelcome.tsx and point out where dynamic quick suggestions should be injected."
        },
        new JsonObject
        {
            ["title"] = "Trace WelcomeSuggestionService.cs",
            ["prompt"] = "Trace src/DotCraft.Core/Protocol/WelcomeSuggestionService.cs to document how cache-only welcome/suggestions responses are served."
        },
        new JsonObject
        {
            ["title"] = "Inspect welcome/suggestions contract",
            ["prompt"] = "Inspect specs/protocols/appserver-protocol.md and verify the welcome/suggestions semantics for source and fingerprint."
        },
        new JsonObject
        {
            ["title"] = "Audit workspace/config/update flow",
            ["prompt"] = "Audit workspace/config/update handling for WelcomeSuggestions.Enabled and list the notification flow."
        }
    ];

    private string GetPersistedCachePath() =>
        Path.Combine(_workspacePath, ".craft", "cache", "welcome-suggestions.json");

    private async Task WritePersistedCacheAsync(string fingerprint, int schemaVersion = 2)
    {
        var cachePath = GetPersistedCachePath();
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        var payload = new
        {
            SchemaVersion = schemaVersion,
            Result = new WelcomeSuggestionSnapshot
            {
                Source = "dynamic",
                Fingerprint = fingerprint,
                GeneratedAt = DateTimeOffset.UtcNow,
                Items =
                [
                    new WelcomeSuggestion
                    {
                        Title = "Review ConversationWelcome.tsx flow",
                        Prompt = "Review desktop/src/renderer/components/conversation/ConversationWelcome.tsx and point out where dynamic quick suggestions should be injected."
                    },
                    new WelcomeSuggestion
                    {
                        Title = "Trace WelcomeSuggestionService.cs",
                        Prompt = "Trace src/DotCraft.Core/Protocol/WelcomeSuggestionService.cs to document how cache-only welcome/suggestions responses are served."
                    },
                    new WelcomeSuggestion
                    {
                        Title = "Inspect welcome/suggestions contract",
                        Prompt = "Inspect specs/protocols/appserver-protocol.md and verify the welcome/suggestions semantics for source and fingerprint."
                    },
                    new WelcomeSuggestion
                    {
                        Title = "Audit workspace/config/update flow",
                        Prompt = "Audit workspace/config/update handling for WelcomeSuggestions.Enabled and list the notification flow."
                    }
                ]
            }
        };
        await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(payload));
    }

    private WelcomeSuggestionService CreateService() =>
        new(_sessionService, _persistence, _memoryStore, _workspacePath, NullLogger<WelcomeSuggestionService>.Instance);

    private SessionIdentity CreateIdentity() => new()
    {
        ChannelName = "dotcraft-desktop",
        UserId = "local",
        ChannelContext = $"workspace:{_workspacePath}",
        WorkspacePath = _workspacePath
    };

    private async Task<SessionThread> CreateThreadWithMessagesAsync(params string[] messages)
    {
        var thread = await _sessionService.CreateThreadAsync(CreateIdentity());
        foreach (var message in messages)
        {
            var turnId = SessionIdGenerator.NewTurnId(thread.Turns.Count + 1);
            thread.Turns.Add(new SessionTurn
            {
                Id = turnId,
                ThreadId = thread.Id,
                Status = TurnStatus.Completed,
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
                Items =
                [
                    new SessionItem
                    {
                        Id = SessionIdGenerator.NewItemId(1),
                        TurnId = turnId,
                        Type = ItemType.UserMessage,
                        Status = ItemStatus.Completed,
                        CreatedAt = DateTimeOffset.UtcNow,
                        CompletedAt = DateTimeOffset.UtcNow,
                        Payload = new UserMessagePayload { Text = message }
                    }
                ]
            });
        }

        thread.LastActiveAt = DateTimeOffset.UtcNow;
        await _threadStore.SaveThreadAsync(thread);
        return thread;
    }

    private static async Task WaitForAsync(
        Func<bool> condition,
        int timeoutMs = 2000,
        Func<string>? describeFailure = null)
    {
        var started = DateTime.UtcNow;
        while (!condition())
        {
            if ((DateTime.UtcNow - started).TotalMilliseconds > timeoutMs)
            {
                var detail = describeFailure?.Invoke();
                throw new TimeoutException(string.IsNullOrWhiteSpace(detail)
                    ? "Condition was not satisfied in time."
                    : $"Condition was not satisfied in time. {detail}");
            }

            await Task.Delay(10);
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workspacePath, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
