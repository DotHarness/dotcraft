using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Memory;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Context;

public sealed class MemoryForkConsolidatorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "MemoryFork_" + Guid.NewGuid().ToString("N")[..8]);

    public MemoryForkConsolidatorTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { }
    }

    [Fact]
    public async Task ConsolidateAsync_WithSameModelSnapshotRunsForkAndSavesStructuredResult()
    {
        var chatClient = new RecordingChatClient("""
        {
          "history_entry": "[2026-05-06 10:00] User prefers blue.",
          "memory_update": "- User prefers blue."
        }
        """);
        var memoryStore = new MemoryStore(_tempDir);
        var legacy = new FakeMemoryConsolidator(MemoryConsolidationResult.Skipped("legacy_fallback"));
        var consolidator = new MemoryForkConsolidator(
            new MaintenanceForkRunner(chatClient),
            legacy,
            memoryStore,
            mainModelId: "gpt-test",
            consolidationModelId: "gpt-test",
            workspaceRoot: _tempDir);
        var tool = AIFunctionFactory.Create(() => "ok", name: "ReadFile", description: "Read a file.");
        var snapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "remember blue")],
            new ChatOptions
            {
                Instructions = "stable base",
                ModelId = "gpt-test",
                Tools = [tool]
            });

        var result = await consolidator.ConsolidateAsync(
            [new ChatMessage(ChatRole.User, "remember blue")],
            snapshot);

        Assert.Equal(MemoryConsolidationOutcome.Succeeded, result.Outcome);
        Assert.True(result.MemoryWritten);
        Assert.True(result.HistoryWritten);
        Assert.Contains("User prefers blue", memoryStore.ReadLongTerm());
        Assert.Contains("User prefers blue", memoryStore.ReadHistory());
        Assert.Equal(0, legacy.Calls);
        Assert.Equal("stable base", chatClient.Options?.Instructions);
        Assert.Equal("gpt-test", chatClient.Options?.ModelId);
        Assert.Equal("ReadFile", Assert.Single(chatClient.Options?.Tools ?? []).Name);
        Assert.Contains("## Maintenance Task", chatClient.Messages[^1].Text);
        Assert.Contains("Task: memory_consolidation", chatClient.Messages[^1].Text);
        Assert.DoesNotContain("## Current MEMORY.md", chatClient.Messages[^1].Text);
        Assert.DoesNotContain("## Completed conversation snapshot", chatClient.Messages[^1].Text);
        Assert.DoesNotContain("remember blue", chatClient.Messages[^1].Text);
    }

    [Fact]
    public async Task ConsolidateAsync_WithAnthropicSameModelSnapshotSerializesAdaptiveThinking()
    {
        var handler = new AnthropicCaptureHandler("""{"status":"unchanged"}""");
        var anthropicClient = new AnthropicClient
        {
            HttpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            ApiKey = "test-key"
        };
        var config = new AppConfig
        {
            ProviderId = "test",
            ProviderPreferences = new() { ["test"] = new ModelPreference { Model = "claude-opus-4-8"  } },
            Reasoning = new AppConfig.ReasoningConfig
            {
                Enabled = true,
                Effort = ReasoningEffort.High,
                Output = ReasoningOutput.Full
            }
        };
        var chatClient = ProviderChatClientAdapters.CreateRequestAdaptedClient(
            anthropicClient.AsIChatClient("claude-opus-4-8"),
            config,
            Runtime(ModelProviderProtocols.Anthropic, "claude-opus-4-8"),
            useDefaultReasoning: false);
        var memoryStore = new MemoryStore(_tempDir);
        var legacy = new FakeMemoryConsolidator(MemoryConsolidationResult.Skipped("legacy_fallback"));
        var consolidator = new MemoryForkConsolidator(
            new MaintenanceForkRunner(chatClient),
            legacy,
            memoryStore,
            mainModelId: "claude-opus-4-8",
            consolidationModelId: "claude-opus-4-8",
            workspaceRoot: _tempDir);
        var snapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "remember blue")],
            new ChatOptions
            {
                Instructions = "stable base",
                ModelId = "claude-opus-4-8",
                Reasoning = config.Reasoning.ToOptions()
            });

        await consolidator.ConsolidateAsync(
            [new ChatMessage(ChatRole.User, "remember blue")],
            snapshot);

        Assert.NotNull(handler.LastRequestJson);
        using var document = JsonDocument.Parse(handler.LastRequestJson!);
        var root = document.RootElement;
        Assert.Equal("adaptive", root.GetProperty("thinking").GetProperty("type").GetString());
        Assert.False(root.GetProperty("thinking").TryGetProperty("budget_tokens", out _));
        Assert.Equal("high", root.GetProperty("output_config").GetProperty("effort").GetString());
    }

    [Fact]
    public async Task ConsolidateAsync_WhenForkWritesMemoryFilesThroughTools_SucceedsWithoutLegacyOrFinalText()
    {
        var memoryStore = new MemoryStore(_tempDir);
        var chatClient = new ToolWritingChatClient(
            memoryStore.LongTermFilePath,
            "- User prefers blue.",
            memoryStore.HistoryFilePath,
            "[2026-05-06 10:00] User prefers blue.\n\n");
        var legacy = new FakeMemoryConsolidator(MemoryConsolidationResult.Skipped("legacy_fallback"));
        var consolidator = new MemoryForkConsolidator(
            new MaintenanceForkRunner(chatClient),
            legacy,
            memoryStore,
            mainModelId: "gpt-test",
            consolidationModelId: "gpt-test",
            workspaceRoot: _tempDir);
        var snapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "remember blue")],
            new ChatOptions
            {
                ModelId = "gpt-test",
                Tools = CreateFileTools(_tempDir)
            });

        var result = await consolidator.ConsolidateAsync(
            [new ChatMessage(ChatRole.User, "remember blue")],
            snapshot);

        Assert.Equal(MemoryConsolidationOutcome.Succeeded, result.Outcome);
        Assert.True(result.MemoryWritten);
        Assert.True(result.HistoryWritten);
        Assert.Contains("User prefers blue", memoryStore.ReadLongTerm());
        Assert.Contains("User prefers blue", memoryStore.ReadHistory());
        Assert.Equal(0, legacy.Calls);
    }

    [Fact]
    public async Task ConsolidateAsync_CreatesMissingHistoryFileWithoutCountingItAsAWrite()
    {
        var chatClient = new RecordingChatClient("""{"status":"unchanged"}""");
        var memoryStore = new MemoryStore(_tempDir);
        var legacy = new FakeMemoryConsolidator(MemoryConsolidationResult.Failed("legacy should not run"));
        var consolidator = new MemoryForkConsolidator(
            new MaintenanceForkRunner(chatClient),
            legacy,
            memoryStore,
            mainModelId: "gpt-test",
            consolidationModelId: "gpt-test",
            workspaceRoot: _tempDir);
        var snapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "nothing durable")],
            new ChatOptions { ModelId = "gpt-test" });

        var result = await consolidator.ConsolidateAsync(
            [new ChatMessage(ChatRole.User, "nothing durable")],
            snapshot);

        Assert.Equal(MemoryConsolidationOutcome.Skipped, result.Outcome);
        Assert.False(result.MemoryWritten);
        Assert.False(result.HistoryWritten);
        Assert.True(File.Exists(memoryStore.HistoryFilePath));
        Assert.Equal(string.Empty, memoryStore.ReadHistory());
        Assert.Equal(0, legacy.Calls);
    }

    [Fact]
    public async Task ConsolidateAsync_RestoresFilesWhenHistoryIsRewrittenInsteadOfAppended()
    {
        var memoryStore = new MemoryStore(_tempDir);
        memoryStore.WriteLongTerm("- Existing memory.");
        memoryStore.AppendHistory("[2026-05-06 09:00] Existing event.");
        var originalMemory = memoryStore.ReadLongTerm();
        var originalHistory = memoryStore.ReadHistory();
        var chatClient = new ToolWritingChatClient(
            memoryStore.LongTermFilePath,
            "- Corrupted memory.",
            memoryStore.HistoryFilePath,
            "[2026-05-06 10:00] Replacement only.\n\n");
        var legacy = new FakeMemoryConsolidator(MemoryConsolidationResult.Skipped("legacy_fallback"));
        var consolidator = new MemoryForkConsolidator(
            new MaintenanceForkRunner(chatClient),
            legacy,
            memoryStore,
            mainModelId: "gpt-test",
            consolidationModelId: "gpt-test",
            workspaceRoot: _tempDir);
        var snapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "remember blue")],
            new ChatOptions
            {
                ModelId = "gpt-test",
                Tools = CreateFileTools(_tempDir)
            });

        var result = await consolidator.ConsolidateAsync(
            [new ChatMessage(ChatRole.User, "remember blue")],
            snapshot);

        Assert.Equal(MemoryConsolidationOutcome.Skipped, result.Outcome);
        Assert.Equal("legacy_fallback", result.Message);
        Assert.Equal(originalMemory, memoryStore.ReadLongTerm());
        Assert.Equal(originalHistory, memoryStore.ReadHistory());
        Assert.Equal(1, legacy.Calls);
    }

    [Fact]
    public async Task ConsolidateAsync_DeniesNonFileToolExecutionButKeepsToolSchema()
    {
        var chatClient = new DeniedToolChatClient("Exec", new Dictionary<string, object?>
        {
            ["command"] = "dotnet test"
        });
        var memoryStore = new MemoryStore(_tempDir);
        var legacy = new FakeMemoryConsolidator(MemoryConsolidationResult.Skipped("legacy_fallback"));
        var consolidator = new MemoryForkConsolidator(
            new MaintenanceForkRunner(chatClient),
            legacy,
            memoryStore,
            mainModelId: "gpt-test",
            consolidationModelId: "gpt-test",
            workspaceRoot: _tempDir);
        var execInvoked = false;
        var execTool = AIFunctionFactory.Create(() =>
        {
            execInvoked = true;
            return "executed";
        }, name: "Exec");
        var snapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "remember blue")],
            new ChatOptions
            {
                ModelId = "gpt-test",
                Tools = [.. CreateFileTools(_tempDir), execTool]
            });

        var result = await consolidator.ConsolidateAsync(
            [new ChatMessage(ChatRole.User, "remember blue")],
            snapshot);

        Assert.Equal(MemoryConsolidationOutcome.Skipped, result.Outcome);
        Assert.Equal("legacy_fallback", result.Message);
        Assert.False(execInvoked);
        Assert.Equal(1, legacy.Calls);
        Assert.Contains(chatClient.FirstOptions?.Tools ?? [], tool => tool.Name == "Exec");
        Assert.Contains(chatClient.FirstOptions?.Tools ?? [], tool => tool.Name == "WriteFile");
    }

    [Fact]
    public async Task ConsolidateAsync_WithDifferentModelFallsBackToLegacy()
    {
        var chatClient = new RecordingChatClient("{}");
        var memoryStore = new MemoryStore(_tempDir);
        var legacy = new FakeMemoryConsolidator(MemoryConsolidationResult.Skipped("legacy_fallback"));
        var consolidator = new MemoryForkConsolidator(
            new MaintenanceForkRunner(chatClient),
            legacy,
            memoryStore,
            mainModelId: "gpt-main",
            consolidationModelId: "gpt-small");
        var snapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "remember blue")],
            new ChatOptions { ModelId = "gpt-main" });

        var result = await consolidator.ConsolidateAsync(
            [new ChatMessage(ChatRole.User, "remember blue")],
            snapshot);

        Assert.Equal(MemoryConsolidationOutcome.Skipped, result.Outcome);
        Assert.Equal("legacy_fallback", result.Message);
        Assert.Equal(1, legacy.Calls);
        Assert.Empty(chatClient.Messages);
    }

    [Fact]
    public async Task ConsolidateAsync_WithInvalidJsonFallsBackToLegacy()
    {
        var chatClient = new RecordingChatClient("not json");
        var memoryStore = new MemoryStore(_tempDir);
        var legacy = new FakeMemoryConsolidator(MemoryConsolidationResult.Skipped("legacy_fallback"));
        var consolidator = new MemoryForkConsolidator(
            new MaintenanceForkRunner(chatClient),
            legacy,
            memoryStore,
            mainModelId: "gpt-test",
            consolidationModelId: "gpt-test",
            workspaceRoot: _tempDir);
        var snapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "remember blue")],
            new ChatOptions { ModelId = "gpt-test" });

        var result = await consolidator.ConsolidateAsync(
            [new ChatMessage(ChatRole.User, "remember blue")],
            snapshot);

        Assert.Equal(MemoryConsolidationOutcome.Skipped, result.Outcome);
        Assert.Equal("legacy_fallback", result.Message);
        Assert.Equal(1, legacy.Calls);
        Assert.NotEmpty(chatClient.Messages);
    }

    [Fact]
    public async Task ConsolidateAsync_WhenForkResultUnavailable_TrimsLegacyFallbackInput()
    {
        var chatClient = new RecordingChatClient("");
        var memoryStore = new MemoryStore(_tempDir);
        var legacy = new FakeMemoryConsolidator(MemoryConsolidationResult.Skipped("legacy_fallback"));
        var consolidator = new MemoryForkConsolidator(
            new MaintenanceForkRunner(chatClient),
            legacy,
            memoryStore,
            mainModelId: "gpt-test",
            consolidationModelId: "gpt-test",
            fallbackInputTokenBudget: 2_000,
            workspaceRoot: _tempDir);
        var messages = new List<ChatMessage>();
        for (var i = 0; i < 12; i++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"user {i} " + new string('u', 1_000)));
            messages.Add(new ChatMessage(ChatRole.Assistant, $"assistant {i} " + new string('a', 1_000)));
        }
        var snapshot = PromptRequestSnapshot.Capture(
            messages,
            new ChatOptions { ModelId = "gpt-test" },
            estimatedInputTokens: 100_000);

        var result = await consolidator.ConsolidateAsync(messages, snapshot);

        Assert.Equal(MemoryConsolidationOutcome.Skipped, result.Outcome);
        Assert.Equal("legacy_fallback", result.Message);
        Assert.Equal(1, legacy.Calls);
        Assert.NotEmpty(chatClient.Messages);
        Assert.True(legacy.LastMessages.Count < messages.Count);
        Assert.NotEmpty(legacy.LastMessages);
    }

    private static List<AITool> CreateFileTools(string workspaceRoot)
    {
        var fileTools = new FileTools(workspaceRoot, requireApprovalOutsideWorkspace: false);
        return
        [
            AIFunctionFactory.Create(fileTools.ReadFile),
            AIFunctionFactory.Create(fileTools.WriteFile),
            AIFunctionFactory.Create(fileTools.EditFile),
            AIFunctionFactory.Create(fileTools.GrepFiles),
            AIFunctionFactory.Create(fileTools.FindFiles)
        ];
    }

    private static EffectiveModelRuntime Runtime(string protocol, string model) =>
        new(
            ProviderId: protocol,
            Model: model,
            Protocol: protocol,
            DisplayName: protocol,
            ApiKey: "test-key",
            EndPoint: "http://localhost",
            NetworkTimeoutSeconds: 60,
            MaxOutputTokens: 64_000,
            IsImplicit: false,
            Capabilities: ModelProviderCapabilities.ForProtocol(protocol));

    private sealed class FakeMemoryConsolidator(MemoryConsolidationResult result) : IMemoryConsolidator
    {
        public int Calls { get; private set; }
        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public Task<MemoryConsolidationResult> ConsolidateAsync(
            IReadOnlyList<ChatMessage> messagesToArchive,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastMessages = messagesToArchive.ToArray();
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingChatClient(string responseText) : IChatClient
    {
        public IReadOnlyList<ChatMessage> Messages { get; private set; } = [];
        public ChatOptions? Options { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Messages = messages.ToArray();
            Options = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Messages = messages.ToArray();
            Options = options;
            yield return new ChatResponseUpdate(ChatRole.Assistant, responseText);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class AnthropicCaptureHandler(string responseText) : HttpMessageHandler
    {
        public string? LastRequestJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""
                    {
                        "id": "msg_memory_test",
                        "type": "message",
                        "role": "assistant",
                        "model": "claude-opus-4-8",
                        "content": [{
                            "type": "text",
                            "text": {{JsonSerializer.Serialize(responseText)}}
                        }],
                        "stop_reason": "end_turn",
                        "usage": {
                            "input_tokens": 10,
                            "output_tokens": 1
                        }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class ToolWritingChatClient(
        string memoryPath,
        string memoryContent,
        string historyPath,
        string historyContent) : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(messages.ToList());
            if (Calls.Count == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, (IList<AIContent>)
                [
                    new FunctionCallContent("call-memory", "WriteFile", new Dictionary<string, object?>
                    {
                        ["path"] = memoryPath,
                        ["content"] = memoryContent
                    }),
                    new FunctionCallContent("call-history", "WriteFile", new Dictionary<string, object?>
                    {
                        ["path"] = historyPath,
                        ["content"] = historyContent
                    })
                ]);
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class DeniedToolChatClient(
        string toolName,
        IDictionary<string, object?> arguments) : IChatClient
    {
        public ChatOptions? FirstOptions { get; private set; }
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(messages.ToList());
            FirstOptions ??= options;
            if (Calls.Count == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, (IList<AIContent>)
                [
                    new FunctionCallContent("call-denied", toolName, new Dictionary<string, object?>(arguments))
                ]);
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
