using System.Runtime.CompilerServices;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Harness;
using DotCraft.Memory;
using DotCraft.Runtime;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DotCraft.Tests.Harness;

public sealed class HarnessPersonalMemoryPolicyTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"dotcraft-harness-memory-{Guid.NewGuid():N}");
    private string UserDataPath => Path.Combine(_workspace, "user-data");

    [Fact]
    public async Task ResumeWithPersonalMemoryDisabled_OmitsOnlyThePersonalIndex()
    {
        const string personalEntry = "fixture-personal-memory";
        const string ownEntry = "fixture-agent-memory";
        const string sharedEntry = "fixture-shared-memory";
        var dataPath = Path.Combine(_workspace, ".craft");
        WriteIndex(MemoryStore.DirectoryOf(UserDataPath), personalEntry);
        WriteIndex(MemoryStore.DirectoryOf(Path.Combine(dataPath, "scopes", "agent")), ownEntry);
        WriteIndex(Path.Combine(dataPath, "memory-stores", "team"), sharedEntry);

        var client = new RecordingChatClient();
        string threadId;
        using (var host = CreateHost(client))
        {
            await host.StartAsync();
            var sessions = host.Services.GetRequiredService<WorkspaceRuntime>().Sessions;
            var thread = await sessions.CreateThreadAsync(
                new SessionIdentity { ChannelName = "test", UserId = "user", WorkspacePath = _workspace },
                new ThreadConfiguration
                {
                    MemoryScope = "agent",
                    MemoryStores = [new MemoryStoreAttachment("team", "Team", "Shared knowledge.", MemoryStoreAccess.Read)]
                });
            Assert.True(thread.Configuration?.MemoryPersonal);
            threadId = thread.Id;
            await host.StopAsync();
        }

        using var resumedHost = CreateHost(client, disablePersonalMemory: true);
        await resumedHost.StartAsync();
        var resumedSessions = resumedHost.Services.GetRequiredService<WorkspaceRuntime>().Sessions;
        await resumedSessions.ResumeThreadAsync(threadId);
        await foreach (var _ in resumedSessions.SubmitInputAsync(threadId, [new TextContent("Continue.")]))
        {
        }

        Assert.Contains(ownEntry, client.Prompt, StringComparison.Ordinal);
        Assert.Contains(sharedEntry, client.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(personalEntry, client.Prompt, StringComparison.Ordinal);
        await resumedHost.StopAsync();
    }

    private IHost CreateHost(RecordingChatClient client, bool disablePersonalMemory = false)
    {
        var config = new AppConfig
        {
            ProviderId = "test",
            ProviderPreferences = { ["test"] = new ModelPreference { Model = "test-model" } },
            Providers = { ["test"] = new AppConfig.ModelProviderConfig { Protocol = ModelProviderProtocols.OpenAI, ApiKey = "test-key" } }
        };
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDotCraftHarness(config, options =>
        {
            options.WorkspacePath = _workspace;
            options.UserDataPath = UserDataPath;
            options.DisablePersonalMemory = disablePersonalMemory;
        });
        builder.Services.AddSingleton(new ModelProviderRegistry([new RecordingModelProvider(client)]));
        return builder.Build();
    }

    private static void WriteIndex(string directory, string entry)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, MemoryStore.IndexFileName), $"- [Test](test.md) — {entry}");
    }

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    private sealed class RecordingModelProvider(RecordingChatClient client) : IModelProvider
    {
        public IReadOnlyCollection<string> Protocols { get; } = [ModelProviderProtocols.OpenAI];
        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => client;
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public string Prompt { get; private set; } = string.Empty;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Prompt = string.Join("\n", messages.Select(message => message.Text));
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Done.");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
