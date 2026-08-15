using DotCraft.Agents;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using DotCraft.Harness;
using DotCraft.Runtime;
using DotCraft.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DotCraft.Tests.Harness;

public sealed class DotCraftHarnessRegistrationTests
{
    [Fact]
    public void Registration_IsSideEffectFreeAndUsesConfiguredPaths()
    {
        var workspacePath = NewTemporaryPath();
        var userDataPath = NewTemporaryPath();
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddDotCraftHarness(CreateConfig(), options =>
        {
            options.WorkspacePath = workspacePath;
            options.DataPath = ".agents";
            options.UserDataPath = userDataPath;
        });

        using var host = builder.Build();
        var paths = host.Services.GetRequiredService<DotCraftPaths>();

        Assert.Equal(Path.Combine(workspacePath, ".agents"), paths.Data.RootPath);
        Assert.Equal(userDataPath, paths.UserData.RootPath);
        Assert.False(Directory.Exists(workspacePath));
        Assert.False(Directory.Exists(userDataPath));
    }

    [Fact]
    public void Registration_ComposesProvidersSchemaAndRuntimeOnce()
    {
        var workspacePath = NewTemporaryPath();
        var userDataPath = NewTemporaryPath();
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddOpenAIModelProvider();
        builder.Services.AddAnthropicModelProvider();
        builder.Services.AddDotCraftHarness(CreateConfig(), options =>
        {
            options.WorkspacePath = workspacePath;
            options.UserDataPath = userDataPath;
        });
        builder.Services.AddOpenAIModelProvider();
        builder.Services.AddAnthropicModelProvider();

        using var host = builder.Build();
        var providers = host.Services.GetServices<IModelProvider>().ToArray();
        Assert.Equal(2, providers.Length);
        Assert.Single(providers.OfType<OpenAIClientProvider>());
        Assert.Single(providers.OfType<AnthropicClientProvider>());
        Assert.NotEmpty(host.Services.GetRequiredService<IConfigSchemaProvider>().GetConfigSchema());
        Assert.Equal(
            Path.Combine(userDataPath, "auth.json"),
            host.Services.GetRequiredService<OpenAITokenStore>().FilePath);

        Assert.NotNull(host.Services.GetRequiredService<WorkspaceRuntime>());
    }

    [Fact]
    public void Registration_RejectsConflictingOpenAIUserDataPaths()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddOpenAIModelProvider(NewTemporaryPath());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.Services.AddOpenAIModelProvider(NewTemporaryPath()));

        Assert.Contains("already configured", exception.Message, StringComparison.Ordinal);
    }

    private static string NewTemporaryPath() =>
        Path.Combine(Path.GetTempPath(), $"dotcraft-harness-{Guid.NewGuid():N}");

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
}
