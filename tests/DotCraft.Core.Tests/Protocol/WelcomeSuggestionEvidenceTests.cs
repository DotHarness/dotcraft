using System.Runtime.CompilerServices;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Persistence;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

[Collection(WelcomeSuggestionServiceTestCollection.Name)]
public sealed class WelcomeSuggestionEvidenceTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "welcome-evidence-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Refresh_SuppliesCapturedMemoryToRealSessionAndCachesThatSnapshot()
    {
        var dataPath = Path.Combine(_workspace, ".craft");
        Directory.CreateDirectory(dataPath);
        var memory = new MemoryStore(dataPath);
        const string memoryText = "User preference: focus on retry behavior in QueueWorker.cs.";
        WriteMemory(memory, memoryText);

        var client = new SuggestionClient();
        var config = AppConfigTestFactory.CreateOpenAI();
        config.WelcomeSuggestions.Enabled = true;
        config.Memory.Enabled = true;
        using var database = new WorkspaceStateDatabase(dataPath);
        var persistence = new SessionPersistenceService(new ThreadStore(dataPath, database));
        var recorder = new ToolInvocationRecorderRouter();
        var profiles = new ToolProfileRegistry();
        profiles.Register(WelcomeSuggestionConstants.ToolProfileName, [new WelcomeSuggestionToolSource()]);
        await using var factory = new AgentFactory(dataPath, _workspace, config, memory, new SkillsLoader(dataPath),
            new AutoApproveApprovalService(), null, chatClientRegistry: TestModelProviderRegistry.Create(),
            chatClient: client, toolSources: [], toolDispatcher: new ToolDispatcher(recorder: recorder));
        var sessions = new SessionService(factory, factory.CreateAgentForMode(AgentMode.Agent), persistence,
            new SessionGate(), toolProfileRegistry: profiles);
        recorder.Bind(sessions);
        var identity = new SessionIdentity { ChannelName = "test", UserId = "user", WorkspacePath = _workspace };

        WelcomeSuggestionService CreateService() => new(sessions, memory, _workspace, config, dataPath);
        async Task<WelcomeSuggestionSnapshot> GenerateAsync(bool clearCache = false)
        {
            await using var service = CreateService();
            if (clearCache)
                service.ClearWorkspaceCache(_workspace);
            service.ScheduleRefresh(_workspace);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                var snapshot = await service.SuggestAsync(new WelcomeSuggestionRequest { Identity = identity, MaxItems = 4 });
                if (snapshot.Source == "dynamic")
                    return snapshot;
                await Task.Delay(25, timeout.Token);
            }
        }

        var threadsDirectory = Path.Combine(dataPath, "threads");
        var threadFilesDuringRequest = -1;
        client.OnRequest = () =>
        {
            threadFilesDuringRequest = Directory.Exists(threadsDirectory)
                ? Directory.EnumerateFiles(threadsDirectory, "*", SearchOption.AllDirectories).Count()
                : 0;
            WriteMemory(memory, "Changed memory after request capture.");
        };
        var first = await GenerateAsync();
        var captured = Assert.Single(client.Requests);
        Assert.Contains(memoryText, captured, StringComparison.Ordinal);
        Assert.Contains("retry-focus.md", captured, StringComparison.Ordinal);
        Assert.Equal(4, first.Items.Count);
        Assert.Equal(0, threadFilesDuringRequest);

        client.OnRequest = null;
        WriteMemory(memory, memoryText);
        await using (var restored = CreateService())
        {
            var cached = await restored.SuggestAsync(new WelcomeSuggestionRequest { Identity = identity, MaxItems = 4 });
            Assert.Equal(first.Fingerprint, cached.Fingerprint);
        }
        var repeated = await GenerateAsync(clearCache: true);
        Assert.Equal(first.Fingerprint, repeated.Fingerprint);
        const string updatedEvidence = "Next task: add coverage for RetryPolicy.cs.";
        WriteMemory(memory, updatedEvidence);
        var changed = await GenerateAsync(clearCache: true);
        Assert.NotEqual(first.Fingerprint, changed.Fingerprint);
        Assert.Contains(updatedEvidence, client.Requests.Last());
        var cachePath = Path.Combine(dataPath, "cache", "welcome-suggestions.json");
        using var cache = JsonDocument.Parse(await File.ReadAllTextAsync(cachePath));
        Assert.Equal(1, cache.RootElement.GetProperty("SchemaVersion").GetInt32());
    }

    private static void WriteMemory(MemoryStore memory, string fact)
    {
        File.WriteAllText(
            Path.Combine(memory.MemoryDirectoryPath, "retry-focus.md"),
            $"---\nname: retry-focus\ndescription: \"{fact}\"\nmetadata:\n  type: feedback\n---\n\n{fact}");
        File.WriteAllText(memory.IndexFilePath, $"- [Retry focus](retry-focus.md) — {fact}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); }
        catch { }
    }

    private sealed class SuggestionClient : IChatClient
    {
        public List<string> Requests { get; } = [];
        public Action? OnRequest { get; set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var history = messages.ToArray();
            if (history.LastOrDefault()?.Role == ChatRole.Tool)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Done") { FinishReason = ChatFinishReason.Stop };
                yield break;
            }
            Requests.Add((options?.Instructions ?? "") + string.Join("\n", history.Select(message => message.Text)));
            OnRequest?.Invoke();
            var items = Enumerable.Range(1, 4).Select(index => new
            {
                title = $"Review Worker{index}.cs",
                prompt = $"Review retry handling in Worker{index}.cs",
                reason = "Captured workspace memory"
            }).ToArray();
            yield return new ChatResponseUpdate(ChatRole.Assistant,
                [new FunctionCallContent("welcome-call", options!.Tools!.Single().Name,
                    new Dictionary<string, object?> { ["items"] = JsonSerializer.SerializeToElement(items) })])
                { FinishReason = ChatFinishReason.ToolCalls };
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
