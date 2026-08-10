using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class ThreadRecoveryTests : IAsyncLifetime
{
    private readonly string _workspacePath = Path.Combine(
        Path.GetTempPath(),
        "ThreadRecoveryTests_" + Guid.NewGuid().ToString("N")[..8]);
    private string CraftPath => Path.Combine(_workspacePath, ".craft");
    private ThreadStore Store { get; set; } = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_workspacePath);
        Store = new ThreadStore(CraftPath);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await Store.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_workspacePath))
                Directory.Delete(_workspacePath, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temporary files.
        }
    }

    [Fact]
    public async Task ExportDeleteRestore_PreservesExecutableSessionAndProvider_ThenRunsNextTurn()
    {
        var chatClient = new RecordingChatClient("first answer");
        await using var agentFactory = CreateAgentFactory(_workspacePath, CraftPath);
        var persistence = new SessionPersistenceService(Store);
        var service = CreateService(agentFactory, chatClient, persistence);
        var thread = await service.CreateThreadAsync(CreateIdentity(_workspacePath));
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("first request")]));
        var terminalTurn = thread.Turns[^1];

        var missingSpillReference = $".craft/tool-results/{thread.Id}/missing.txt";
        var preview = $"tool output preview (full output at: {missingSpillReference})";
        Assert.False(File.Exists(Path.Combine(_workspacePath, missingSpillReference)));
        var currentSession = new List<ChatMessage>
        {
            new(ChatRole.User, "RECOVERY_SUMMARY"),
            new(ChatRole.Assistant,
            [
                new FunctionCallContent("call_recovery", "ReadFile", new Dictionary<string, object?>
                {
                    ["path"] = "large.txt"
                })
            ]),
            new(ChatRole.Tool,
            [
                new FunctionResultContent("call_recovery", preview),
                new DataContent(new byte[] { 1, 2, 3, 4, 5 }, "image/png") { Name = "inline.png" }
            ]),
            new(ChatRole.Assistant, "summary answer")
        };
        await Store.AppendCompactionCheckpointAsync(
            thread.Id,
            terminalTurn.Id,
            currentSession,
            trigger: "manual",
            mode: "partial",
            tokensBefore: 10_000,
            tokensAfter: 100);

        var contextWindow = persistence.GetOrCreateResponsesContextWindow(thread.Id);
        await Store.ReplaceProviderHistoryAsync(new ProviderHistoryReplacedPayload
        {
            SchemaVersion = ProviderHistorySchema.CurrentSchemaVersion,
            ThreadId = thread.Id,
            Protocol = ProviderHistorySchema.OpenAIResponsesProtocol,
            GenerationId = "generation_recovery",
            ContextWindowId = contextWindow.CurrentWindowId,
            CoveredThroughTurnId = terminalTurn.Id,
            Reason = ProviderHistoryReasons.RemoteCompaction,
            Entries =
            [
                new ProviderHistoryEntry
                {
                    EntryId = "provider_item_1",
                    Item = JsonSerializer.SerializeToElement(new { type = "message", id = "provider_item_1" })
                }
            ]
        });

        var expectedHistory = await Store.LoadModelHistoryAsync(thread.Id);
        var expectedProvider = await Store.LoadProviderHistoryAsync(thread, contextWindow.CurrentWindowId);
        var package = await service.ExportThreadRecoveryAsync(thread.Id);

        Assert.Equal(terminalTurn.Id, package.TerminalTurnId);
        Assert.Equal(1, package.FormatVersion);
        Assert.True(package.ByteLength > 0);
        Assert.Equal(64, package.Sha256.Length);
        Assert.Equal($"{thread.Id}.json", Path.GetFileName(package.PackagePath));
        await using (var snapshotStream = File.OpenRead(package.PackagePath))
        {
            using var snapshot = await JsonDocument.ParseAsync(snapshotStream);
            Assert.Equal(JsonValueKind.Object, snapshot.RootElement.ValueKind);
        }

        await service.DeleteThreadPermanentlyAsync(thread.Id);
        Assert.Null(await Store.LoadThreadAsync(thread.Id));

        var restoredId = await service.RestoreThreadRecoveryAsync(package.PackagePath, thread.Id);
        Assert.Equal(thread.Id, restoredId);
        var restored = (await Store.LoadThreadAsync(thread.Id))!;
        Assert.Single(restored.Turns);
        Assert.Equal(terminalTurn.Id, restored.Turns[0].Id);
        Assert.Empty(restored.Turns[0].Items);
        Assert.Null(restored.Turns[0].Input);
        Assert.Equal(1, restored.TurnSequenceHighWatermark);
        Assert.Equal(EncodeHistory(expectedHistory), EncodeHistory(await Store.LoadModelHistoryAsync(thread.Id)));

        var restoredContext = persistence.GetOrCreateResponsesContextWindow(thread.Id);
        var restoredProvider = await Store.LoadProviderHistoryAsync(restored, restoredContext.CurrentWindowId);
        Assert.Equal(expectedProvider.GenerationId, restoredProvider.GenerationId);
        Assert.Equal(expectedProvider.ContextWindowId, restoredProvider.ContextWindowId);
        Assert.Equal(expectedProvider.IsNativeCompacted, restoredProvider.IsNativeCompacted);
        Assert.Equal(expectedProvider.Entries[0].Item.GetRawText(), restoredProvider.Entries[0].Item.GetRawText());

        await service.ResumeThreadAsync(thread.Id);
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("follow up")]));
        Assert.Equal(EncodeHistory(expectedHistory), EncodeHistory(chatClient.LastMessages.Take(expectedHistory.Count)));
        Assert.Contains(chatClient.LastMessages, message => MessageText(message).Contains("follow up", StringComparison.Ordinal));
        Assert.Equal(
            ["turn_001", "turn_002"],
            (await Store.LoadThreadAsync(thread.Id))!.Turns.Select(static turn => turn.Id).ToArray());
    }

    [Theory]
    [InlineData(TurnStatus.Completed)]
    [InlineData(TurnStatus.Failed)]
    [InlineData(TurnStatus.Cancelled)]
    public async Task Export_AllTerminalTurnStatuses_AreAccepted(TurnStatus status)
    {
        await using var agentFactory = CreateAgentFactory(_workspacePath, CraftPath);
        var service = CreateService(agentFactory, new RecordingChatClient("unused"), new SessionPersistenceService(Store));
        var thread = await service.CreateThreadAsync(CreateIdentity(_workspacePath));
        thread.Turns.Add(CreateTurn(thread.Id, "turn_001", status));
        await Store.SaveThreadAsync(thread);

        var package = await service.ExportThreadRecoveryAsync(thread.Id);

        Assert.True(File.Exists(package.PackagePath));
        Assert.Equal("turn_001", package.TerminalTurnId);
    }

    [Fact]
    public async Task Export_RunningThread_IsRejected()
    {
        await using var agentFactory = CreateAgentFactory(_workspacePath, CraftPath);
        var service = CreateService(agentFactory, new RecordingChatClient("unused"), new SessionPersistenceService(Store));
        var thread = await service.CreateThreadAsync(CreateIdentity(_workspacePath));
        thread.Turns.Add(CreateTurn(thread.Id, "turn_001", TurnStatus.Running));
        await Store.SaveThreadAsync(thread);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportThreadRecoveryAsync(thread.Id));
    }

    [Fact]
    public async Task Export_UnloadedThread_IsLoadedIntoRuntimeBeforeMaintenance()
    {
        await using var firstFactory = CreateAgentFactory(_workspacePath, CraftPath);
        var firstService = CreateService(firstFactory, new RecordingChatClient("unused"), new SessionPersistenceService(Store));
        var thread = await firstService.CreateThreadAsync(CreateIdentity(_workspacePath));
        thread.Turns.Add(CreateTurn(thread.Id, "turn_001", TurnStatus.Completed));
        await Store.SaveThreadAsync(thread);

        await using var coldFactory = CreateAgentFactory(_workspacePath, CraftPath);
        var coldService = CreateService(coldFactory, new RecordingChatClient("unused"), new SessionPersistenceService(Store));
        var package = await coldService.ExportThreadRecoveryAsync(thread.Id);

        Assert.Equal("turn_001", package.TerminalTurnId);
    }

    [Fact]
    public async Task LoadThread_WaitsForRecoveryCommitGate()
    {
        await using var agentFactory = CreateAgentFactory(_workspacePath, CraftPath);
        var service = CreateService(agentFactory, new RecordingChatClient("unused"), new SessionPersistenceService(Store));
        var thread = await service.CreateThreadAsync(CreateIdentity(_workspacePath));
        thread.Turns.Add(CreateTurn(thread.Id, "turn_001", TurnStatus.Completed));
        await Store.SaveThreadAsync(thread);

        using (await ThreadRolloutWriteGate.AcquireAsync(CraftPath, thread.Id))
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Store.LoadThreadAsync(thread.Id, cancellation.Token));
        }

        Assert.NotNull(await Store.LoadThreadAsync(thread.Id));
    }

    [Fact]
    public async Task Resume_CancelledDuringRecoveryCommit_DoesNotCacheRolledBackThread()
    {
        await using var firstFactory = CreateAgentFactory(_workspacePath, CraftPath);
        var firstService = CreateService(firstFactory, new RecordingChatClient("unused"), new SessionPersistenceService(Store));
        var thread = await firstService.CreateThreadAsync(CreateIdentity(_workspacePath));
        thread.Turns.Add(CreateTurn(thread.Id, "turn_001", TurnStatus.Completed));
        await Store.SaveThreadAsync(thread);

        await using var coldFactory = CreateAgentFactory(_workspacePath, CraftPath);
        var coldService = CreateService(coldFactory, new RecordingChatClient("unused"), new SessionPersistenceService(Store));
        using (await ThreadRolloutWriteGate.AcquireAsync(CraftPath, thread.Id))
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                coldService.ResumeThreadAsync(thread.Id, cancellation.Token));
        }

        Store.DeleteThread(thread.Id);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => coldService.ResumeThreadAsync(thread.Id));
    }

    [Fact]
    public async Task Restore_AfterRollback_UsesSurvivingSessionAndPreservesSequenceHighWatermark()
    {
        var chatClient = new RecordingChatClient("follow-up answer");
        await using var agentFactory = CreateAgentFactory(_workspacePath, CraftPath);
        var service = CreateService(agentFactory, chatClient, new SessionPersistenceService(Store));
        var thread = await service.CreateThreadAsync(CreateIdentity(_workspacePath));
        thread.Turns.Add(CreateTurn(thread.Id, "turn_001", TurnStatus.Completed));
        thread.Turns.Add(CreateTurn(thread.Id, "turn_002", TurnStatus.Completed));
        thread.TurnSequenceHighWatermark = 2;
        await Store.SaveThreadAsync(thread);

        await Store.AppendModelHistoryAsync(
            thread.Id,
            [new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_1", "surviving output")])],
            "turn_001");
        await Store.AppendModelHistoryAsync(
            thread.Id,
            [new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_2", "ROLLBACK_SECRET")])],
            "turn_002");
        thread.Turns.RemoveAt(1);
        await Store.RollbackThreadAsync(thread, 1);

        var package = await service.ExportThreadRecoveryAsync(thread.Id);
        Assert.DoesNotContain(
            "ROLLBACK_SECRET",
            await File.ReadAllTextAsync(package.PackagePath),
            StringComparison.Ordinal);

        await service.DeleteThreadPermanentlyAsync(thread.Id);
        await service.RestoreThreadRecoveryAsync(package.PackagePath, thread.Id);
        var restored = (await Store.LoadThreadAsync(thread.Id))!;
        Assert.Single(restored.Turns);
        Assert.Equal("turn_001", restored.Turns[0].Id);
        Assert.Equal(2, restored.TurnSequenceHighWatermark);
        await service.ResumeThreadAsync(thread.Id);
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("continue after rollback")]));
        Assert.Equal(
            ["turn_001", "turn_003"],
            (await Store.LoadThreadAsync(thread.Id))!.Turns.Select(static turn => turn.Id).ToArray());
    }

    [Fact]
    public async Task Restore_TamperedSnapshot_IsRejectedWithoutPartialThread()
    {
        await using var agentFactory = CreateAgentFactory(_workspacePath, CraftPath);
        var service = CreateService(agentFactory, new RecordingChatClient("unused"), new SessionPersistenceService(Store));
        var (thread, package) = await SeedExportAndDeleteAsync(service);
        await File.WriteAllTextAsync(package.PackagePath, "{");

        var exception = await Assert.ThrowsAsync<ThreadRecoveryException>(() =>
            service.RestoreThreadRecoveryAsync(package.PackagePath, thread.Id));

        Assert.Equal(ThreadRecoveryErrorCodes.PackageInvalid, exception.Code);
        Assert.Null(await Store.LoadThreadAsync(thread.Id));
    }

    [Theory]
    [InlineData("thread")]
    [InlineData("threadId")]
    [InlineData("workspacePath")]
    [InlineData("originChannel")]
    [InlineData("source")]
    [InlineData("sourceKind")]
    [InlineData("sourceShape")]
    [InlineData("metadata")]
    [InlineData("metadataValue")]
    [InlineData("terminalTurn")]
    [InlineData("terminalTurnId")]
    [InlineData("modelHistory")]
    [InlineData("modelHistoryElement")]
    [InlineData("providerHistory")]
    [InlineData("providerGeneration")]
    [InlineData("providerContextWindow")]
    [InlineData("providerEntries")]
    [InlineData("providerEntryElement")]
    [InlineData("providerEntryId")]
    [InlineData("providerEntryItem")]
    public async Task Restore_ExplicitNullRequiredMembers_ArePackageInvalid(string member)
    {
        await using var agentFactory = CreateAgentFactory(_workspacePath, CraftPath);
        var service = CreateService(agentFactory, new RecordingChatClient("unused"), new SessionPersistenceService(Store));
        var (thread, package) = await SeedExportAndDeleteAsync(service);
        UpdateSnapshot(package.PackagePath, snapshot => SetRequiredMemberToNull(snapshot, member));

        var exception = await Assert.ThrowsAsync<ThreadRecoveryException>(() =>
            service.RestoreThreadRecoveryAsync(package.PackagePath, thread.Id));

        Assert.Equal(ThreadRecoveryErrorCodes.PackageInvalid, exception.Code);
        Assert.Null(await Store.LoadThreadAsync(thread.Id));
    }

    [Fact]
    public async Task Restore_UnsupportedFormatAndModelSchema_AreRejectedBeforeInstallation()
    {
        await using var agentFactory = CreateAgentFactory(_workspacePath, CraftPath);
        var service = CreateService(agentFactory, new RecordingChatClient("unused"), new SessionPersistenceService(Store));
        var (thread, package) = await SeedExportAndDeleteAsync(service);
        UpdateSnapshot(package.PackagePath, snapshot => snapshot["formatVersion"] = 999);

        var formatError = await Assert.ThrowsAsync<ThreadRecoveryException>(() =>
            service.RestoreThreadRecoveryAsync(package.PackagePath, thread.Id));
        Assert.Equal(ThreadRecoveryErrorCodes.PackageIncompatible, formatError.Code);
        Assert.Null(await Store.LoadThreadAsync(thread.Id));

        UpdateSnapshot(package.PackagePath, snapshot =>
        {
            snapshot["formatVersion"] = 1;
            snapshot["modelHistory"]![0]!["schemaVersion"] = 999;
        });
        var schemaError = await Assert.ThrowsAsync<ThreadRecoveryException>(() =>
            service.RestoreThreadRecoveryAsync(package.PackagePath, thread.Id));
        Assert.Equal(ThreadRecoveryErrorCodes.PackageIncompatible, schemaError.Code);
        Assert.Null(await Store.LoadThreadAsync(thread.Id));
    }

    [Fact]
    public async Task Restore_ExistingTarget_IsNeverOverwritten()
    {
        await using var agentFactory = CreateAgentFactory(_workspacePath, CraftPath);
        var service = CreateService(agentFactory, new RecordingChatClient("unused"), new SessionPersistenceService(Store));
        var thread = await service.CreateThreadAsync(CreateIdentity(_workspacePath));
        thread.Turns.Add(CreateTurn(thread.Id, "turn_001", TurnStatus.Completed));
        await Store.SaveThreadAsync(thread);
        var package = await service.ExportThreadRecoveryAsync(thread.Id);

        var exception = await Assert.ThrowsAsync<ThreadRecoveryException>(() =>
            service.RestoreThreadRecoveryAsync(package.PackagePath, thread.Id));

        Assert.Equal(ThreadRecoveryErrorCodes.TargetExists, exception.Code);
        Assert.Single((await Store.LoadThreadAsync(thread.Id))!.Turns);
    }

    [Fact]
    public async Task Restore_DifferentWorkspace_IsRejectedWithoutInstallingThread()
    {
        await using var agentFactory = CreateAgentFactory(_workspacePath, CraftPath);
        var service = CreateService(agentFactory, new RecordingChatClient("unused"), new SessionPersistenceService(Store));
        var thread = await service.CreateThreadAsync(CreateIdentity(_workspacePath));
        thread.Turns.Add(CreateTurn(thread.Id, "turn_001", TurnStatus.Completed));
        await Store.SaveThreadAsync(thread);
        var package = await service.ExportThreadRecoveryAsync(thread.Id);

        var otherWorkspace = Path.Combine(Path.GetTempPath(), "ThreadRecoveryOther_" + Guid.NewGuid().ToString("N")[..8]);
        var otherCraft = Path.Combine(otherWorkspace, ".craft");
        Directory.CreateDirectory(Path.Combine(otherCraft, "recovery-staging"));
        var copiedPackage = Path.Combine(otherCraft, "recovery-staging", Path.GetFileName(package.PackagePath));
        File.Copy(package.PackagePath, copiedPackage);
        var otherStore = new ThreadStore(otherCraft);
        try
        {
            await using var otherFactory = CreateAgentFactory(otherWorkspace, otherCraft);
            var otherService = CreateService(otherFactory, new RecordingChatClient("unused"), new SessionPersistenceService(otherStore));
            var exception = await Assert.ThrowsAsync<ThreadRecoveryException>(() =>
                otherService.RestoreThreadRecoveryAsync(copiedPackage, thread.Id));
            Assert.Equal(ThreadRecoveryErrorCodes.WorkspaceMismatch, exception.Code);
            Assert.Null(await otherStore.LoadThreadAsync(thread.Id));
        }
        finally
        {
            await otherStore.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(otherWorkspace, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    private async Task<(SessionThread Thread, ThreadRecoveryPackage Package)> SeedExportAndDeleteAsync(
        SessionService service)
    {
        var thread = await service.CreateThreadAsync(CreateIdentity(_workspacePath));
        thread.Turns.Add(CreateTurn(thread.Id, "turn_001", TurnStatus.Completed));
        await Store.SaveThreadAsync(thread);
        await Store.AppendModelHistoryAsync(
            thread.Id,
            [
                new ChatMessage(ChatRole.User, "request"),
                new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_tamper", "tool result")])
            ],
            "turn_001");
        var package = await service.ExportThreadRecoveryAsync(thread.Id);
        await service.DeleteThreadPermanentlyAsync(thread.Id);
        return (thread, package);
    }

    private AgentFactory CreateAgentFactory(string workspacePath, string craftPath)
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        return new AgentFactory(
            dotcraftPath: craftPath,
            workspacePath: workspacePath,
            config: config,
            memoryStore: new MemoryStore(craftPath),
            skillsLoader: new SkillsLoader(craftPath),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            toolSources: Array.Empty<IToolSource>());
    }

    private static SessionService CreateService(
        AgentFactory agentFactory,
        IChatClient chatClient,
        SessionPersistenceService persistence) =>
        new(agentFactory, chatClient.AsAIAgent(), persistence, new SessionGate());

    private static SessionIdentity CreateIdentity(string workspacePath) => new()
    {
        ChannelName = "recovery-test",
        UserId = "test-user",
        WorkspacePath = workspacePath
    };

    private static SessionTurn CreateTurn(string threadId, string turnId, TurnStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var input = new SessionItem
        {
            Id = $"item_{turnId}",
            TurnId = turnId,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new UserMessagePayload { Text = "request" }
        };
        return new SessionTurn
        {
            Id = turnId,
            ThreadId = threadId,
            Status = status,
            StartedAt = now,
            CompletedAt = status == TurnStatus.Running ? null : now,
            Input = input,
            Items = [input]
        };
    }

    private static string EncodeHistory(IEnumerable<ChatMessage> history)
    {
        var codec = new ModelHistoryCodec();
        return JsonSerializer.Serialize(history.Select(message => codec.Encode(message)), SessionJsonOptions.Default);
    }

    private static string MessageText(ChatMessage message) =>
        string.Concat(message.Contents.OfType<TextContent>().Select(static content => content.Text));

    private static void UpdateSnapshot(string packagePath, Action<JsonObject> update)
    {
        var snapshot = JsonNode.Parse(File.ReadAllText(packagePath))!.AsObject();
        update(snapshot);
        File.WriteAllText(packagePath, snapshot.ToJsonString());
    }

    private static void SetRequiredMemberToNull(JsonObject snapshot, string member)
    {
        var thread = snapshot["thread"]!.AsObject();
        var terminalTurn = snapshot["terminalTurn"]!.AsObject();
        var providerHistory = snapshot["providerHistory"]!.AsObject();
        switch (member)
        {
            case "thread":
                snapshot["thread"] = null;
                break;
            case "threadId":
                thread["threadId"] = null;
                break;
            case "workspacePath":
                thread["workspacePath"] = null;
                break;
            case "originChannel":
                thread["originChannel"] = null;
                break;
            case "source":
                thread["source"] = null;
                break;
            case "sourceKind":
                thread["source"]!["kind"] = null;
                break;
            case "sourceShape":
                thread["source"]!["subAgent"] = new JsonObject();
                break;
            case "metadata":
                thread["metadata"] = null;
                break;
            case "metadataValue":
                thread["metadata"]!.AsObject()["required"] = null;
                break;
            case "terminalTurn":
                snapshot["terminalTurn"] = null;
                break;
            case "terminalTurnId":
                terminalTurn["turnId"] = null;
                break;
            case "modelHistory":
                snapshot["modelHistory"] = null;
                break;
            case "modelHistoryElement":
                snapshot["modelHistory"]!.AsArray()[0] = null;
                break;
            case "providerHistory":
                snapshot["providerHistory"] = null;
                break;
            case "providerGeneration":
                providerHistory["generationId"] = null;
                break;
            case "providerContextWindow":
                providerHistory["contextWindowId"] = null;
                break;
            case "providerEntries":
                providerHistory["entries"] = null;
                break;
            case "providerEntryElement":
                providerHistory["entries"] = new JsonArray((JsonNode?)null);
                break;
            case "providerEntryId":
                providerHistory["entries"] = new JsonArray(new JsonObject
                {
                    ["entryId"] = null,
                    ["item"] = new JsonObject()
                });
                break;
            case "providerEntryItem":
                providerHistory["entries"] = new JsonArray(new JsonObject
                {
                    ["entryId"] = "provider_item",
                    ["item"] = null
                });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(member), member, null);
        }
    }

    private static async Task DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private sealed class RecordingChatClient(string responseText) : IChatClient
    {
        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = chatMessages.ToList();
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, responseText)]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = chatMessages.ToList();
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(responseText)]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
