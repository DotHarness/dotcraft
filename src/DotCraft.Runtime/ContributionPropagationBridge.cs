using DotCraft.Agents;
using DotCraft.Commands.Core;
using DotCraft.Context;
using DotCraft.Contributions;
using DotCraft.Sessions;
using DotCraft.Tools;

namespace DotCraft.Runtime;

/// <summary>Invalidates cached host state for captured contribution changes; in-flight turns are untouched.</summary>
internal sealed class ContributionPropagationBridge : IDisposable
{
    private static readonly Type[] AgentCapturedContracts =
    [
        typeof(IToolSource),
        typeof(IToolRestriction),
        typeof(IChatMiddleware),
        typeof(IAgentContextSource),
        typeof(ISubAgentRuntimeSource)
    ];

    private readonly IContributionRegistry _registry;
    private readonly EventHandler<ContributionsChangedEventArgs> _handler;
    private int _disposed;

    /// <summary>Subscribes to the registry.</summary>
    /// <param name="contextPageManager">Released before the agents are invalidated, so a rebuilt prompt cannot re-read a memoized page the mutation just invalidated.</param>
    internal ContributionPropagationBridge(
        IContributionRegistry registry,
        IThreadAgentRefreshService refreshService,
        IContextPageManager? contextPageManager = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentNullException.ThrowIfNull(refreshService);
        _handler = (_, args) =>
        {
            if (args.Includes<ICodeCommand>())
                contextPageManager?.ReleaseStablePage(ContextPageKeys.CustomCommandsSummary("*"));
            if (args.Includes<IThreadSystemPromptContextProvider>() && contextPageManager is not null)
            {
                foreach (var provider in _registry.Resolve<IThreadSystemPromptContextProvider>())
                    contextPageManager.ReleaseStablePage(provider.ContextPageKey);
            }
            if (AffectsThreadState(args))
                refreshService.InvalidateThreadAgents();
        };
        _registry.Changed += _handler;
    }

    /// <summary>Unsubscribes from the registry. Idempotent.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _registry.Changed -= _handler;
    }

    private static bool AffectsThreadState(ContributionsChangedEventArgs args)
    {
        for (var index = 0; index < AgentCapturedContracts.Length; index++)
        {
            if (args.Includes(AgentCapturedContracts[index]))
                return true;
        }

        return false;
    }
}
