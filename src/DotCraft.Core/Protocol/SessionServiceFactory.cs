using DotCraft.Agents;
using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Hooks;
using DotCraft.Logging;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Tracing;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotCraft.Protocol;

/// <summary>
/// Factory helper that constructs a <see cref="SessionService"/> from an already-built
/// <see cref="DotCraft.Agents.AgentFactory"/> and <see cref="AIAgent"/> plus shared DI services.
/// Avoids boilerplate across channel hosts that each build their own AgentFactory.
/// </summary>
public static class SessionServiceFactory
{
    /// <summary>
    /// Creates a <see cref="SessionService"/> by resolving <see cref="SessionPersistenceService"/>,
    /// <see cref="SessionGate"/>, <see cref="HookRunner"/>, and <see cref="TraceCollector"/>
    /// from the provided service provider.
    /// </summary>
    public static SessionService Create(
        AgentFactory agentFactory,
        AIAgent agent,
        IServiceProvider sp,
        TimeSpan? approvalTimeout = null)
    {
        var loggerFactory = sp.GetService<ILoggerFactory>();
        return new SessionService(
            agentFactory,
            agent,
            sp.GetRequiredService<SessionPersistenceService>(),
            sp.GetRequiredService<SessionGate>(),
            sp.GetService<IChannelRuntimeToolProvider>(),
            sp.GetService<HookRunner>(),
            sp.GetService<TraceCollector>(),
            sp.GetService<TokenUsageStore>(),
            approvalTimeout,
            logger: loggerFactory?.CreateLogger<SessionService>(),
            approvalStore: sp.GetService<ApprovalStore>(),
            toolProfileRegistry: sp.GetService<IToolProfileRegistry>(),
            sessionStreamDebugLogger: sp.GetService<SessionStreamDebugLogger>(),
            backgroundTerminalService: sp.GetService<IBackgroundTerminalService>(),
            appConfigMonitor: sp.GetService<IAppConfigMonitor>());
    }
}
