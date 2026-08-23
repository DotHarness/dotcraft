using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Sessions;
using DotCraft.Tools;
using Microsoft.Extensions.Logging;

namespace DotCraft.Runtime;

/// <summary>Owns everything one workspace runtime puts into, and takes out of, the contribution registry.</summary>
internal sealed class WorkspaceContributionScope(IContributionRegistry registry) : IDisposable
{
    private readonly IContributionRegistry _registry =
        registry ?? throw new ArgumentNullException(nameof(registry));

    private readonly List<IDisposable> _owned = [];
    private ContributionPropagationBridge? _propagationBridge;
    private ThreadRuntimeSignalDispatcher? _runtimeSignalDispatcher;
    private bool _disposed;

    /// <summary>Registers the kernel's own contribution catalogs. Self-guarding and never scope-owned.</summary>
    public void RegisterBuiltInCatalogs()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SystemPromptSectionCatalog.RegisterBuiltIns(_registry);
        ChatMiddlewareCatalog.RegisterBuiltIns(_registry);
    }

    /// <summary>Seeds the container's contributor multi-registrations into their contribution points.</summary>
    /// <remarks>Scope-owned, unlike the catalogs: seeding is not self-guarding, so a second start would duplicate it.</remarks>
    public void SeedContainerContributors(IServiceProvider services)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(services);
        var registrar = _registry.CreateRegistrar(ContributionOrigin.Builtin);
        _owned.Add(registrar);
        _registry.SeedContributions<IThreadSystemPromptContextProvider>(services, registrar);
    }

    /// <summary>Registers the composition root's dispatch stages.</summary>
    public void RegisterKernelContributions(
        IToolPolicyEvaluator? policyEvaluator,
        IToolApprovalEvaluator? approvalEvaluator,
        IToolInvocationRecorder? recorder,
        IToolResultNormalizer? resultNormalizer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ToolDispatchStageCatalog.RegisterBuiltIns(
            _registry,
            policyEvaluator,
            approvalEvaluator,
            recorder,
            resultNormalizer);
    }

    /// <summary>Registers the built-in defaults the agent layer owns.</summary>
    /// <param name="factory">The constructed factory these defaults are sequenced behind, so one that needs it is visible to the first agent it builds.</param>
    public void RegisterAgentContributions(AgentFactory factory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(factory);
        CompactableToolPolicyCatalog.RegisterBuiltIns(_registry);
        CompactionSummarizerCatalog.RegisterBuiltIns(_registry);
        AgentContextSourceCatalog.RegisterBuiltIns(_registry);
    }

    /// <summary>Registers the built-in defaults the session layer owns, so a plugin can replace them by target name.</summary>
    /// <remarks>Scope-owned, not self-guarding: the instances are minted per start, so a restart must re-register its own.</remarks>
    public void RegisterSessionContributions(
        ICommitMessageSuggester? commitMessageSuggest,
        IWelcomeSuggester? welcomeSuggestions)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var registrar = _registry.CreateRegistrar(ContributionOrigin.Builtin);
        _owned.Add(registrar);
        SuggestionServiceCatalog.RegisterBuiltIns(_registry, commitMessageSuggest, welcomeSuggestions, registrar);
    }

    /// <summary>Registers the composed tool sources as contributions, spacing orders by ten to leave room between them.</summary>
    public IReadOnlyList<IToolSource> RegisterToolSources(IReadOnlyList<CollectedToolSource> sources)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(sources);

        var registrars = new Dictionary<ContributionOrigin, IContributionRegistrar>();
        using (_registry.BeginBatch())
        {
            for (var index = 0; index < sources.Count; index++)
            {
                var (source, origin) = sources[index];
                if (!registrars.TryGetValue(origin, out var registrar))
                {
                    registrars[origin] = registrar = _registry.CreateRegistrar(origin);
                    _owned.Add(registrar);
                }

                registrar.Add(source, new ContributionOptions(Order: index * 10) { OwnsContribution = false });
            }
        }

        return _registry.ResolveHostOwned();
    }

    /// <summary>Starts fanning thread runtime signals out to the contribution point, off the thread that raises them.</summary>
    public ThreadRuntimeSignalDispatcher AttachRuntimeSignals(ILoggerFactory? loggerFactory = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _runtimeSignalDispatcher ??= new ThreadRuntimeSignalDispatcher(_registry, loggerFactory);
    }

    /// <summary>Starts propagating registry mutations into per-thread agent invalidations and context page releases.</summary>
    public void AttachPropagation(ISessionService sessionService, IContextPageManager? contextPageManager = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (sessionService is IThreadAgentRefreshService refreshService)
            _propagationBridge = new ContributionPropagationBridge(_registry, refreshService, contextPageManager);
    }

    /// <summary>Removes every contribution and subscription this scope owns. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        // Subscriptions first, so the disposals below cannot schedule work against services going away.
        _propagationBridge?.Dispose();
        _propagationBridge = null;
        _runtimeSignalDispatcher?.Dispose();
        _runtimeSignalDispatcher = null;
        for (var index = _owned.Count - 1; index >= 0; index--)
            _owned[index].Dispose();
        _owned.Clear();
    }
}
