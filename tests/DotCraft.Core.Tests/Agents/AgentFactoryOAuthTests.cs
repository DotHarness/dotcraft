using DotCraft.Agents;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;

namespace DotCraft.Tests.Agents;

public sealed class AgentFactoryOAuthTests : IDisposable
{
    private readonly string _tempDir;

    public AgentFactoryOAuthTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AFOAUTH_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task GetCompactionPipeline_PreservesChatGptOAuthRuntime()
    {
        var config = new AppConfig
        {
            ProviderId = "openai",
            Model = "gpt-5-codex",
            Providers =
            {
                ["openai"] = new AppConfig.ModelProviderConfig
                {
                    DisplayName = "OpenAI (ChatGPT)",
                    Protocol = ModelProviderProtocols.OpenAIResponses,
                    AuthMethod = ModelProviderAuthMethods.ChatGptOAuth,
                    ChatGptAccountId = "acct_test"
                }
            }
        };
        var registry = new ChatClientRegistry(new OpenAIClientProvider(new FakeOpenAIAuthService()));

        await using var agentFactory = new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: new MemoryStore(_tempDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            toolProviders: [],
            chatClientRegistry: registry);

        var pipeline = agentFactory.GetCompactionPipeline(
            "thread_oauth",
            providerIdOverride: "openai",
            modelOverride: "gpt-5-codex",
            configOverride: config);

        Assert.Equal(0, pipeline.EvaluateThreshold(0).Tokens);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private sealed class FakeOpenAIAuthService : IOpenAIAuthService
    {
        public bool IsAuthenticated => true;

        public event Action<OpenAIAuthStatus>? LoggedIn
        {
            add { }
            remove { }
        }

        public event Action? LoggedOut
        {
            add { }
            remove { }
        }

        public OpenAIAuthStatus GetStatus() => new(
            true,
            "acct_test",
            "plus",
            "test@example.com",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1));

        public Task<OpenAIAuthStatus> LoginAsync(
            bool openBrowser,
            Action<string>? onAuthorizationUrl,
            CancellationToken cancellationToken) =>
            Task.FromResult(GetStatus());

        public Task LogoutAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken) =>
            Task.FromResult("access-token");

        public string? GetAccountId() => "acct_test";
    }
}
