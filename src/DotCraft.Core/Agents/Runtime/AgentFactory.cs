using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Tracing;
using DotCraft.Hooks;
using DotCraft.Memory;
using DotCraft.Dreams;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.Agents;

/// <summary>
/// Factory for creating AI agents with tool aggregation from sources.
/// Tool declarations are projected from immutable effective snapshots.
/// </summary>
public sealed class AgentFactory : IAsyncDisposable
{
    private readonly AppConfig _config;
    private readonly IChatClient _chatClient;
    private readonly ConcurrentDictionary<string, TokenTracker> _tokenTrackers = new();
    private readonly ConcurrentDictionary<CompactionPipelineKey, CompactionPipeline> _compactionPipelines = new();
    private readonly TraceCollector? _traceCollector;
    private readonly HashSet<string> _globalEnabledToolNames;
    private readonly AgentRuntimeContext _runtimeContext;
    private readonly IReadOnlyList<IToolSource> _toolSources;
    private readonly ChatClientRegistry _chatClientRegistry;
    private readonly IChatClient? _compactionChatClientOverride;
    private static readonly ConcurrentDictionary<MethodInfo, bool> StreamArgumentsOptOutCache = new();
    private readonly CustomCommandLoader? _customCommandLoader;
    private readonly PlanStore? _planStore;
    private readonly Action<string, StructuredPlan>? _onPlanUpdated;
    private readonly HookRunner? _hookRunner;
    private readonly MemoryStore _memoryStore;
    private readonly Action<string>? _onConsolidatorStatus;
    private readonly IMemoryConsolidator? _memoryConsolidatorOverride;
    private readonly IToolDispatcher _toolDispatcher;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<AgentFactory> _logger;

    /// <summary>
    /// Creates a new AgentFactory with tool sources.
    /// </summary>
    public AgentFactory(
        string dotcraftPath,
        string workspacePath,
        AppConfig config,
        MemoryStore memoryStore,
        SkillsLoader skillsLoader,
        IApprovalService approvalService,
        PathBlacklist? blacklist,
        AgentRuntimeContext? runtimeContext = null,
        TraceCollector? traceCollector = null,
        CustomCommandLoader? customCommandLoader = null,
        PlanStore? planStore = null,
        Action<string, StructuredPlan>? onPlanUpdated = null,
        Action<string>? onConsolidatorStatus = null,
        HookRunner? hookRunner = null,
        ChatClientRegistry? chatClientRegistry = null,
        IChatClient? chatClient = null,
        IMemoryConsolidator? memoryConsolidator = null,
        IChatClient? compactionChatClient = null,
        IContextPageManager? contextPageManager = null,
        IToolDispatcher? toolDispatcher = null,
        IEnumerable<IToolSource>? toolSources = null,
        ILoggerFactory? loggerFactory = null)
    {
        _config = config;
        _traceCollector = traceCollector;
        _customCommandLoader = customCommandLoader;
        _planStore = planStore;
        _onPlanUpdated = onPlanUpdated;
        _hookRunner = hookRunner;
        _memoryStore = memoryStore;
        _onConsolidatorStatus = onConsolidatorStatus;
        _memoryConsolidatorOverride = memoryConsolidator;
        _compactionChatClientOverride = compactionChatClient;
        _toolDispatcher = toolDispatcher ?? new ToolDispatcher();
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<AgentFactory>();
        _globalEnabledToolNames = ResolveGlobalEnabledToolNames(_config);
        _chatClientRegistry = chatClientRegistry
                              ?? runtimeContext?.ChatClientRegistry
                              ?? new ChatClientRegistry(new ModelProviderRegistry([]));

        var mainRuntime = _chatClientRegistry.ResolveMainRuntime(config);
        var mainModel = mainRuntime.Model;
        var mainCompactionConfig = ModelCatalog.ResolveCompactionConfig(config, mainModel);
        _chatClient = chatClient ?? _chatClientRegistry.GetChatClient(mainRuntime);
        var consolidationRuntime = _chatClientRegistry.ResolveConsolidationRuntime(
            config,
            mainRuntime.ProviderId,
            mainModel);
        var maintenanceMainChatClient = ProviderChatClientAdapters.CreateRequestAdaptedClient(
            _chatClient,
            config,
            mainRuntime,
            useDefaultReasoning: false);
        var consolidationChatClient = chatClient ?? _chatClientRegistry.GetChatClient(consolidationRuntime);
        var legacyConsolidator = new MemoryConsolidator(
            ProviderChatClientAdapters.CreateRequestAdaptedClient(
                consolidationChatClient,
                config,
                consolidationRuntime,
                useDefaultReasoning: false),
            memoryStore,
            onConsolidatorStatus);
        Consolidator = _memoryConsolidatorOverride
            ?? new MemoryForkConsolidator(
                new MaintenanceForkRunner(
                    maintenanceMainChatClient,
                    cacheOptions: new MaintenanceForkCacheOptions(
                        mainRuntime.Protocol,
                        _config.PromptCaching,
                        mainModel)),
                legacyConsolidator,
                memoryStore,
                mainModel,
                consolidationRuntime.Model,
                mainCompactionConfig.BlockingLimit(),
                workspacePath);

        CompactionPipeline = new CompactionPipeline(
            mainCompactionConfig,
            ProviderChatClientAdapters.CreateRequestAdaptedClient(
                _compactionChatClientOverride ?? _chatClient,
                config,
                mainRuntime,
                useDefaultReasoning: false),
            _traceCollector,
            new MaintenanceForkCacheOptions(
                mainRuntime.Protocol,
                _config.PromptCaching,
                mainModel));

        // Build the source-neutral runtime context.
        _runtimeContext = runtimeContext ?? new AgentRuntimeContext
        {
            Config = config,
            ChatClient = _chatClient,
            ChatClientRegistry = _chatClientRegistry,
            EffectiveProviderId = mainRuntime.ProviderId,
            EffectiveProviderProtocol = mainRuntime.Protocol,
            EffectiveMainModel = mainModel,
            WorkspacePath = workspacePath,
            BotPath = dotcraftPath,
            MemoryStore = memoryStore,
            DreamStore = new DreamStore(dotcraftPath),
            SkillsLoader = skillsLoader,
            ContextPageManager = contextPageManager,
            ApprovalService = approvalService,
            PathBlacklist = blacklist,
            TraceCollector = traceCollector
        };

        _toolSources = (toolSources ?? [])
            .Append(new ModeSupplementalToolSource(_planStore, _onPlanUpdated))
            .ToArray();
    }

    /// <summary>
    /// Process-level agent runtime context (workspace root, memory, skills).
    /// Per-thread overrides are passed to the snapshot and agent construction methods.
    /// </summary>
    public AgentRuntimeContext RuntimeContext => _runtimeContext;

    /// <summary>Gets the constructor-injected default tool sources.</summary>
    public IReadOnlyList<IToolSource> ToolSources => _toolSources;

    /// <summary>Releases resources owned by thread-scoped sources.</summary>
    public async ValueTask ReleaseThreadToolResourcesAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        foreach (var source in _toolSources.OfType<IThreadScopedToolSource>())
            await source.ReleaseThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the last created tools for inspection.
    /// </summary>
    public IReadOnlyList<AITool>? LastCreatedTools { get; private set; }

    /// <summary>
    /// Gets the plan store for persisting plan files.
    /// </summary>
    public PlanStore? PlanStore => _planStore;

    /// <summary>
    /// Gets the layered context-compaction pipeline (auto / reactive / manual).
    /// </summary>
    public CompactionPipeline CompactionPipeline { get; }

    /// <summary>
    /// Gets the context-compaction pipeline for a thread's effective main model.
    /// </summary>
    public CompactionPipeline GetCompactionPipeline(
        string sessionKey,
        string? providerIdOverride = null,
        string? modelOverride = null,
        AppConfig? configOverride = null,
        ContextWindowMode? contextWindowModeOverride = null)
    {
        var effectiveConfig = configOverride ?? _config;
        var runtime = _chatClientRegistry.ResolveMainRuntime(effectiveConfig, providerIdOverride, modelOverride);
        var effectiveMainModel = runtime.Model;
        var compactionConfig = ModelCatalog.ResolveCompactionConfig(
            effectiveConfig,
            effectiveMainModel,
            contextWindowModeOverride ?? effectiveConfig.Compaction.ContextWindowMode);
        var key = CompactionPipelineKey.From(
            string.IsNullOrWhiteSpace(sessionKey) ? string.Empty : sessionKey.Trim(),
            runtime,
            compactionConfig);

        return _compactionPipelines.GetOrAdd(key, static (pipelineKey, state) =>
        {
            var (factory, resolvedConfig, config) = state;
            var runtime = pipelineKey.ToRuntime();
            var baseChatClient = factory._compactionChatClientOverride
                ?? factory._chatClientRegistry.GetChatClient(runtime);
            return new CompactionPipeline(
                resolvedConfig,
                ProviderChatClientAdapters.CreateRequestAdaptedClient(
                    baseChatClient,
                    config,
                    runtime,
                    useDefaultReasoning: false),
                factory._traceCollector,
                new MaintenanceForkCacheOptions(
                    pipelineKey.ProviderProtocol,
                    config.PromptCaching,
                    pipelineKey.Model));
        }, (this, compactionConfig, effectiveConfig));
    }

    /// <summary>
    /// Creates a provider-aware memory consolidator for the current thread runtime.
    /// </summary>
    public IMemoryConsolidator? CreateConsolidatorForRuntime(
        AppConfig config,
        string? providerIdOverride,
        string? modelOverride,
        ContextWindowMode? contextWindowModeOverride = null)
    {
        if (_memoryConsolidatorOverride != null)
            return _memoryConsolidatorOverride;

        var mainRuntime = _chatClientRegistry.ResolveMainRuntime(config, providerIdOverride, modelOverride);
        var consolidationRuntime = _chatClientRegistry.ResolveConsolidationRuntime(
            config,
            mainRuntime.ProviderId,
            mainRuntime.Model);
        var fallback = new MemoryConsolidator(
            ProviderChatClientAdapters.CreateRequestAdaptedClient(
                _chatClientRegistry.GetChatClient(consolidationRuntime),
                config,
                consolidationRuntime,
                useDefaultReasoning: false),
            _memoryStore,
            _onConsolidatorStatus);

        return new MemoryForkConsolidator(
            new MaintenanceForkRunner(
                ProviderChatClientAdapters.CreateRequestAdaptedClient(
                    _chatClientRegistry.GetChatClient(mainRuntime),
                    config,
                    mainRuntime,
                    useDefaultReasoning: false),
                _traceCollector,
                new MaintenanceForkCacheOptions(
                    mainRuntime.Protocol,
                    config.PromptCaching,
                    mainRuntime.Model)),
            fallback,
            _memoryStore,
            mainRuntime.Model,
            consolidationRuntime.Model,
            ModelCatalog.ResolveCompactionConfig(
                config,
                mainRuntime.Model,
                contextWindowModeOverride ?? config.Compaction.ContextWindowMode).BlockingLimit(),
            _runtimeContext.WorkspacePath);
    }

    /// <summary>
    /// Gets the memory consolidator for persisting conversation knowledge.
    /// Session Core drives consolidation independently from context
    /// compaction, using completed thread history as input.
    /// </summary>
    public IMemoryConsolidator? Consolidator { get; }

    /// <summary>
    /// Gets or creates a token tracker for the specified session.
    /// </summary>
    public TokenTracker GetOrCreateTokenTracker(string sessionKey)
    {
        return _tokenTrackers.GetOrAdd(sessionKey, _ => new TokenTracker());
    }

    /// <summary>
    /// Returns the token tracker for the specified session when one already exists.
    /// </summary>
    public TokenTracker? TryGetTokenTracker(string sessionKey)
    {
        return _tokenTrackers.TryGetValue(sessionKey, out var tracker) ? tracker : null;
    }

    /// <summary>
    /// Removes the token tracker for the specified session.
    /// </summary>
    public void RemoveTokenTracker(string sessionKey)
    {
        _tokenTrackers.TryRemove(sessionKey, out _);
        CompactionPipeline.Forget(sessionKey);
        foreach (var pair in _compactionPipelines.Where(pair =>
                     string.Equals(pair.Key.SessionKey, sessionKey, StringComparison.Ordinal)).ToArray())
        {
            pair.Value.Forget(sessionKey);
            _compactionPipelines.TryRemove(pair.Key, out _);
        }
    }

    /// <summary>
    /// Creates default declarations from an immutable host snapshot.
    /// </summary>
    public List<AITool> CreateDefaultTools() => CreateDefaultTools(_runtimeContext);

    /// <summary>
    /// Creates default tools using the given tool context (e.g. per-thread workspace override).
    /// </summary>
    public List<AITool> CreateDefaultTools(AgentRuntimeContext toolContext)
    {
        var planning = CreateHostPlanningContext(toolContext, AgentMode.Agent);
        var snapshot = BuildToolSnapshotAsync(_toolSources, planning, toolContext)
            .AsTask().GetAwaiter().GetResult();
        return ProjectSnapshotTools(snapshot);
    }

    /// <summary>Builds an immutable effective snapshot from source registrations.</summary>
    public async ValueTask<EffectiveToolSnapshot> BuildToolSnapshotAsync(
        IEnumerable<IToolSource> sources,
        ToolPlanningContext planningContext,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await new EffectiveToolSnapshotBuilder()
            .BuildAsync(sources, planningContext, cancellationToken)
            .ConfigureAwait(false);
        return ApplyGlobalToolExposure(snapshot);
    }

    /// <summary>Builds a snapshot including capabilities hosted by the selected provider.</summary>
    public async ValueTask<EffectiveToolSnapshot> BuildToolSnapshotAsync(
        IEnumerable<IToolSource> sources,
        ToolPlanningContext planningContext,
        AgentRuntimeContext runtimeContext,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await new EffectiveToolSnapshotBuilder().BuildAsync(
                sources,
                planningContext,
                ProviderHostedCapabilityPlanner.Build(runtimeContext),
                cancellationToken)
            .ConfigureAwait(false);
        return ApplyGlobalToolExposure(snapshot);
    }

    private EffectiveToolSnapshot ApplyGlobalToolExposure(EffectiveToolSnapshot snapshot)
    {
        if (_globalEnabledToolNames.Count == 0)
            return snapshot;

        return snapshot.WithModelExposure(definition =>
            _globalEnabledToolNames.Contains(snapshot.ProviderFlatNames[definition.Name]));
    }

    /// <summary>Dispatches a host or app call through an already frozen snapshot.</summary>
    public ValueTask<ToolExecutionResult> DispatchToolAsync(
        EffectiveToolSnapshot snapshot,
        ToolName toolName,
        JsonObject arguments,
        ToolInvocationRequest request,
        CancellationToken cancellationToken = default) =>
        _toolDispatcher.DispatchAsync(snapshot, toolName, arguments, request, cancellationToken);

    /// <summary>Projects model-visible declarations from a frozen effective snapshot.</summary>
    public static List<AITool> ProjectSnapshotTools(EffectiveToolSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.ModelVisibleDefinitions
            .Select(definition => (AITool)new SnapshotToolDeclarationFunction(
                snapshot.ProviderFlatNames[definition.Name],
                definition,
                definition.Name.Namespace == null
                    ? null
                    : snapshot.NamespaceDescriptions.GetValueOrDefault(definition.Name.Namespace)))
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .ToList();
    }

    internal static AIFunction ProjectSnapshotDefinition(
        EffectiveToolSnapshot snapshot,
        ToolDefinition definition) =>
        new SnapshotToolDeclarationFunction(
            snapshot.ProviderFlatNames[definition.Name],
            definition,
            definition.Name.Namespace == null
                ? null
                : snapshot.NamespaceDescriptions.GetValueOrDefault(definition.Name.Namespace));

    /// <summary>
    /// Creates a schema-stable tool list for the given <see cref="AgentMode"/>.
    /// Mode restrictions are enforced at invocation time.
    /// </summary>
    public List<AITool> CreateToolsForMode(AgentMode mode) => CreateToolsForMode(mode, _runtimeContext);

    /// <summary>
    /// Creates tools for the given mode using the specified tool context (e.g. per-thread workspace override).
    /// </summary>
    public List<AITool> CreateToolsForMode(AgentMode mode, AgentRuntimeContext toolContext)
    {
        var tools = CreateDefaultTools(toolContext);

        tools = ApplyResultLimits(tools, toolContext.WorkspacePath);
        tools = SortTools(ToolSchemaSanitizer.SanitizeTools(tools));

        return tools;
    }

    /// <summary>
    /// Creates the default AI agent with all registered tools.
    /// </summary>
    public ChatClientAgent CreateDefaultAgent()
        => CreateAgentForMode(AgentMode.Agent);

    /// <summary>
    /// Creates an AI agent configured for the specified mode.
    /// </summary>
    public ChatClientAgent CreateAgentForMode(AgentMode mode, AgentModeManager? modeManager = null)
    {
        var planning = CreateHostPlanningContext(_runtimeContext, mode);
        var snapshot = BuildToolSnapshotAsync(_toolSources, planning, _runtimeContext)
            .AsTask().GetAwaiter().GetResult();
        var tools = ProjectSnapshotTools(snapshot);
        return CreateAgentWithToolsAndSnapshot(
            tools,
            snapshot,
            planning,
            modeManager,
            _runtimeContext);
    }

    private static ToolPlanningContext CreateHostPlanningContext(
        AgentRuntimeContext context,
        AgentMode mode) =>
        new(
            context.CurrentThreadId ?? "host",
            turnId: null,
            context.WorkspacePath,
            mode.ToString().ToLowerInvariant(),
            profile: null,
            providerCapabilities: context.CurrentThreadSource?.SubAgent is null ? [] : ["subagent-child"],
            revision: 1,
            workspaceRoots: context.WorkspaceRoots);

    /// <summary>
    /// Creates an AI agent with the specified tools.
    /// </summary>
    public ChatClientAgent CreateAgentWithTools(List<AITool> tools, AgentModeManager? modeManager = null) =>
        BuildAgent(tools, modeManager, _runtimeContext, instructions: null);

    /// <summary>
    /// Creates an AI agent with the specified tools and tool context (e.g. per-thread workspace override).
    /// </summary>
    public ChatClientAgent CreateAgentWithTools(List<AITool> tools, AgentModeManager? modeManager, AgentRuntimeContext toolContext) =>
        BuildAgent(tools, modeManager, toolContext, instructions: null);

    /// <summary>Creates an agent whose source-backed declarations execute through the common dispatcher.</summary>
    public ChatClientAgent CreateAgentWithSnapshot(
        EffectiveToolSnapshot snapshot,
        ToolPlanningContext planningContext,
        AgentModeManager? modeManager,
        AgentRuntimeContext toolContext,
        string? instructions = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(planningContext);
        return CreateAgentWithToolsAndSnapshot(
            ProjectSnapshotTools(snapshot),
            snapshot,
            planningContext,
            modeManager,
            toolContext,
            instructions);
    }

    /// <summary>
    /// Creates an agent with a transitional mixed surface. Source-backed declarations use the
    /// dispatcher while unrelated provider-hosted functions keep their
    /// existing invocation path.
    /// </summary>
    public ChatClientAgent CreateAgentWithToolsAndSnapshot(
        List<AITool> tools,
        EffectiveToolSnapshot snapshot,
        ToolPlanningContext planningContext,
        AgentModeManager? modeManager,
        AgentRuntimeContext toolContext,
        string? instructions = null)
    {
        PrepareSnapshotDeferredTools(snapshot, toolContext, tools);
        return BuildAgent(
            tools,
            modeManager,
            toolContext,
            instructions,
            (invocation, cancellationToken) => DispatchSnapshotInvocationAsync(
                snapshot,
                planningContext,
                invocation,
                cancellationToken));
    }

    private void PrepareSnapshotDeferredTools(
        EffectiveToolSnapshot snapshot,
        AgentRuntimeContext context,
        List<AITool> directTools)
    {
        context.DeferredToolActivationIndex = null;
        var registration = snapshot.Registrations.Values
            .FirstOrDefault(DeferredToolSearchRuntime.IsRegistration);
        if (registration?.Binding.Runtime is not DeferredToolSearchRuntime runtime)
            return;

        var providerFlatName = snapshot.ProviderFlatNames[registration.Definition.Name];
        directTools.RemoveAll(tool => string.Equals(tool.Name, providerFlatName, StringComparison.Ordinal));
        context.DeferredToolActivationIndex = runtime.ActivationIndex;
        if (runtime.Plan.Mode == DeferredToolLoadingMode.Native)
        {
            directTools.Add(string.Equals(
                    runtime.Plan.ProviderProtocol,
                    ModelProviderProtocols.Anthropic,
                    StringComparison.Ordinal)
                ? new AnthropicToolSearchTool(runtime.ActivationIndex, runtime.Plan.MaxSearchResults)
                : new NativeToolSearchTool(runtime.ActivationIndex, runtime.Plan.MaxSearchResults));
        }
        else
        {
            directTools.Add(ToolSearchTool.CreateCanonicalFunction(
                runtime.ActivationIndex,
                runtime.Plan.MaxSearchResults));
        }
    }

    /// <summary>
    /// Creates an AI agent with explicit system instructions (e.g. ephemeral commit-message assistant).
    /// </summary>
    public ChatClientAgent CreateAgentWithTools(
        List<AITool> tools,
        AgentModeManager? modeManager,
        AgentRuntimeContext toolContext,
        string? instructions) =>
        BuildAgent(tools, modeManager, toolContext, instructions);

    private ChatClientAgent BuildAgent(
        List<AITool> tools,
        AgentModeManager? modeManager,
        AgentRuntimeContext ctx,
        string? instructions = null,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>>? functionInvoker = null)
    {
        // Snapshot-backed declarations execute through ToolDispatcher, which owns the
        // common policy and hook pipeline. Keep client-side evaluators only
        // for host-created agents that do not have a dispatcher invocation delegate.
        var usesSnapshotDispatcher = functionInvoker != null;
        tools = SortTools(ToolSchemaSanitizer.SanitizeTools(tools));
        LastCreatedTools = tools;

        var deferredRegistry = ctx.DeferredToolActivationIndex;

        // ChatClientBuilder applies earlier Use calls outside later ones:
        // TracingChatClient => StreamingFunctionInvokingChatClient => [DynamicToolInjectionChatClient]
        // => ImageContentSanitizingChatClient => [AnthropicDeferredToolLoadingChatClient]
        // => provider-specific clients.
        var chatClientBuilder = new ChatClientBuilder(ctx.ChatClient);
        if (_traceCollector != null)
        {
            var tc = _traceCollector;
            chatClientBuilder.Use(innerClient => new TracingChatClient(innerClient, tc));
        }
        var streamOptOutTools = BuildStreamOptOutToolNames(
            tools, deferredRegistry?.DeferredTools.Values);
        chatClientBuilder.Use(innerClient =>
        {
            var fic = new StreamingFunctionInvokingChatClient(innerClient)
            {
                AllowConcurrentInvocation = true,
                EnableToolCallArgumentPreviews = true,
                ModeToolPolicy = usesSnapshotDispatcher
                    ? null
                    : BuildInvocationPolicy(modeManager, ctx.ToolInvocationPolicy),
                ToolCallPolicy = usesSnapshotDispatcher ? null : ctx.ToolCallPolicy,
                IsStreamableTool = name => !streamOptOutTools.Contains(name)
            };
            fic.FunctionInvoker = functionInvoker;
            if (deferredRegistry != null)
                fic.AdditionalTools = deferredRegistry.ActivatedToolsList;
            return fic;
        });
        if (deferredRegistry?.Mode == DeferredToolLoadingMode.Simulated)
        {
            var registry = deferredRegistry;
            var tc = _traceCollector;
            var hr = usesSnapshotDispatcher ? null : _hookRunner;
            chatClientBuilder.Use(innerClient => new DynamicToolInjectionChatClient(innerClient, registry, tc, hr));
        }
        chatClientBuilder.Use(innerClient => new ImageContentSanitizingChatClient(innerClient));
        var runtime = ctx.ChatClientRegistry.ResolveMainRuntime(
            ctx.Config,
            ctx.EffectiveProviderId,
            ctx.EffectiveMainModel);
        var isNativeSubAgent = ctx.CurrentThreadSource?.SubAgent is { } subAgentSource
            && string.Equals(
                subAgentSource.RuntimeType,
                NativeSubAgentRuntime.RuntimeTypeName,
                StringComparison.OrdinalIgnoreCase);
        ProviderChatClientAdapters.UseProviderAdapters(
            chatClientBuilder,
            ctx.Config,
            runtime,
            ctx.EffectiveReasoning,
            ctx.EffectiveSpeed,
            ctx.Config.PromptCaching,
            _traceCollector);
        var configuredChatClient = chatClientBuilder.Build();
        var chatOptions = CreateChatOptions(tools, ctx.EffectiveReasoning, runtime, instructions);
        if (ProviderHostedCapabilityPlanner.Build(ctx).ImageGenerationEnabled)
            _chatClientRegistry.GetProviderService<IProviderHostedToolAdapter>(runtime)?
                .Configure(chatOptions, new HashSet<string>(StringComparer.Ordinal) { "image_generation" });

        MemoryContextProvider? contextProvider = null;

        // Custom instructions: skip MemoryContextProvider so ChatOptions.Instructions is the system prompt (e.g. commit-suggest).
        if (string.IsNullOrWhiteSpace(instructions))
        {
            string? subAgentProfilesSection = null;
            if (tools.Any(t => string.Equals(t.Name, "SpawnAgent", StringComparison.OrdinalIgnoreCase)))
            {
                subAgentProfilesSection = SubAgentProfilePromptSectionBuilder.Build(
                    ctx.Config.SubAgentProfiles,
                    SubAgentProfileRegistry.KnownRuntimeTypes,
                    ctx.Config.SubAgent.DisabledProfiles);
            }

            // When deferred loading is active, derive connected server names from the
            // ToolServerMap so the system prompt can list them for the model.
            IReadOnlyList<string>? deferredServerNames = null;
            if (deferredRegistry != null && ctx.McpClientManager != null)
            {
                deferredServerNames = ctx.McpClientManager.ToolServerMap.Values
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            var skillVariantModeEnabled = string.Equals(
                ctx.Config.Skills.SelfLearning.VariantMode,
                "enabled",
                StringComparison.OrdinalIgnoreCase);
            var toolNames = tools.Select(static tool => tool.Name).ToArray();
            var sortedToolNames = Array.AsReadOnly(
                toolNames
                    .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
            var skillVariantTarget = SkillVariantStore.CreateTarget(
                ctx.EffectiveMainModel,
                ctx.WorkspacePath,
                ctx.Config.Tools.Sandbox.Enabled,
                ctx.Config.Permissions.DefaultApprovalPolicy.ToString(),
                toolNames);

            contextProvider = new MemoryContextProvider(
                    ctx.MemoryStore,
                    ctx.SkillsLoader,
                    ctx.BotPath,
                    ctx.WorkspacePath,
                    _traceCollector,
                    () => sortedToolNames,
                    _customCommandLoader,
                    sandboxEnabled: _config.Tools.Sandbox.Enabled,
                    deferredMcpServerNames: deferredServerNames,
                    subAgentProfilesSection: subAgentProfilesSection,
                    skillVariantModeEnabled: skillVariantModeEnabled,
                    skillVariantTarget: skillVariantTarget,
                    // Native SubAgent role text is a thread context item on every protocol so the
                    // child's instruction channel stays byte-identical to its parent's.
                    roleInstructions: isNativeSubAgent ? null : ctx.RoleInstructions,
                    contextPageManager: ctx.ContextPageManager,
                    dreamStore: ctx.DreamStore,
                    subAgentWaitAgentTimeoutOptions: SubAgentWaitAgentTimeoutOptions.FromConfig(ctx.Config.SubAgent),
                    threadId: ctx.CurrentThreadId,
                    threadSystemPromptContextProviders: ctx.ThreadSystemPromptContextProviders,
                    originChannel: ctx.CurrentOriginChannel,
                    workspaceRoots: ctx.WorkspaceRoots,
                    loggerFactory: _loggerFactory);
        }

        return new ChatClientAgent(
            configuredChatClient,
            chatOptions,
            contextProvider,
            name: "DotCraft");
    }

    private async ValueTask<object?> DispatchSnapshotInvocationAsync(
        EffectiveToolSnapshot snapshot,
        ToolPlanningContext planningContext,
        FunctionInvocationContext invocation,
        CancellationToken cancellationToken)
    {
        var canonicalName = ResolveInvocationToolName(snapshot, invocation.CallContent);
        if (canonicalName is null)
        {
            if (ProviderFunctionCallMetadata.TryGetNamespace(
                    invocation.CallContent,
                    out var unresolvedNamespace))
            {
                return $"{ToolErrorCodes.NotFound}: Tool " +
                       $"'{unresolvedNamespace}.{invocation.CallContent.Name}' is not available in this Turn.";
            }

            return await invocation.Function.InvokeAsync(invocation.Arguments, cancellationToken).ConfigureAwait(false);
        }

        var arguments = new JsonObject();
        foreach (var (key, value) in invocation.Arguments)
            arguments[key] = value is JsonNode node
                ? node.DeepClone()
                : JsonSerializer.SerializeToNode(value, SessionWireJsonOptions.Default);

        var executionContext = ToolExecutionRuntimeScope.Current;
        if (executionContext is not null
            && !string.Equals(planningContext.ThreadId, "host", StringComparison.Ordinal)
            && !string.Equals(executionContext.ThreadId, planningContext.ThreadId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The active tool execution scope does not match the planned thread.");
        }

        var invocationThreadId = executionContext?.ThreadId ?? planningContext.ThreadId;
        var invocationTurnId = executionContext?.TurnId ?? planningContext.TurnId;
        var result = await _toolDispatcher.DispatchAsync(
            snapshot,
            canonicalName.Value,
            arguments,
            new ToolInvocationRequest(
                invocationThreadId,
                invocationTurnId,
                invocation.CallContent.CallId,
                ToolInvocationAudience.Model,
                WorkspacePath: planningContext.WorkspacePath),
            cancellationToken).ConfigureAwait(false);
        if (result.Success && result.Directive == ToolExecutionDirective.TerminateTurn)
            invocation.Terminate = true;
        if (result.Success)
            return result.ProviderResult
                ?? (object?)(result.ContentItems is null ? null : result.ContentItems.ToList())
                ?? result.Content;

        var errorCode = result.Error?.Code ?? ToolErrorCodes.ExecutionFailed;
        var message = !string.IsNullOrWhiteSpace(result.Content)
            ? result.Content
            : result.Error is null
                ? $"{errorCode}: Tool execution failed."
                : $"{errorCode}: {result.Error.Message}";
        return StreamingFunctionInvokingChatClient.CreateToolFailureResult(
            invocation.CallContent.CallId,
            message,
            errorCode);
    }

    private static ToolName? ResolveInvocationToolName(
        EffectiveToolSnapshot snapshot,
        FunctionCallContent call)
    {
        if (ProviderFunctionCallMetadata.TryGetNamespace(call, out var toolNamespace))
            return snapshot.TryResolveProviderNamespacedName(toolNamespace, call.Name, out var composite)
                ? composite
                : null;

        return snapshot.TryResolveProviderFlatName(call.Name, out var flatName)
            ? flatName
            : null;
    }

    private sealed class SnapshotToolDeclarationFunction(
        string providerFlatName,
        ToolDefinition definition,
        string? namespaceDescription)
        : AIFunction, ICanonicalToolIdentityMetadata, IGeneratedToolMetadata
    {
        public override string Name => providerFlatName;
        public ToolName CanonicalToolName => definition.Name;
        public string ProviderFlatName => providerFlatName;
        public string? ToolNamespaceDescription => namespaceDescription;
        public override string Description => definition.Description;
        public override JsonElement JsonSchema => definition.InputSchema;
        public override JsonElement? ReturnJsonSchema => definition.OutputSchema;
        public override MethodInfo? UnderlyingMethod => null;
        public override JsonSerializerOptions JsonSerializerOptions => SessionWireJsonOptions.Default;
        public bool StreamArgumentsEnabled =>
            !definition.Annotations.TryGetValue("dotcraft/streamArguments", out var value)
            || value.ValueKind != JsonValueKind.False;
        public int? MaxResultChars =>
            definition.Annotations.TryGetValue("dotcraft/maxResultChars", out var value)
            && value.TryGetInt32(out var limit)
                ? limit
                : null;
        public string? Icon => null;
        public Func<IDictionary<string, object?>?, string>? DisplayFormatter => null;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<object?>(new InvalidOperationException(
                "Snapshot tool declarations must be invoked through the common dispatcher."));
    }

    /// <summary>
    /// Creates provider-specific reasoning options based on the current configuration.
    /// Returns <see langword="null"/> when reasoning is disabled.
    /// </summary>
    public ReasoningOptions? CreateReasoningOptions(AppConfig.ReasoningConfig? reasoningConfig = null)
    {
        return (reasoningConfig ?? _runtimeContext.EffectiveReasoning).ToOptions();
    }

    /// <summary>
    /// Creates a chat client with filtering tool call.
    /// </summary>
    public IChatClient CreateToolCallFilteringChatClient()
    {
        var deferredRegistry = _runtimeContext.DeferredToolActivationIndex;

        // ChatClientBuilder applies earlier Use calls outside later ones:
        // ToolCallFilteringChatClient => TracingChatClient => StreamingFunctionInvokingChatClient
        // => [DynamicToolInjectionChatClient] => ImageContentSanitizingChatClient
        // => [AnthropicDeferredToolLoadingChatClient] => provider-specific clients.
        var chatClientBuilder = new ChatClientBuilder(_chatClient);
        chatClientBuilder.Use(innerClient => new ToolCallFilteringChatClient(innerClient));
        if (_traceCollector != null)
        {
            chatClientBuilder.Use(innerClient => new TracingChatClient(innerClient, _traceCollector));
        }
        var streamOptOutTools = BuildStreamOptOutToolNames(
            CreateDefaultTools(),
            deferredRegistry?.DeferredTools.Values);
        chatClientBuilder.Use(innerClient =>
        {
            var fic = new StreamingFunctionInvokingChatClient(innerClient)
            {
                AllowConcurrentInvocation = true,
                EnableToolCallArgumentPreviews = true,
                IsStreamableTool = name => !streamOptOutTools.Contains(name)
            };
            if (deferredRegistry != null)
                fic.AdditionalTools = deferredRegistry.ActivatedToolsList;
            return fic;
        });
        if (deferredRegistry != null)
        {
            var registry = deferredRegistry;
            var tc = _traceCollector;
            var hr = _hookRunner;
            if (deferredRegistry.Mode == DeferredToolLoadingMode.Simulated)
                chatClientBuilder.Use(innerClient => new DynamicToolInjectionChatClient(innerClient, registry, tc, hr));
        }
        chatClientBuilder.Use(innerClient => new ImageContentSanitizingChatClient(innerClient));
        var runtime = _chatClientRegistry.ResolveMainRuntime(
            _config,
            _runtimeContext.EffectiveProviderId,
            _runtimeContext.EffectiveMainModel);
        ProviderChatClientAdapters.UseProviderAdapters(
            chatClientBuilder,
            _config,
            runtime,
            _runtimeContext.EffectiveReasoning,
            _runtimeContext.EffectiveSpeed,
            _config.PromptCaching,
            _traceCollector);
        return chatClientBuilder.Build();
    }

    /// <summary>
    /// Gets the hook runner, if configured.
    /// </summary>
    public HookRunner? HookRunner => _hookRunner;

    /// <summary>
    /// Wraps tools with hook interceptors when PreToolUse/PostToolUse hooks are configured.
    /// Each <see cref="AIFunction"/> is wrapped in a <see cref="HookWrappedFunction"/>
    /// that runs hooks before/after tool execution.
    /// Session ID is resolved dynamically from <see cref="Tracing.TracingChatClient.CurrentSessionKey"/>.
    /// </summary>
    public List<AITool> ApplyHooks(List<AITool> tools)
    {
        if (_hookRunner == null || !_hookRunner.HasToolHooks)
        {
            if (Diagnostics.DebugModeService.IsEnabled())
                _logger.LogDebug(
                    "Hook wrapping skipped (HookRunnerPresent: {HookRunnerPresent}, HasToolHooks: {HasToolHooks})",
                    _hookRunner != null,
                    _hookRunner?.HasToolHooks);
            return tools;
        }

        var wrappedCount = 0;
        var result = tools.Select<AITool, AITool>(tool => tool switch
        {
            AIFunction fn => Wrap(fn),
            _ => tool
        }).ToList();

        if (Diagnostics.DebugModeService.IsEnabled())
            _logger.LogDebug("Hook wrapping applied to {WrappedCount}/{ToolCount} tools", wrappedCount, tools.Count);

        return result;

        AITool Wrap(AIFunction fn)
        {
            wrappedCount++;
            return new HookWrappedFunction(fn, _hookRunner);
        }
    }

    /// <summary>
    /// Wraps each <see cref="AIFunction"/> with <see cref="ResultSizeLimitingFunction"/> so oversized
    /// tool outputs are spilled to disk with a preview. Skips functions already wrapped.
    /// </summary>
    public List<AITool> ApplyResultLimits(List<AITool> tools, string workspacePath)
    {
        var globalMax = _config.Tools.ResultLimits.MaxToolResultChars;
        var previewLines = _config.Tools.ResultLimits.SpillPreviewLines;

        return [.. tools.Select<AITool, AITool>(tool => tool switch
        {
            ToolSchemaSanitizingFunction => tool,
            AIFunction fn when fn is not ResultSizeLimitingFunction => Wrap(fn),
            _ => tool
        })];

        AITool Wrap(AIFunction fn)
        {
            var limit = GeneratedToolMetadataResolver.TryGet(fn, out var metadata) && metadata.MaxResultChars.HasValue
                ? metadata.MaxResultChars.Value
                : ToolResultProcessor.ResolveMaxResultChars(fn.Name, globalMax);
            return new ResultSizeLimitingFunction(fn, limit, workspacePath, previewLines);
        }
    }

    private static HashSet<string> ResolveGlobalEnabledToolNames(AppConfig config)
    {
        return config.EnabledTools.Count == 0
            ? []
            : new HashSet<string>(config.EnabledTools, StringComparer.OrdinalIgnoreCase);
    }

    private static List<AITool> SortTools(IEnumerable<AITool> tools) =>
        tools
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.GetType().FullName, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Builds the set of tool names that should opt out of streaming argument deltas.
    /// Generated tools expose the policy through generated metadata; legacy or dynamic
    /// wrappers fall back to inspecting <c>UnderlyingMethod</c> when one is available.
    /// </summary>
    internal static HashSet<string> BuildStreamOptOutToolNames(
        IEnumerable<AITool> primary,
        IEnumerable<AITool>? additional = null)
    {
        var optOut = new HashSet<string>(StringComparer.Ordinal);
        CollectOptOuts(primary, optOut);
        if (additional != null)
            CollectOptOuts(additional, optOut);
        return optOut;
    }

    private static void CollectOptOuts(IEnumerable<AITool> tools, HashSet<string> optOut)
    {
        foreach (var tool in tools)
        {
            if (tool is not AIFunction fn)
                continue;

            if (GeneratedToolMetadataResolver.TryGet(fn, out var metadata))
            {
                if (!metadata.StreamArgumentsEnabled)
                    optOut.Add(fn.Name);
                continue;
            }

            var method = fn.UnderlyingMethod;
            if (method is null)
                continue;

            if (StreamArgumentsOptOutCache.GetOrAdd(method, static methodInfo =>
                Attribute.GetCustomAttribute(
                    methodInfo,
                    typeof(StreamArgumentsAttribute),
                    inherit: true) is StreamArgumentsAttribute { Enabled: false }))
            {
                optOut.Add(fn.Name);
            }
        }
    }

    private static Func<FunctionInvocationContext, ModeToolPolicyDecision>? BuildInvocationPolicy(
        AgentModeManager? modeManager,
        Func<FunctionInvocationContext, ModeToolPolicyDecision>? threadPolicy)
    {
        Func<FunctionInvocationContext, ModeToolPolicyDecision>? modePolicy =
            modeManager == null ? null : new ModeToolPolicy(modeManager).Evaluate;
        if (modePolicy == null)
            return threadPolicy;
        if (threadPolicy == null)
            return modePolicy;

        return context =>
        {
            var modeDecision = modePolicy(context);
            return modeDecision.Kind == ModeToolPolicyDecisionKind.Allow
                ? threadPolicy(context)
                : modeDecision;
        };
    }

    private ChatOptions CreateChatOptions(
        IEnumerable<AITool> tools,
        AppConfig.ReasoningConfig reasoningConfig,
        EffectiveModelRuntime runtime,
        string? instructions = null)
    {
        var chatOptions = new ChatOptions
        {
            Tools = [.. tools],
            Reasoning = CreateReasoningOptions(reasoningConfig),
            AllowMultipleToolCalls = runtime.IsChatGptOAuth && runtime.IsOpenAIResponses
                ? runtime.SupportsParallelToolCalls
                : null
        };

        if (!string.IsNullOrWhiteSpace(instructions))
            chatOptions.Instructions = instructions;

        return chatOptions;
    }

    private readonly record struct CompactionPipelineKey(
        string SessionKey,
        string ProviderId,
        string ProviderProtocol,
        string Model,
        string EndPoint,
        string ApiKey,
        int NetworkTimeoutSeconds,
        int? MaxOutputTokens,
        int StreamMaxRetries,
        int StreamIdleTimeoutMs,
        string AuthMethod,
        string? ChatGptAccountId,
        bool UseResponsesLite,
        bool SupportsParallelToolCalls,
        bool AutoCompactEnabled,
        bool ReactiveCompactEnabled,
        int ContextWindow,
        int SummaryReserveTokens,
        int SummaryMaxOutputTokens,
        int AutoCompactBufferTokens,
        int WarningBufferTokens,
        int ErrorBufferTokens,
        int ManualCompactBufferTokens,
        int KeepRecentMinTokens,
        int KeepRecentMinGroups,
        int KeepRecentMaxTokens,
        bool MicrocompactEnabled,
        int MicrocompactKeepRecent,
        int MicrocompactGapMinutes,
        int MaxConsecutiveFailures)
    {
        public static CompactionPipelineKey From(
            string sessionKey,
            EffectiveModelRuntime runtime,
            CompactionConfig compaction) =>
            new(
                sessionKey,
                runtime.ProviderId,
                runtime.Protocol,
                runtime.Model,
                runtime.EndPoint,
                runtime.ApiKey,
                runtime.NetworkTimeoutSeconds,
                runtime.MaxOutputTokens,
                Math.Clamp(runtime.StreamMaxRetries, 0, ModelProviderDefaults.MaxStreamMaxRetries),
                Math.Max(1, runtime.StreamIdleTimeoutMs),
                ModelProviderAuthMethods.Normalize(runtime.AuthMethod),
                string.IsNullOrWhiteSpace(runtime.ChatGptAccountId) ? null : runtime.ChatGptAccountId.Trim(),
                runtime.UseResponsesLite,
                runtime.SupportsParallelToolCalls,
                compaction.AutoCompactEnabled,
                compaction.ReactiveCompactEnabled,
                compaction.ContextWindow,
                compaction.SummaryReserveTokens,
                compaction.SummaryMaxOutputTokens,
                compaction.AutoCompactBufferTokens,
                compaction.WarningBufferTokens,
                compaction.ErrorBufferTokens,
                compaction.ManualCompactBufferTokens,
                compaction.KeepRecentMinTokens,
                compaction.KeepRecentMinGroups,
                compaction.KeepRecentMaxTokens,
                compaction.MicrocompactEnabled,
                compaction.MicrocompactKeepRecent,
                compaction.MicrocompactGapMinutes,
                compaction.MaxConsecutiveFailures);

        public EffectiveModelRuntime ToRuntime() => new(
            ProviderId,
            Model,
            ProviderProtocol,
            DisplayName: ProviderId,
            ApiKey,
            EndPoint,
            NetworkTimeoutSeconds,
            MaxOutputTokens,
            IsImplicit: ModelProviderResolver.IsImplicitProviderId(ProviderId),
            ModelProviderCapabilities.ForProtocol(ProviderProtocol),
            StreamMaxRetries,
            StreamIdleTimeoutMs,
            AuthMethod,
            ChatGptAccountId,
            UseResponsesLite: UseResponsesLite,
            SupportsParallelToolCalls: SupportsParallelToolCalls);
    }

    /// <summary>
    /// Disposes all resources created by tool providers.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var source in _toolSources.OfType<IAsyncDisposable>())
            await source.DisposeAsync();
        foreach (var disposable in _runtimeContext.DisposableResources)
        {
            await disposable.DisposeAsync();
        }
        _runtimeContext.DisposableResources.Clear();
    }
}
