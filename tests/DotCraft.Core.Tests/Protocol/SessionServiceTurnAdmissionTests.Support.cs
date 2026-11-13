using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceTurnAdmissionTests
{
    private static async Task<List<SessionEvent>> DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        var result = new List<SessionEvent>();
        await foreach (var item in events)
            result.Add(item);
        Assert.DoesNotContain(result, item => item.EventType == SessionEventType.TurnFailed);
        return result;
    }

    private sealed class AdmissionHarness : IAsyncDisposable
    {
        private readonly AgentFactory _factory;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "turn-admission-" + Guid.NewGuid().ToString("N"));
        public RecordingProvider Provider { get; }
        public AppConfig Config { get; }
        public ThreadStore Store { get; }
        public SessionService Service { get; }

        public AdmissionHarness(bool blockRequests = false, AdmissionRefresher? refresher = null, IRolloutWriter? writer = null)
        {
            Directory.CreateDirectory(Root);
            Provider = new(blockRequests);
            Config = AppConfigTestFactory.CreateOpenAI(model: "initial-model", providerId: "source");
            Config.InstantInterruptEnabled = false;
            Config.Providers["target"] = new AppConfig.ModelProviderConfig
            {
                Protocol = ModelProviderProtocols.OpenAI,
                ApiKey = "unused-key",
                EndPoint = "https://127.0.0.1:9/v1"
            };
            Config.ProviderPreferences["target"] = new()
            {
                Model = "target-model",
                Reasoning = new() { Enabled = false, Effort = ModelReasoningEffort.Low },
                Speed = InferenceSpeed.Fast
            };
            _factory = new AgentFactory(Root, Root, Config, new MemoryStore(Root), new SkillsLoader(Root),
                new AutoApproveApprovalService(), blacklist: null, toolSources: [],
                chatClientRegistry: new ChatClientRegistry(Provider));
            Store = writer is null ? new ThreadStore(Root) : new ThreadStore(Root, null, null, writer);
            Service = new SessionService(_factory, _factory.CreateDefaultAgent(),
                new SessionPersistenceService(Store), new SessionGate(),
                runtimeRefreshers: refresher is null ? null : [refresher]);
        }

        public Task<SessionThread> CreateThreadAsync(ThreadConfiguration? configuration = null) =>
            Service.CreateThreadAsync(new SessionIdentity
            {
                ChannelName = "test", UserId = "user", WorkspacePath = Root
            }, configuration);

        public async ValueTask DisposeAsync()
        {
            Provider.Release.TrySetResult();
            await Store.DisposeAsync();
            await _factory.DisposeAsync();
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class RecordingProvider(bool blockRequests) : IModelProvider
    {
        public ConcurrentQueue<EffectiveModelRuntime> Requests { get; } = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyCollection<string> Protocols => [ModelProviderProtocols.OpenAIChatCompletions];
        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => new Client(this, runtime, blockRequests);

        private sealed class Client(RecordingProvider owner, EffectiveModelRuntime runtime, bool block) : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages, ChatOptions? options = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                owner.Requests.Enqueue(runtime);
                owner.Entered.TrySetResult();
                if (block)
                    await owner.Release.Task.WaitAsync(cancellationToken);
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]);
            }

            public object? GetService(Type type, object? serviceKey = null) => null;
            public void Dispose() { }
        }
    }

    private sealed class AdmissionWriter : IRolloutWriter
    {
        private readonly OrderedRolloutWriter _inner = new();
        public string? RejectModel { get; set; }

        public Task AddBatchAsync(string threadId, string path, IReadOnlyList<ThreadRolloutRecord> records,
            CancellationToken ct = default)
        {
            if (RejectModel is { } model && records.Any(record => record.ThreadOpened?.Configuration?.Model == model))
                throw new RolloutPersistenceException("configuration commit rejected", new IOException("test failure"));
            return _inner.AddBatchAsync(threadId, path, records, ct);
        }

        public Task<RolloutWriteReceipt> FlushAsync(string threadId, CancellationToken ct = default) => _inner.FlushAsync(threadId, ct);
        public Task CloseAsync(string threadId, CancellationToken ct = default) => _inner.CloseAsync(threadId, ct);
        public Task ShutdownAllAsync(CancellationToken ct = default) => _inner.ShutdownAllAsync(ct);
    }

    private sealed class AdmissionRefresher : ISessionRuntimeRefresher
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task RefreshAsync(CancellationToken ct)
        {
            Entered.TrySetResult();
            return Release.Task.WaitAsync(ct);
        }
    }
}
