using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Contributions;
using DotCraft.Runtime;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Commands.Custom;
using DotCraft.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DotCraft.Tests.Runtime;

public sealed class WorkspaceRuntimeTests
{
    [Fact]
    public void RegistrationAndHostBuild_DoNotCreateWorkspaceState()
    {
        var root = NewTemporaryPath();
        var craftPath = Path.Combine(root, ".craft");
        var builder = Host.CreateApplicationBuilder();

        AddRuntimeTestServices(builder.Services, root, craftPath);
        using var host = builder.Build();

        var paths = host.Services.GetRequiredService<DotCraftPaths>();
        Assert.False(paths.UserData.IsConfigured);
        Assert.Null(host.Services.GetRequiredService<SkillsLoader>().UserSkillsPath);
        Assert.Null(host.Services.GetRequiredService<CustomCommandLoader>().UserCommandsPath);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task GenericHost_UsesCustomDataDirectory_AndStopsRuntime()
    {
        var root = NewTemporaryPath();
        var craftPath = Path.Combine(root, ".agents");
        var builder = Host.CreateApplicationBuilder();
        AddRuntimeTestServices(builder.Services, root, craftPath);

        WorkspaceRuntime runtime;
        using (var host = builder.Build())
        {
            await host.StartAsync();

            runtime = host.Services.GetRequiredService<WorkspaceRuntime>();
            Assert.True(runtime.IsStarted);

            var thread = await runtime.Sessions.CreateThreadAsync(new SessionIdentity
            {
                ChannelName = "embedded",
                UserId = "test-user",
                WorkspacePath = root
            });
            Assert.Equal(thread.Id, (await runtime.Sessions.GetThreadAsync(thread.Id))?.Id);

            await host.StopAsync();
            Assert.False(runtime.IsStarted);
        }

        Assert.True(Directory.Exists(craftPath));
        Assert.False(Directory.Exists(Path.Combine(root, ".craft")));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task GenericHost_StartsWithoutConfiguredProvider()
    {
        var root = NewTemporaryPath();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDotCraftRuntime(new DotCraftRuntimeOptions
        {
            Config = new AppConfig(),
            WorkspacePath = root,
            DataPath = Path.Combine(root, ".craft")
        });
        builder.Services.AddSingleton<IConfigSchemaProvider>(new TestConfigSchemaProvider());

        using (var host = builder.Build())
        {
            await host.StartAsync();
            var runtime = host.Services.GetRequiredService<WorkspaceRuntime>();
            Assert.True(runtime.IsStarted);
            Assert.Empty(runtime.ConfigSchema);
            Assert.NotNull(new AgentProfileStore(runtime.Paths).List());
            Assert.Empty(await runtime.Sessions.FindThreadsAsync(CreateIdentity(root)));

            await host.StopAsync();
            Assert.False(runtime.IsStarted);
        }

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task GenericHost_DefersMissingProviderImplementationUntilAgentBuild()
    {
        var root = NewTemporaryPath();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDotCraftRuntime(new DotCraftRuntimeOptions
        {
            Config = CreateConfig(),
            WorkspacePath = root,
            DataPath = Path.Combine(root, ".craft")
        });
        builder.Services.AddSingleton<IConfigSchemaProvider>(new TestConfigSchemaProvider());

        using (var host = builder.Build())
        {
            await host.StartAsync();
            var runtime = host.Services.GetRequiredService<WorkspaceRuntime>();

            await Assert.ThrowsAsync<ModelProviderNotRegisteredException>(
                () => runtime.Sessions.CreateThreadAsync(CreateIdentity(root)));

            await host.StopAsync();
        }

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task GenericHost_DefersMissingModelUntilAgentBuild()
    {
        var root = NewTemporaryPath();
        var config = CreateConfig();
        config.ProviderPreferences.Clear();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddOpenAIModelProvider();
        builder.Services.AddDotCraftRuntime(new DotCraftRuntimeOptions
        {
            Config = config,
            WorkspacePath = root,
            DataPath = Path.Combine(root, ".craft")
        });
        builder.Services.AddSingleton<IConfigSchemaProvider>(new TestConfigSchemaProvider());

        using (var host = builder.Build())
        {
            await host.StartAsync();
            var runtime = host.Services.GetRequiredService<WorkspaceRuntime>();

            var error = await Assert.ThrowsAsync<ArgumentException>(
                () => runtime.Sessions.CreateThreadAsync(CreateIdentity(root)));
            Assert.Equal("Model must be configured. (Parameter 'config')", error.Message);

            await host.StopAsync();
        }

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task ProviderConfigurationAddedAfterStartup_IsUsedWithoutRestart()
    {
        var root = NewTemporaryPath();
        var config = new AppConfig();
        var provider = new RecordingModelProvider();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IModelProvider>(provider);
        builder.Services.AddDotCraftRuntime(new DotCraftRuntimeOptions
        {
            Config = config,
            WorkspacePath = root,
            DataPath = Path.Combine(root, ".craft")
        });
        builder.Services.AddSingleton<IConfigSchemaProvider>(new TestConfigSchemaProvider());

        using (var host = builder.Build())
        {
            await host.StartAsync();
            var runtime = host.Services.GetRequiredService<WorkspaceRuntime>();
            var monitor = host.Services.GetRequiredService<IAppConfigMonitor>();

            ConfigureProvider(monitor.Current, "key-a");
            monitor.NotifyChanged("test/provider-added", [
                ConfigChangeRegions.ProviderRegistry,
                ConfigChangeRegions.WorkspaceProviderPreferences
            ]);

            var thread = await runtime.Sessions.CreateThreadAsync(CreateIdentity(root));
            var firstTurnEvents = await DrainAsync(
                runtime.Sessions.SubmitInputAsync(thread.Id, [new TextContent("first")]));
            Assert.Contains(firstTurnEvents, item => item.EventType == SessionEventType.TurnCompleted);
            Assert.DoesNotContain(firstTurnEvents, item => item.EventType == SessionEventType.TurnFailed);
            Assert.Contains("key-a", provider.CreatedApiKeys);

            monitor.Current.Providers["test-provider"].ApiKey = "key-b";
            monitor.NotifyChanged("test/provider-updated", [ConfigChangeRegions.ProviderRegistry]);

            var secondTurnEvents = await DrainAsync(
                runtime.Sessions.SubmitInputAsync(thread.Id, [new TextContent("second")]));
            Assert.Contains(secondTurnEvents, item => item.EventType == SessionEventType.TurnCompleted);
            Assert.DoesNotContain(secondTurnEvents, item => item.EventType == SessionEventType.TurnFailed);
            Assert.Contains("key-b", provider.CreatedApiKeys);

            await host.StopAsync();
        }

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task ASuggestionReplacement_RegisteredAfterAConsumerCaptured_IsObservedByIt()
    {
        var root = NewTemporaryPath();
        var builder = Host.CreateApplicationBuilder();
        AddRuntimeTestServices(builder.Services, root, Path.Combine(root, ".craft"));

        using (var host = builder.Build())
        {
            await host.StartAsync();
            var runtime = host.Services.GetRequiredService<WorkspaceRuntime>();

            // What an open AppServer connection holds: captured once, never re-read.
            var captured = runtime.WelcomeSuggestionService;
            var registry = host.Services.GetRequiredService<ContributionRegistry>();
            var replacement = new CountingWelcomeSuggestionService();
            var handle = registry.Add<IWelcomeSuggester>(
                replacement,
                new ContributionOptions(ReplaceTarget: SuggestionServiceNames.WelcomeSuggestions));

            captured.ClearWorkspaceCache(root);
            Assert.Equal(1, replacement.Clears);

            handle.Dispose();
            captured.ClearWorkspaceCache(root);
            Assert.Equal(1, replacement.Clears);

            await host.StopAsync();
        }

        Directory.Delete(root, recursive: true);
    }

    private sealed class CountingWelcomeSuggestionService : IWelcomeSuggester
    {
        public int Clears { get; private set; }

        public Task<WelcomeSuggestionSnapshot> SuggestAsync(
            WelcomeSuggestionRequest parameters,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WelcomeSuggestionSnapshot { Source = "replacement" });

        public void ScheduleRefresh(string workspacePath, string? triggerThreadId = null)
        {
        }

        public void ClearWorkspaceCache(string workspacePath) => Clears++;
    }

    private static string NewTemporaryPath() =>
        Path.Combine(Path.GetTempPath(), $"dotcraft-runtime-{Guid.NewGuid():N}");

    private static SessionIdentity CreateIdentity(string workspacePath) => new()
    {
        ChannelName = "embedded",
        UserId = "test-user",
        WorkspacePath = workspacePath
    };

    private static async Task<IReadOnlyList<SessionEvent>> DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        var collected = new List<SessionEvent>();
        await foreach (var item in events)
            collected.Add(item);
        return collected;
    }

    private static void AddRuntimeTestServices(
        IServiceCollection services,
        string workspacePath,
        string craftPath)
    {
        services.AddOpenAIModelProvider();
        services.AddDotCraftRuntime(new DotCraftRuntimeOptions
        {
            Config = CreateConfig(),
            WorkspacePath = workspacePath,
            DataPath = craftPath
        });
        services.AddSingleton<IConfigSchemaProvider>(new TestConfigSchemaProvider());
    }

    private static AppConfig CreateConfig()
    {
        var config = new AppConfig();
        ConfigureProvider(config, "test-key");
        return config;
    }

    private static void ConfigureProvider(AppConfig config, string apiKey)
    {
        config.ProviderId = "test-provider";
        config.ProviderPreferences = new Dictionary<string, ModelPreference>(StringComparer.OrdinalIgnoreCase)
        {
            ["test-provider"] = new ModelPreference { Model = "test-model" }
        };
        config.Providers = new Dictionary<string, AppConfig.ModelProviderConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["test-provider"] = new AppConfig.ModelProviderConfig
            {
                DisplayName = "Test Provider",
                Protocol = ModelProviderProtocols.OpenAI,
                ApiKey = apiKey,
                EndPoint = "https://127.0.0.1:9/v1"
            }
        };
    }

    private sealed class TestConfigSchemaProvider : IConfigSchemaProvider
    {
        public IReadOnlyList<ConfigSchemaSection> GetConfigSchema() => [];
    }

    private sealed class RecordingModelProvider : IModelProvider
    {
        public IReadOnlyCollection<string> Protocols { get; } = [ModelProviderProtocols.OpenAI];

        public List<string> CreatedApiKeys { get; } = [];

        public IChatClient CreateChatClient(EffectiveModelRuntime runtime)
        {
            CreatedApiKeys.Add(runtime.ApiKey);
            return new RecordingChatClient(runtime.ApiKey);
        }
    }

    private sealed class RecordingChatClient(string response) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new ChatResponseUpdate(ChatRole.Assistant, response);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
