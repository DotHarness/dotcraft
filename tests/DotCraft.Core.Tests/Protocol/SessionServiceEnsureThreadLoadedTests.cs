using System.Reflection;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;

namespace DotCraft.Tests.Sessions.Protocol;

/// <summary>
/// Verifies <see cref="SessionService.EnsureThreadLoadedAsync"/> hydrates the per-thread runtime agent
/// after a cold load from disk (simulated second <see cref="SessionService"/> instance).
/// </summary>
public sealed class SessionServiceEnsureThreadLoadedTests : IDisposable
{
    private readonly string _tempDir;

    public SessionServiceEnsureThreadLoadedTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "EnsureLoaded_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task EnsureThreadLoadedAsync_BuildsPerThreadAgent_WhenThreadHadConfigurationOnDisk()
    {
        var store = new ThreadStore(_tempDir);
        var persistence = new SessionPersistenceService(store);
        var identity = new SessionIdentity
        {
            ChannelName = "test",
            UserId = "u",
            WorkspacePath = _tempDir
        };
        var config = new ThreadConfiguration { Mode = "plan" };

        await using var agentFactory = CreateAgentFactory();
        var defaultAgent = agentFactory.CreateAgentForMode(AgentMode.Agent);
        var svc1 = new SessionService(agentFactory, defaultAgent, persistence, new SessionGate());
        var thread = await svc1.CreateThreadAsync(identity, config);
        await store.SaveThreadAsync(thread);

        Assert.True(ThreadAgentsContains(svc1, thread.Id));

        var svc2 = new SessionService(agentFactory, defaultAgent, persistence, new SessionGate());
        await svc2.GetThreadAsync(thread.Id);
        Assert.False(ThreadAgentsContains(svc2, thread.Id));

        await svc2.EnsureThreadLoadedAsync(thread.Id);
        Assert.True(ThreadAgentsContains(svc2, thread.Id));
    }

    [Fact]
    public async Task CreateThreadAsync_MaterializesThreadAndInitializesContextUsage()
    {
        var store = new ThreadStore(_tempDir);
        var persistence = new SessionPersistenceService(store);
        var identity = new SessionIdentity
        {
            ChannelName = "test",
            UserId = "u",
            WorkspacePath = _tempDir
        };

        await using var agentFactory = CreateAgentFactory();
        var defaultAgent = agentFactory.CreateAgentForMode(AgentMode.Agent);
        var svc = new SessionService(agentFactory, defaultAgent, persistence, new SessionGate());

        var thread = await svc.CreateThreadAsync(identity);
        var fromDisk = await store.LoadThreadAsync(thread.Id);
        Assert.NotNull(fromDisk);
        Assert.Equal(thread.Id, fromDisk!.Id);
        Assert.Equal(0, store.LoadContextUsageTokens(thread.Id));

        var listed = await svc.FindThreadsAsync(identity);
        Assert.Contains(listed, s => s.Id == thread.Id);
    }

    private AgentFactory CreateAgentFactory()
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        var memory = new MemoryStore(_tempDir);
        var skills = new SkillsLoader(_tempDir);
        return new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: memory,
            skillsLoader: skills,
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            toolProviders: Array.Empty<IAgentToolProvider>());
    }

    private static bool ThreadAgentsContains(SessionService svc, string threadId)
    {
        var registryField = typeof(SessionService).GetField("_runtimeRegistry", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(registryField);
        var registry = registryField.GetValue(svc);
        Assert.NotNull(registry);

        var tryGetRuntime = registry.GetType().GetMethod("TryGetRuntime", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(tryGetRuntime);
        var args = new object?[] { threadId, null };
        var found = (bool)tryGetRuntime.Invoke(registry, args)!;
        if (!found || args[1] == null)
            return false;

        var agentProperty = args[1]!.GetType().GetProperty("Agent", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(agentProperty);
        return agentProperty.GetValue(args[1]) != null;
    }
}
