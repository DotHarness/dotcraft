using System.Collections.Concurrent;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Contributions;
using DotCraft.GeneratedTools.Core;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Tools.Sandbox;

/// <summary>Contributes thread-scoped sandbox file, shell, web, skill, and agent tools.</summary>
public sealed class SandboxToolSource(
    AppConfig config,
    ISandboxProvider sandboxProvider,
    ChatClientRegistry chatClientRegistry,
    SkillsLoader skillsLoader,
    IApprovalService approvalService,
    string dataDirectoryName,
    PathBlacklist? pathBlacklist = null,
    TraceCollector? traceCollector = null,
    ISkillMutationApplier? skillMutationApplier = null,
    IContextPageManager? contextPageManager = null,
    ILoggerFactory? loggerFactory = null,
    IContributionView? contributions = null)
    : AIFunctionToolSource, IThreadScopedToolSource, IThreadRetiredToolResourceSource, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, SandboxManagerState> _managerStates = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public override string SourceId => "sandbox-native";

    /// <inheritdoc />
    public override int Priority => 10;

    /// <inheritdoc />
    protected override string GetDescription(AIFunction function, ToolPlanningContext context) =>
        string.Equals(function.Name, nameof(AgentTools.SpawnAgent), StringComparison.Ordinal)
            ? SubAgentModelCatalogSnapshots.AppendToToolDescription(
                base.GetDescription(function, context),
                context.SubAgentModelCatalogSnapshot)
            : base.GetDescription(function, context);

    /// <inheritdoc />
    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
    {
        if (!config.Tools.Sandbox.Enabled)
            return [];

        var workspace = SandboxWorkspaceIdentity.Create(context.WorkspacePath, context.WorkspaceRoots);
        var state = _managerStates.GetOrAdd(context.ThreadId, static _ => new SandboxManagerState());
        var manager = state.GetOrCreate(
            workspace,
            () => new SandboxSessionManager(
                config.Tools.Sandbox,
                sandboxProvider,
                workspace.Cwd,
                dataDirectoryName,
                workspace.Roots,
                loggerFactory?.CreateLogger<SandboxSessionManager>()));
        var tools = new List<AIFunction>();
        var shellTools = new SandboxShellTools(
            new SandboxCommandClient(manager),
            config.Tools.Shell.Timeout,
            config.Tools.Shell.MaxOutputLength);
        tools.Add(GeneratedToolFunctions.SandboxShellTools_Exec(shellTools));

        var fileTools = new SandboxFileTools(manager, config.Tools.File.MaxFileSize);
        tools.Add(GeneratedToolFunctions.SandboxFileTools_ReadFile(fileTools));
        tools.Add(GeneratedToolFunctions.SandboxFileTools_WriteFile(fileTools));
        tools.Add(GeneratedToolFunctions.SandboxFileTools_EditFile(fileTools));
        tools.Add(GeneratedToolFunctions.SandboxFileTools_GrepFiles(fileTools));
        tools.Add(GeneratedToolFunctions.SandboxFileTools_FindFiles(fileTools));

        var mainRuntime = chatClientRegistry.ResolveMainRuntime(
            config,
            context.EffectiveProviderId,
            context.EffectiveMainModel);
        var subAgentRuntime = chatClientRegistry.ResolveSubAgentRuntime(
            config,
            mainRuntime.ProviderId,
            mainRuntime.Model);
        var subAgentPreference = ModelPreferenceRules.Find(
            config.SubAgent.ProviderPreferences,
            mainRuntime.ProviderId);
        var managerRuntime = new SubAgentManager(
            chatClientRegistry.GetSubAgentChatClient(config, mainRuntime.ProviderId, mainRuntime.Model),
            context.WorkspacePath,
            backgroundTerminalService: null,
            maxConcurrency: config.SubagentMaxConcurrency,
            shellTimeout: config.Tools.Shell.Timeout,
            requireApprovalOutsideWorkspace: context.RequireApprovalOutsideWorkspace
                ?? config.Tools.File.RequireApprovalOutsideWorkspace,
            reasoningConfig: subAgentPreference?.Reasoning ?? config.Reasoning,
            promptCachingConfig: config.PromptCaching,
            model: subAgentRuntime.Model,
            providerProtocol: subAgentRuntime.Protocol,
            blacklist: pathBlacklist,
            sandboxManager: manager,
            approvalService: approvalService,
            traceCollector: traceCollector,
            endpoint: subAgentRuntime.EndPoint,
            maxOutputTokens: subAgentRuntime.MaxOutputTokens,
            config: config,
            workspaceRoots: context.WorkspaceRoots,
            contributions: contributions);
        var coordinator = new SubAgentCoordinator(
            context.WorkspacePath,
            [new NativeSubAgentRuntime(managerRuntime), new CliOneshotRuntime()],
            config.SubAgentProfiles,
            approvalService,
            config.SubAgent.DisabledProfiles,
            externalCliSessionStore: null,
            enableExternalCliSessionResume: config.SubAgent.EnableExternalCliSessionResume,
            catalog: SubAgentProfileCatalog.Resolve(contributions, context.ThreadId));
        var agentTools = new AgentTools(
            subAgentManager: coordinator,
            subAgentRoles: config.SubAgent.Roles,
            maxSubAgentDepth: config.SubAgent.MaxDepth,
            subAgentPreference: subAgentPreference,
            appConfig: config,
            waitAgentTimeoutOptions: SubAgentWaitAgentTimeoutOptions.FromConfig(config.SubAgent),
            maxConcurrentSubAgents: config.SubAgent.MaxConcurrentSubAgents,
            modelCatalogSnapshot: context.SubAgentModelCatalogSnapshot,
            inheritedModel: context.EffectiveMainModel);
        tools.Add(GeneratedToolFunctions.AgentTools_SpawnAgent(agentTools));
        tools.Add(GeneratedToolFunctions.AgentTools_SendMessage(agentTools));
        tools.Add(GeneratedToolFunctions.AgentTools_FollowupTask(agentTools));
        tools.Add(GeneratedToolFunctions.AgentTools_WaitAgent(agentTools));
        tools.Add(GeneratedToolFunctions.AgentTools_ListAgents(agentTools));
        tools.Add(GeneratedToolFunctions.AgentTools_CloseAgent(agentTools));

        var webTools = new WebTools(
            config.Tools.Web.MaxChars,
            config.Tools.Web.Timeout,
            config.Tools.Web.SearchMaxResults,
            config.Tools.Web.SearchProvider);
        tools.Add(GeneratedToolFunctions.WebTools_WebSearch(webTools));
        tools.Add(GeneratedToolFunctions.WebTools_WebFetch(webTools));

        var target = SkillVariantStore.CreateTarget(
            context.EffectiveMainModel,
            context.WorkspacePath,
            sandboxEnabled: true,
            config.Permissions.DefaultApprovalPolicy.ToString(),
            tools.Select(tool => tool.Name).ToArray());
        var selfLearning = config.Skills.SelfLearning;
        var variantModeEnabled = string.Equals(
            selfLearning.VariantMode,
            "enabled",
            StringComparison.OrdinalIgnoreCase);
        tools.Add(GeneratedToolFunctions.SkillViewTool_SkillView(
            new SkillViewTool(skillsLoader, variantModeEnabled, target, traceCollector)));

        if (selfLearning.Enabled)
        {
            var baseApplier = skillMutationApplier ?? new WorkspaceFileSkillMutationApplier(skillsLoader);
            var applier = variantModeEnabled
                ? new VariantSkillMutationApplier(baseApplier, skillsLoader, target)
                : baseApplier;
            tools.Add(GeneratedToolFunctions.SkillManageTool_SkillManage(
                new SkillManageTool(applier, selfLearning, approvalService, contextPageManager)));
        }

        return tools;
    }

    /// <summary>Releases the sandbox owned by one thread.</summary>
    public async ValueTask ReleaseThreadAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_managerStates.TryRemove(threadId, out var state))
            await DisposeManagersAsync(state.TakeAll(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask ReleaseRetiredThreadResourcesAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_managerStates.TryGetValue(threadId, out var state))
            await DisposeManagersAsync(state.TakeRetired(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var threadId in _managerStates.Keys)
            await ReleaseThreadAsync(threadId).ConfigureAwait(false);
        _managerStates.Clear();
    }

    private static async ValueTask DisposeManagersAsync(
        IReadOnlyList<SandboxSessionManager> managers,
        CancellationToken cancellationToken)
    {
        foreach (var manager in managers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await manager.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class SandboxManagerState
    {
        private readonly object _sync = new();
        private SandboxManagerGeneration? _current;
        private readonly List<SandboxSessionManager> _retired = [];

        public SandboxSessionManager GetOrCreate(
            SandboxWorkspaceIdentity workspace,
            Func<SandboxSessionManager> create)
        {
            lock (_sync)
            {
                if (_current?.Workspace.Matches(workspace) == true)
                    return _current.Manager;

                var next = create();
                if (_current != null)
                    _retired.Add(_current.Manager);
                _current = new SandboxManagerGeneration(workspace, next);
                return next;
            }
        }

        public IReadOnlyList<SandboxSessionManager> TakeRetired()
        {
            lock (_sync)
            {
                if (_retired.Count == 0)
                    return [];
                var result = _retired.ToArray();
                _retired.Clear();
                return result;
            }
        }

        public IReadOnlyList<SandboxSessionManager> TakeAll()
        {
            lock (_sync)
            {
                var result = new List<SandboxSessionManager>(_retired.Count + 1);
                result.AddRange(_retired);
                _retired.Clear();
                if (_current != null)
                {
                    result.Add(_current.Manager);
                    _current = null;
                }
                return result;
            }
        }
    }

    private sealed record SandboxManagerGeneration(
        SandboxWorkspaceIdentity Workspace,
        SandboxSessionManager Manager);

    private sealed class SandboxWorkspaceIdentity(string cwd, IReadOnlyList<string> roots)
    {
        public string Cwd { get; } = cwd;
        public IReadOnlyList<string> Roots { get; } = roots;

        public static SandboxWorkspaceIdentity Create(string cwd, IReadOnlyList<string> roots) =>
            new(
                Path.GetFullPath(cwd),
                roots.Select(Path.GetFullPath).ToArray());

        public bool Matches(SandboxWorkspaceIdentity other)
        {
            if (!string.Equals(Cwd, other.Cwd, PathComparison)
                || Roots.Count != other.Roots.Count)
            {
                return false;
            }

            for (var index = 0; index < Roots.Count; index++)
            {
                if (!string.Equals(Roots[index], other.Roots[index], PathComparison))
                    return false;
            }

            return true;
        }

        private static StringComparison PathComparison =>
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }
}
