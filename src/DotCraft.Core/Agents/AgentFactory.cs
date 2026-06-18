using System.Collections.Concurrent;
using System.Reflection;
using DotCraft.Abstractions;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Tracing;
using DotCraft.GeneratedTools.Core;
using DotCraft.Hooks;
using DotCraft.Memory;
using DotCraft.Dreams;
using DotCraft.Plugins;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// Factory for creating AI agents with tool aggregation from providers.
/// Tools are aggregated from registered <see cref="IAgentToolProvider"/> instances.
/// </summary>
public sealed class AgentFactory : IAsyncDisposable
{
    private readonly AppConfig _config;
    private readonly IChatClient _chatClient;
    private readonly ConcurrentDictionary<string, TokenTracker> _tokenTrackers = new();
    private readonly ConcurrentDictionary<CompactionPipelineKey, CompactionPipeline> _compactionPipelines = new();
    private readonly TraceCollector? _traceCollector;
    private readonly HashSet<string> _globalEnabledToolNames;
    private readonly ToolProviderContext _toolProviderContext;
    private readonly IReadOnlyList<IAgentToolProvider> _toolProviders;
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

    /// <summary>
    /// Creates a new AgentFactory with tool providers.
    /// </summary>
    public AgentFactory(
        string dotcraftPath,
        string workspacePath,
        AppConfig config,
        MemoryStore memoryStore,
        SkillsLoader skillsLoader,
        IApprovalService approvalService,
        PathBlacklist? blacklist,
        IEnumerable<IAgentToolProvider> toolProviders,
        ToolProviderContext? toolProviderContext = null,
        TraceCollector? traceCollector = null,
        CustomCommandLoader? customCommandLoader = null,
        PlanStore? planStore = null,
        Action<string, StructuredPlan>? onPlanUpdated = null,
        Action<string>? onConsolidatorStatus = null,
        HookRunner? hookRunner = null,
        ChatClientRegistry? chatClientRegistry = null,
        IMemoryConsolidator? memoryConsolidator = null,
        IChatClient? compactionChatClient = null,
        IContextPageManager? contextPageManager = null)
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
        _globalEnabledToolNames = ResolveGlobalEnabledToolNames(_config);
        _chatClientRegistry = chatClientRegistry ?? toolProviderContext?.ChatClientRegistry ?? new ChatClientRegistry();

        var mainRuntime = _chatClientRegistry.ResolveMainRuntime(config);
        var mainModel = mainRuntime.Model;
        var mainCompactionConfig = ModelContextWindowCatalog.ResolveCompactionConfig(config, mainModel);
        _chatClient = _chatClientRegistry.GetChatClient(mainRuntime);
        var consolidationRuntime = _chatClientRegistry.ResolveConsolidationRuntime(
            config,
            mainRuntime.ProviderId,
            mainModel);
        var maintenanceMainChatClient = ProviderChatClientAdapters.CreateRequestAdaptedClient(
            _chatClient,
            config,
            mainRuntime,
            useDefaultReasoning: false);
        var legacyConsolidator = new MemoryConsolidator(
            ProviderChatClientAdapters.CreateRequestAdaptedClient(
                _chatClientRegistry.GetChatClient(consolidationRuntime),
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

        // Build tool provider context
        _toolProviderContext = toolProviderContext ?? new ToolProviderContext
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

        _toolProviders = toolProviders.ToList();
    }

    /// <summary>
    /// Process-level tool provider context (workspace root, memory, skills).
    /// Per-thread overrides are passed to <see cref="CreateToolsForMode(AgentMode, ToolProviderContext)"/>
    /// and the overload of <see cref="CreateAgentWithTools(List{AITool}, AgentModeManager?, ToolProviderContext)"/>.
    /// </summary>
    public ToolProviderContext ToolProviderContext => _toolProviderContext;

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
        AppConfig? configOverride = null)
    {
        var effectiveConfig = configOverride ?? _config;
        var runtime = _chatClientRegistry.ResolveMainRuntime(effectiveConfig, providerIdOverride, modelOverride);
        var effectiveMainModel = runtime.Model;
        var compactionConfig = ModelContextWindowCatalog.ResolveCompactionConfig(effectiveConfig, effectiveMainModel);
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
        string? modelOverride)
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
            ModelContextWindowCatalog.ResolveCompactionConfig(
                config,
                mainRuntime.Model).BlockingLimit(),
            _toolProviderContext.WorkspacePath);
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
    /// Creates default tools by aggregating all registered tool providers.
    /// Tools are ordered by provider priority (lower priority value = earlier in list).
    /// </summary>
    public List<AITool> CreateDefaultTools() => CreateDefaultTools(_toolProviderContext);

    /// <summary>
    /// Creates default tools using the given tool context (e.g. per-thread workspace override).
    /// </summary>
    public List<AITool> CreateDefaultTools(ToolProviderContext toolContext)
    {
        var tools = _toolProviders
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.GetType().FullName, StringComparer.Ordinal)
            .SelectMany(p => SortTools(p.CreateTools(toolContext)))
            .ToList();

        // Apply global tool filtering if configured
        if (_globalEnabledToolNames.Count > 0)
        {
            tools = tools
                .Where(t => _globalEnabledToolNames.Contains(t.Name))
                .ToList();
        }

        tools = DropConflictingPluginFunctions(tools);

        // Wrap tools with hook interceptors
        tools = ApplyHooks(tools);

        tools = ApplyResultLimits(tools, toolContext.WorkspacePath);
        tools = SortTools(ToolSchemaSanitizer.SanitizeTools(tools));

        return tools;
    }

    /// <summary>
    /// Creates tools from an explicit provider list (e.g. registered tool profile).
    /// </summary>
    public List<AITool> CreateToolsFromProviders(
        IReadOnlyList<IAgentToolProvider> providers,
        ToolProviderContext toolContext)
    {
        var tools = providers
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.GetType().FullName, StringComparer.Ordinal)
            .SelectMany(p => SortTools(p.CreateTools(toolContext)))
            .ToList();

        if (_globalEnabledToolNames.Count > 0)
        {
            tools = tools
                .Where(t => _globalEnabledToolNames.Contains(t.Name))
                .ToList();
        }

        tools = DropConflictingPluginFunctions(tools);

        tools = ApplyHooks(tools);

        tools = ApplyResultLimits(tools, toolContext.WorkspacePath);
        tools = SortTools(ToolSchemaSanitizer.SanitizeTools(tools));

        return tools;
    }

    /// <summary>
    /// Creates a schema-stable tool list for the given <see cref="AgentMode"/>.
    /// Mode restrictions are enforced at invocation time.
    /// </summary>
    public List<AITool> CreateToolsForMode(AgentMode mode) => CreateToolsForMode(mode, _toolProviderContext);

    /// <summary>
    /// Creates tools for the given mode using the specified tool context (e.g. per-thread workspace override).
    /// </summary>
    public List<AITool> CreateToolsForMode(AgentMode mode, ToolProviderContext toolContext)
    {
        var tools = CreateDefaultTools(toolContext);

        if (_planStore != null)
        {
            // Use GetActiveSessionKey for reliable session key retrieval across async boundaries
            var planTools = new PlanTools(_planStore, TracingChatClient.GetActiveSessionKey, _onPlanUpdated);
            tools.Add(GeneratedToolFunctions.PlanTools_CreatePlan(planTools));
            tools.Add(GeneratedToolFunctions.PlanTools_UpdateTodos(planTools));
            tools.Add(GeneratedToolFunctions.PlanTools_TodoWrite(planTools));
        }

        if (toolContext.CurrentThreadSource?.SubAgent == null)
        {
            var userInputTools = new RequestUserInputTools();
            tools.Add(GeneratedToolFunctions.RequestUserInputTools_RequestUserInput(userInputTools));
        }

        tools = ApplyResultLimits(tools, toolContext.WorkspacePath);
        tools = SortTools(ToolSchemaSanitizer.SanitizeTools(tools));

        return tools;
    }

    /// <summary>
    /// Creates the default AI agent with all registered tools.
    /// </summary>
    public AIAgent CreateDefaultAgent()
    {
        return CreateAgentWithTools(CreateDefaultTools());
    }

    /// <summary>
    /// Creates an AI agent configured for the specified mode.
    /// </summary>
    public AIAgent CreateAgentForMode(AgentMode mode, AgentModeManager? modeManager = null)
    {
        return CreateAgentWithTools(CreateToolsForMode(mode), modeManager);
    }

    /// <summary>
    /// Creates an AI agent with the specified tools.
    /// </summary>
    public AIAgent CreateAgentWithTools(List<AITool> tools, AgentModeManager? modeManager = null) =>
        BuildAgent(tools, modeManager, _toolProviderContext, instructions: null);

    /// <summary>
    /// Creates an AI agent with the specified tools and tool context (e.g. per-thread workspace override).
    /// </summary>
    public AIAgent CreateAgentWithTools(List<AITool> tools, AgentModeManager? modeManager, ToolProviderContext toolContext) =>
        BuildAgent(tools, modeManager, toolContext, instructions: null);

    /// <summary>
    /// Creates an AI agent with explicit system instructions (e.g. ephemeral commit-message assistant).
    /// </summary>
    public AIAgent CreateAgentWithTools(
        List<AITool> tools,
        AgentModeManager? modeManager,
        ToolProviderContext toolContext,
        string? instructions) =>
        BuildAgent(tools, modeManager, toolContext, instructions);

    private AIAgent BuildAgent(
        List<AITool> tools,
        AgentModeManager? modeManager,
        ToolProviderContext ctx,
        string? instructions = null)
    {
        tools = SortTools(ToolSchemaSanitizer.SanitizeTools(tools));
        LastCreatedTools = tools;

        var deferredRegistry = ctx.DeferredToolRegistry;

        // ChatClientBuilder applies earlier Use calls outside later ones:
        // TracingChatClient => StreamingFunctionInvokingChatClient => [DynamicToolInjectionChatClient]
        // => ImageContentSanitizingChatClient => provider-specific clients.
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
                ModeToolPolicy = BuildInvocationPolicy(modeManager, ctx.ToolInvocationPolicy),
                ToolCallPolicy = ctx.ToolCallPolicy,
                IsStreamableTool = name => !streamOptOutTools.Contains(name)
            };
            if (deferredRegistry != null)
                fic.AdditionalTools = deferredRegistry.ActivatedToolsList;
            return fic;
        });
        if (deferredRegistry?.Mode == DeferredToolLoadingMode.Simulated)
        {
            var registry = deferredRegistry;
            var tc = _traceCollector;
            var hr = _hookRunner;
            chatClientBuilder.Use(innerClient => new DynamicToolInjectionChatClient(innerClient, registry, tc, hr));
        }
        chatClientBuilder.Use(innerClient => new ImageContentSanitizingChatClient(innerClient));
        ProviderChatClientAdapters.UseProviderAdapters(
            chatClientBuilder,
            ctx.Config,
            ctx.ChatClientRegistry.ResolveMainRuntime(
                ctx.Config,
                ctx.EffectiveProviderId,
                ctx.EffectiveMainModel),
            ctx.EffectiveReasoning,
            ctx.Config.PromptCaching,
            _traceCollector);
        var configuredChatClient = chatClientBuilder.Build();

        var options = new ChatClientAgentOptions
        {
            Name = "DotCraft",
            UseProvidedChatClientAsIs = true,
            ChatOptions = CreateChatOptions(tools, ctx.EffectiveReasoning, instructions)
        };

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

            options.AIContextProviders =
            [
                new MemoryContextProvider(
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
                    promptProfile: ctx.PromptProfile,
                    roleInstructions: ctx.RoleInstructions,
                    contextPageManager: ctx.ContextPageManager,
                    dreamStore: ctx.DreamStore,
                    subAgentWaitAgentTimeoutOptions: SubAgentWaitAgentTimeoutOptions.FromConfig(ctx.Config.SubAgent),
                    threadId: ctx.CurrentThreadId,
                    threadSystemPromptContextProviders: ctx.ThreadSystemPromptContextProviders)
            ];
        }

        return configuredChatClient.AsAIAgent(options);
    }

    /// <summary>
    /// Creates provider-specific reasoning options based on the current configuration.
    /// Returns <see langword="null"/> when reasoning is disabled.
    /// </summary>
    public ReasoningOptions? CreateReasoningOptions(AppConfig.ReasoningConfig? reasoningConfig = null)
    {
        return (reasoningConfig ?? _toolProviderContext.EffectiveReasoning).ToOptions();
    }

    /// <summary>
    /// Creates a chat client with filtering tool call.
    /// </summary>
    public IChatClient CreateToolCallFilteringChatClient()
    {
        var deferredRegistry = _toolProviderContext.DeferredToolRegistry;

        // ChatClientBuilder applies earlier Use calls outside later ones:
        // ToolCallFilteringChatClient => TracingChatClient => StreamingFunctionInvokingChatClient
        // => [DynamicToolInjectionChatClient] => ImageContentSanitizingChatClient
        // => provider-specific clients.
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
            chatClientBuilder.Use(innerClient => new DynamicToolInjectionChatClient(innerClient, registry, tc, hr));
        }
        chatClientBuilder.Use(innerClient => new ImageContentSanitizingChatClient(innerClient));
        ProviderChatClientAdapters.UseProviderAdapters(
            chatClientBuilder,
            _config,
            _chatClientRegistry.ResolveMainRuntime(
                _config,
                _toolProviderContext.EffectiveProviderId,
                _toolProviderContext.EffectiveMainModel),
            _toolProviderContext.EffectiveReasoning,
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
    /// Session ID is resolved dynamically from <see cref="DashBoard.TracingChatClient.CurrentSessionKey"/>.
    /// </summary>
    public List<AITool> ApplyHooks(List<AITool> tools)
    {
        if (_hookRunner == null || !_hookRunner.HasToolHooks)
        {
            if (Diagnostics.DebugModeService.IsEnabled())
                Console.Error.WriteLine($"[Hooks] ApplyHooks: skipped (hookRunner={(_hookRunner == null ? "null" : "present")}, hasToolHooks={_hookRunner?.HasToolHooks})");
            return tools;
        }

        var wrappedCount = 0;
        var result = tools.Select<AITool, AITool>(tool => tool switch
        {
            AIFunction fn => Wrap(fn),
            _ => tool
        }).ToList();

        if (Diagnostics.DebugModeService.IsEnabled())
            Console.Error.WriteLine($"[Hooks] ApplyHooks: wrapped {wrappedCount}/{tools.Count} tools");

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

    private static List<AITool> DropConflictingPluginFunctions(List<AITool> tools)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<AITool>(tools.Count);
        foreach (var tool in tools)
        {
            if (string.IsNullOrWhiteSpace(tool.Name))
            {
                result.Add(tool);
                continue;
            }

            if (seen.Add(tool.Name))
            {
                result.Add(tool);
                continue;
            }

            if (tool is IPluginFunctionTool { PluginFunctionDescriptor: { } descriptor })
            {
                Console.Error.WriteLine(
                    $"[PluginFunction] Skipped duplicate function '{descriptor.Name}' from plugin '{descriptor.PluginId}'.");
                continue;
            }

            result.Add(tool);
        }

        return result;
    }

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
        string? instructions = null)
    {
        var chatOptions = new ChatOptions
        {
            Tools = [.. tools],
            Reasoning = CreateReasoningOptions(reasoningConfig)
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
        int MicrocompactTriggerCount,
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
                compaction.MicrocompactTriggerCount,
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
            ChatGptAccountId);
    }

    /// <summary>
    /// Disposes all resources created by tool providers.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var disposable in _toolProviderContext.DisposableResources)
        {
            await disposable.DisposeAsync();
        }
        _toolProviderContext.DisposableResources.Clear();
    }
}
