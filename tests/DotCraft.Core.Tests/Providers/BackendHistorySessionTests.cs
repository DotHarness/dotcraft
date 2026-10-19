using System.Runtime.CompilerServices;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Contributions;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Providers;

public sealed class BackendHistorySessionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backend-history-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CustomProtocolHistorySurvivesResumeForkAndRollbackAndIsNotReusedByAnotherBackend()
    {
        Directory.CreateDirectory(_root);
        var backend = new StatefulBackend("alpha");
        var second = new StatefulBackend("beta");
        var contributions = new ContributionRegistry();
        contributions.Add<IModelBackendSource>(backend);
        contributions.Add<IModelBackendSource>(second);
        using var resolver = new ModelBackendResolver(new ModelProviderRegistry([]), contributions, _root);
        var clients = new ChatClientRegistry(new ModelProviderRegistry([]), resolver);
        var config = new AppConfig
        {
            ProviderId = "demo",
            ProviderPreferences = new() { ["demo"] = new() { Model = "model" } },
            Providers = new() { ["demo"] = new() { BackendId = "alpha" } }
        };
        await using var factory = new AgentFactory(_root, _root, config, new MemoryStore(_root), new SkillsLoader(_root),
            new AutoApproveApprovalService(), null, chatClientRegistry: clients, chatClient: backend);
        SessionService CreateService() => new(factory, new StreamingFunctionInvokingChatClient(backend).AsAIAgent(),
            new SessionPersistenceService(new ThreadStore(_root)), new SessionGate());
        var service = CreateService();
        var thread = await service.CreateThreadAsync(new() { ChannelName = "test", UserId = "user", WorkspacePath = _root });
        await Drain(service.SubmitInputAsync(thread.Id, [new TextContent("first")]));
        Assert.Equal("alpha", backend.Sessions[^1].CaptureOpaqueSnapshot().Identity.BackendId);
        Assert.Contains(backend.Sessions[^1].Items, item => item.Payload.GetRawText().Contains("first"));
        service = CreateService();
        await service.GetThreadAsync(thread.Id);
        await Drain(service.SubmitInputAsync(thread.Id, [new TextContent("second")]));
        Assert.Contains(backend.Sessions[^1].Initial.Items, item => item.Payload.GetRawText().Contains("first"));
        var fork = await service.ForkThreadAsync(thread.Id);
        await Drain(service.SubmitInputAsync(fork.Id, [new TextContent("branch")]));
        Assert.Contains(backend.Sessions[^1].Initial.Items, item => item.Payload.GetRawText().Contains("second"));
        await service.RollbackThreadAsync(fork.Id, 1);
        await Drain(service.SubmitInputAsync(fork.Id, [new TextContent("after-rollback")]));
        Assert.DoesNotContain(backend.Sessions[^1].Initial.Items, item => item.Payload.GetRawText().Contains("branch"));
        var compacted = await service.CompactThreadAsync(fork.Id);
        Assert.Equal("partial", compacted.Outcome);
        await Drain(service.SubmitInputAsync(fork.Id, [new TextContent("after-compaction")]));
        Assert.Contains(backend.Sessions[^1].Initial.Items, item => item.Payload.GetRawText().Contains("native-compacted"));
        config.Providers["demo"].BackendId = "beta";
        service = CreateService();
        await service.GetThreadAsync(thread.Id);
        await Drain(service.SubmitInputAsync(thread.Id, [new TextContent("different-backend")]));
        Assert.Empty(second.Sessions[^1].Initial.Items);
        Assert.Contains(second.Sessions[^1].Items, item => item.Payload.GetRawText().Contains("first"));
        var snapshot = await new SessionPersistenceService(new ThreadStore(_root)).LoadProviderHistoryAsync(
            (await service.GetThreadAsync(thread.Id))!, "window", default);
        Assert.Equal("beta", snapshot.BackendId);
        Assert.Equal("custom-stateful", snapshot.Protocol);
    }

    private static async Task Drain(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var item in events) Assert.True(item.EventType != SessionEventType.TurnFailed, item.TurnFailedPayload?.Error);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
        catch (IOException) { }
    }

    private sealed class StatefulBackend(string id) : IModelBackendSource, IModelBackend, IModelProvider,
        IProviderHistorySessionFactory, IProviderNativeCompactorFactory, IProviderNativeCompactor, IChatClient
    {
        public List<History> Sessions { get; } = [];
        public IModelBackend Backend => this;
        public ModelBackendDescriptor Descriptor => new(id, id, "custom-stateful", ["custom-stateful"], [], new());
        public IReadOnlyCollection<string> Protocols => ["custom-stateful"];
        public IModelProvider CreateProvider(ModelBackendContext context) => this;
        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => this;
        public IProviderNativeCompactor CreateCompactor(EffectiveModelRuntime runtime, IChatClient? client = null) => this;
        public Task<ProviderNativeCompactionReplacement> CompactAsync(ProviderNativeCompactionInput input,
            IReadOnlyList<ChatMessage> history, ChatOptions? options, CancellationToken ct) =>
            Task.FromResult(new ProviderNativeCompactionReplacement("custom-stateful",
                [new("compacted", JsonSerializer.SerializeToElement(new { text = "native-compacted" }))],
                input.CoveredMessageCount, input.CoveredThroughTurnId, 100));
        public object? GetService(Type type, object? key = null) => type.IsInstanceOfType(this) ? this : null;
        public IProviderConversationHistory CreateSession(ProviderConversationIdentity identity, OpaqueProviderHistorySnapshot snapshot,
            IReadOnlyList<ChatMessage> coveredMessages, IProviderHistorySink? sink)
        {
            var history = new History(snapshot with { Identity = snapshot.Identity with { TurnId = identity.TurnId } }, sink);
            Sessions.Add(history);
            return history;
        }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "summary")));
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var history = Assert.IsType<History>(ProviderRequestContextScope.Current?.History);
            await history.AppendLocalInputAsync(messages.ToArray(), cancellationToken);
            yield return new(ChatRole.Assistant, "ok") { FinishReason = ChatFinishReason.Stop };
        }
        public void Dispose() { }
    }

    private sealed class History(OpaqueProviderHistorySnapshot snapshot, IProviderHistorySink? sink)
        : IProviderConversationHistory, IProviderCompactionBridge
    {
        public OpaqueProviderHistorySnapshot Initial { get; } = snapshot;
        public List<ProviderHistoryItem> Items { get; } = snapshot.Items.ToList();
        private ProviderHistoryIdentity _identity = snapshot.Identity;
        private string? _covered = snapshot.CoveredThroughTurnId;
        public async ValueTask AppendLocalInputAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct)
        {
            var items = messages.Select(message => new ProviderHistoryItem(Guid.NewGuid().ToString("N"),
                JsonSerializer.SerializeToElement(new { text = message.Text }))).ToArray();
            Items.AddRange(items);
            if (sink is not null) await sink.AppendAsync(_identity, ProviderHistoryEntrySource.LocalInput, null, items, ct);
            _covered = _identity.TurnId;
        }
        public async ValueTask HistoryReplacedAsync(IReadOnlyList<ChatMessage> messages, ChatOptions? options, string reason,
            CancellationToken ct, string? coveredThroughTurnId = null)
        {
            _identity = _identity with { GenerationId = Guid.NewGuid().ToString("N") };
            Items.Clear();
            Items.AddRange(messages.Select(message => new ProviderHistoryItem(Guid.NewGuid().ToString("N"),
                JsonSerializer.SerializeToElement(new { text = message.Text }))));
            _covered = coveredThroughTurnId ?? _identity.TurnId;
            if (sink is not null) await sink.ReplaceAsync(_identity, _covered, reason, Items, ct);
        }
        public void MarkProjectionCovered(IReadOnlyList<ChatMessage> messages) { }
        public string? BeginAttempt() => null;
        public ValueTask AbortAttemptAsync(string? attemptId, CancellationToken ct) => ValueTask.CompletedTask;
        public void EndAttempt(string? attemptId) { }
        public OpaqueProviderHistorySnapshot CaptureOpaqueSnapshot() => new(_identity, Items.ToArray(), _covered);
        public ValueTask<ProviderNativeCompactionInput> CaptureInputAsync(ProviderCompactionPhase phase,
            IReadOnlyList<ChatMessage> messages, ChatOptions? options, CancellationToken ct) =>
            ValueTask.FromResult(new ProviderNativeCompactionInput(Items.ToArray(), messages.Count, _covered));
        public async ValueTask ReplaceAsync(ProviderNativeCompactionReplacement replacement, CancellationToken ct)
        {
            _identity = _identity with { GenerationId = Guid.NewGuid().ToString("N") };
            Items.Clear();
            Items.AddRange(replacement.Items);
            _covered = replacement.CoveredThroughTurnId;
            if (sink is not null) await sink.ReplaceAsync(_identity, _covered, "remote_compaction", Items, ct);
        }
        public long EstimateContextTokens(ProviderNativeCompactionInput snapshot, IReadOnlyList<ChatMessage> tail, ChatOptions? options) => 100;
    }
}
