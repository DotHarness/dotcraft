using System.Reflection;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using Xunit;
using DotCraft.Tools;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceApprovalPolicyResolutionTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "SSApprovalPolicy_" + Guid.NewGuid().ToString("N")[..8]);

    public SessionServiceApprovalPolicyResolutionTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task ResolveApprovalPolicy_UsesWorkspaceDefault_WhenThreadPolicyIsDefault()
    {
        await using var agentFactory = CreateAgentFactory();
        var monitor = new AppConfigMonitor(new AppConfig
        {
            Permissions = new AppConfig.PermissionsConfig
            {
                DefaultApprovalPolicy = ApprovalPolicy.AutoApprove
            }
        });
        var service = CreateService(agentFactory, monitor);

        var resolved = InvokeResolveApprovalPolicy(service, ApprovalPolicy.Default);

        Assert.Equal(ApprovalPolicy.AutoApprove, resolved);
    }

    [Fact]
    public async Task ResolveApprovalPolicy_PerThreadPolicy_OverridesWorkspaceDefault()
    {
        await using var agentFactory = CreateAgentFactory();
        var monitor = new AppConfigMonitor(new AppConfig
        {
            Permissions = new AppConfig.PermissionsConfig
            {
                DefaultApprovalPolicy = ApprovalPolicy.AutoApprove
            }
        });
        var service = CreateService(agentFactory, monitor);

        var resolved = InvokeResolveApprovalPolicy(service, ApprovalPolicy.Interrupt);

        Assert.Equal(ApprovalPolicy.Interrupt, resolved);
    }

    [Fact]
    public async Task ResolveApprovalPolicy_Prompt_DoesNotInheritWorkspaceAutoApprove()
    {
        await using var agentFactory = CreateAgentFactory();
        var monitor = new AppConfigMonitor(new AppConfig
        {
            Permissions = new AppConfig.PermissionsConfig
            {
                DefaultApprovalPolicy = ApprovalPolicy.AutoApprove
            }
        });
        var service = CreateService(agentFactory, monitor);

        // `prompt` always forces the interactive flow; it must never fall back to a
        // full-access workspace default.
        var resolved = InvokeResolveApprovalPolicy(service, ApprovalPolicy.Prompt);

        Assert.Equal(ApprovalPolicy.Prompt, resolved);
    }

    private SessionService CreateService(AgentFactory agentFactory, IAppConfigMonitor monitor)
    {
        var defaultAgent = agentFactory.CreateAgentForMode(AgentMode.Agent);
        return new SessionService(
            agentFactory,
            defaultAgent,
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate(),
            appConfigMonitor: monitor);
    }

    private AgentFactory CreateAgentFactory()
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        return new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: new MemoryStore(_tempDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            toolSources: Array.Empty<IToolSource>());
    }

    private static ApprovalPolicy InvokeResolveApprovalPolicy(SessionService service, ApprovalPolicy threadPolicy)
    {
        var method = typeof(SessionService).GetMethod(
            "ResolveApprovalPolicy",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (ApprovalPolicy)method.Invoke(service, [threadPolicy])!;
    }
}
