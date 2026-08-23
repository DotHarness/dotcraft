using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Contributions;
using DotCraft.Runtime;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Commands.Custom;
using DotCraft.Workspaces;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task GenericHost_StartupFailure_DoesNotExposeReadyRuntime()
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
            var runtime = host.Services.GetRequiredService<WorkspaceRuntime>();
            await Assert.ThrowsAsync<ModelProviderNotRegisteredException>(() => host.StartAsync());
            Assert.False(runtime.IsStarted);
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

    private static AppConfig CreateConfig() => new()
    {
        ProviderId = "test-provider",
        ProviderPreferences = new Dictionary<string, ModelPreference>(StringComparer.OrdinalIgnoreCase)
        {
            ["test-provider"] = new ModelPreference { Model = "test-model" }
        },
        Providers =
        {
            ["test-provider"] = new AppConfig.ModelProviderConfig
            {
                DisplayName = "Test Provider",
                Protocol = ModelProviderProtocols.OpenAI,
                ApiKey = "test-key",
                EndPoint = "https://127.0.0.1:9/v1"
            }
        }
    };

    private sealed class TestConfigSchemaProvider : IConfigSchemaProvider
    {
        public IReadOnlyList<ConfigSchemaSection> GetConfigSchema() => [];
    }
}
