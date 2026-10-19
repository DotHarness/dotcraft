using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Utilities;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions;

public sealed class ThreadGitInfoTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"thread_git_info_{Guid.NewGuid():N}");

    public ThreadGitInfoTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                foreach (var file in Directory.EnumerateFiles(_tempDir, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task Read_ReturnsBranchAndCredentialFreeOrigin()
    {
        var repo = await CreateRepositoryAsync("feat/thread-git-info");
        await GitAsync(repo, "remote", "add", "origin", "https://user:secret-token@github.com/o/r.git");

        Assert.Equal(
            new ThreadGitInfo { Branch = "feat/thread-git-info", OriginUrl = "https://github.com/o/r.git" },
            await ThreadGitInfoProbe.ReadAsync(repo, CancellationToken.None));
    }

    [Fact]
    public async Task Read_RecordsNoBranchOnDetachedHead()
    {
        var repo = await CreateRepositoryAsync("main");
        await GitAsync(repo, "checkout", "--detach");

        Assert.Null(await ThreadGitInfoProbe.ReadAsync(repo, CancellationToken.None));
    }

    [Fact]
    public async Task Read_ReturnsNothingOutsideGit()
    {
        var plain = Directory.CreateDirectory(Path.Combine(_tempDir, "plain")).FullName;

        Assert.Null(await ThreadGitInfoProbe.ReadAsync(plain, CancellationToken.None));
    }

    [Theory]
    [InlineData("https://user:token@github.com/o/r.git", "https://github.com/o/r.git")]
    [InlineData("ssh://git@github.com/o/r.git", "ssh://github.com/o/r.git")]
    [InlineData("git@github.com:o/r.git", "github.com:o/r.git")]
    [InlineData("C:\\repos\\origin", "C:\\repos\\origin")]
    public void SanitizeRemoteUrl_RemovesUserInformation(string raw, string expected)
    {
        Assert.Equal(expected, ThreadGitInfoProbe.SanitizeRemoteUrl(raw));
    }

    [Fact]
    public async Task GitInfo_SurvivesReplayAndReachesTheIndex()
    {
        var craft = Directory.CreateDirectory(Path.Combine(_tempDir, ".craft")).FullName;
        var thread = new SessionThread
        {
            Id = "thread_git_info",
            WorkspacePath = _tempDir,
            OriginChannel = "test",
            Status = ThreadStatus.Active,
            HistoryMode = HistoryMode.Server,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            GitInfo = new ThreadGitInfo { Branch = "main", OriginUrl = "https://github.com/o/r.git" }
        };
        await new ThreadStore(craft).SaveThreadAsync(thread);

        thread.GitInfo = new ThreadGitInfo { Branch = "feat/next", OriginUrl = "https://github.com/o/r.git" };
        await new ThreadStore(craft).SaveThreadBaselineAsync(thread);

        var store = new ThreadStore(craft);
        var loaded = await store.LoadThreadAsync(thread.Id);
        var summary = Assert.Single(await store.LoadIndexAsync(), entry => entry.Id == thread.Id);
        Assert.Equal(thread.GitInfo, loaded?.GitInfo);
        Assert.Equal(thread.GitInfo, summary.GitInfo);
    }

    [Fact]
    public async Task LocalTurn_RecordsGitInfoAlthoughARemoteToolHostIsAvailable()
    {
        var repo = await CreateRepositoryAsync("feat/local");
        var chat = new ReplyChatClient();
        await using var factory = new AgentFactory(
            dotcraftPath: repo,
            workspacePath: repo,
            config: AppConfigTestFactory.CreateOpenAI(),
            memoryStore: new MemoryStore(repo),
            skillsLoader: new SkillsLoader(repo),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            chatClient: chat,
            toolSources: Array.Empty<IToolSource>(),
            remoteToolHostClient: new UnroutedRemoteClient());
        var service = new SessionService(
            factory,
            chat.AsAIAgent(),
            new SessionPersistenceService(new ThreadStore(repo)),
            new SessionGate());
        var updated = new TaskCompletionSource<ThreadGitInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ThreadUpdatedForBroadcast += thread => updated.TrySetResult(thread.GitInfo);
        var thread = await service.CreateThreadAsync(
            new SessionIdentity { ChannelName = "test", UserId = "user", WorkspacePath = repo });

        await foreach (var _ in service.SubmitInputAsync(thread.Id, [new TextContent("hello")]))
        {
        }

        var info = await updated.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal("feat/local", info?.Branch);
    }

    private async Task<string> CreateRepositoryAsync(string branch)
    {
        var repo = Directory.CreateDirectory(Path.Combine(_tempDir, "repo")).FullName;
        await GitAsync(repo, "init");
        await GitAsync(repo, "checkout", "-b", branch);
        await GitAsync(repo, "-c", "user.email=test@example.com", "-c", "user.name=Test", "commit", "--allow-empty", "-m", "init");
        return repo;
    }

    private static async Task GitAsync(string cwd, params string[] args)
    {
        var result = await GitProcessRunner.RunAsync(cwd, args, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.True(result.ExitCode == 0, result.StdErr);
    }

    private sealed class ReplyChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>A host that is available but routes no thread, as in an ordinary local session.</summary>
    private sealed class UnroutedRemoteClient : IRemoteToolHostClient
    {
        public event Action<RemoteToolRouteChange>? RouteChanged { add { } remove { } }

        public void UpdateRemoteToolSnapshot(string threadId, EffectiveToolSnapshot snapshot, string mode) { }

        public ValueTask PrepareTurnAsync(string threadId, EffectiveToolSnapshot snapshot, string mode,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void ReportTurn(string threadId, string turnId, TurnStatus status) { }

        public ValueTask<RemoteToolHostCatalog> ListAsync(string threadId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<RemoteToolConnectResult> ConnectAsync(string threadId, string hostId, string workspaceId,
            CancellationToken cancellationToken = default, RemoteToolRouteInitiator initiator = RemoteToolRouteInitiator.Client) =>
            throw new NotSupportedException();

        public ValueTask<RemoteToolDisconnectResult> DisconnectAsync(string threadId,
            CancellationToken cancellationToken = default, RemoteToolRouteInitiator initiator = RemoteToolRouteInitiator.Client) =>
            throw new NotSupportedException();

        public bool TryGetRoute(string threadId, out RemoteToolRoute route)
        {
            route = null!;
            return false;
        }

        public bool TryGetConnectionSnapshot(string threadId, out RemoteToolConnectionSnapshot snapshot)
        {
            snapshot = null!;
            return false;
        }

        public bool TryForkRoute(string parentThreadId, string childThreadId) => false;

        public ValueTask<string> WriteImageAsync(RemoteToolRoute route, string threadId, string callId, byte[] bytes,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<byte[]> ReadImageAsync(RemoteToolRoute route, string threadId, string callId, string path,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<ToolExecutionResult> InvokeAsync(RemoteToolRoute route, ToolDefinition definition, string contractHash,
            ToolInvocationContext context, JsonObject arguments, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
