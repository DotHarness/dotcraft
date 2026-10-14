using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tests.Tools;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceMemoryStoreAccessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dotcraft-memory-access-{Guid.NewGuid():N}");
    private readonly string _workspace;
    private readonly string _craft;
    private readonly string _worktree;

    public SessionServiceMemoryStoreAccessTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        _craft = Path.Combine(_workspace, ".craft");
        _worktree = Path.Combine(_root, "worktrees", "feature");
        Directory.CreateDirectory(_craft);
        Directory.CreateDirectory(_worktree);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task WorktreeThread_WritesItsStoreButNotOtherPathsOutsideTheWorktree()
    {
        var store = Path.Combine(_craft, "memory");
        await using var factory = CreateAgentFactory();
        var writeFile = await WriteFileForAsync(factory, new ThreadConfiguration
        {
            ExecutionWorkspaceOverride = _worktree,
            RequireApprovalOutsideWorkspace = false
        });

        Assert.True((await WriteAsync(writeFile, Path.Combine(store, "fact.md"))).Success);
        Assert.False((await WriteAsync(writeFile, Path.Combine(_workspace, "README.md"))).Success);
        Assert.False(File.Exists(Path.Combine(_workspace, "README.md")));
    }

    [Fact]
    public async Task ScopedThreadThatRejectsOutsidePaths_WritesItsOwnStoreOnly()
    {
        await using var factory = CreateAgentFactory();
        var writeFile = await WriteFileForAsync(factory, new ThreadConfiguration
        {
            MemoryScope = "nightly",
            Cwd = _worktree,
            RequireApprovalOutsideWorkspace = false
        });

        Assert.True((await WriteAsync(writeFile, Path.Combine(_craft, "scopes", "nightly", "memory", "fact.md"))).Success);
        Assert.False((await WriteAsync(writeFile, Path.Combine(_craft, "memory", "fact.md"))).Success);
    }

    [Fact]
    public async Task MemoryDisabledThread_GetsNoStoreExemption()
    {
        await using var factory = CreateAgentFactory();
        var writeFile = await WriteFileForAsync(factory, new ThreadConfiguration
        {
            MemoryEnabled = false,
            ExecutionWorkspaceOverride = _worktree,
            RequireApprovalOutsideWorkspace = false
        });

        Assert.False((await WriteAsync(writeFile, Path.Combine(_craft, "memory", "fact.md"))).Success);
    }

    private AgentFactory CreateAgentFactory()
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        var skills = new SkillsLoader(_craft);
        var source = new CoreToolSource(
            config,
            TestModelProviderRegistry.Create(),
            skills,
            new AutoApproveApprovalService(),
            new StubBackgroundTerminalService());
        return new AgentFactory(
            dotcraftPath: _craft,
            workspacePath: _workspace,
            config: config,
            memoryStore: new MemoryStore(_craft),
            skillsLoader: skills,
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            toolSources: [source]);
    }

    private async Task<ToolRegistration> WriteFileForAsync(AgentFactory factory, ThreadConfiguration configuration)
    {
        var service = new SessionService(
            factory,
            factory.CreateAgentForMode(AgentMode.Agent),
            new SessionPersistenceService(new ThreadStore(_craft)),
            new SessionGate());
        var thread = await service.CreateThreadAsync(
            new SessionIdentity { ChannelName = "test", UserId = "user", WorkspacePath = _workspace },
            configuration);

        var snapshot = await service.GetEffectiveToolSnapshotAsync(thread.Id);
        return Assert.Single(snapshot.Registrations.Values, static registration =>
            registration.Definition.Name.Name == nameof(FileTools.WriteFile));
    }

    private static async Task<ToolExecutionResult> WriteAsync(ToolRegistration writeFile, string path) =>
        await writeFile.Binding.Runtime.InvokeAsync(
            new ToolInvocationContext(
                "thread",
                null,
                "call-1",
                ToolInvocationAudience.Model,
                writeFile.Definition.Name,
                writeFile.Definition.Id,
                writeFile.Binding.Id,
                writeFile.Binding.Revision,
                DateTimeOffset.UtcNow),
            new JsonObject { ["path"] = path, ["content"] = "A fact." });
}
