using System.Collections.Concurrent;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Cron;
using DotCraft.Tracing;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Dreams;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Lsp;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Abstractions;

/// <summary>
/// Provides context information for tool provider to create tools.
/// </summary>
public sealed class ToolProviderContext
{
    /// <summary>
    /// The application configuration.
    /// </summary>
    public required AppConfig Config { get; init; }

    /// <summary>
    /// The chat client for AI interactions.
    /// Required for subagent spawning and other AI-powered tools.
    /// </summary>
    public required IChatClient ChatClient { get; init; }

    /// <summary>
    /// Central provider-neutral registry used to resolve model-specific chat clients.
    /// </summary>
    public ChatClientRegistry ChatClientRegistry
    {
        get => field ??= new ChatClientRegistry();
        init;
    }

    /// <summary>
    /// Effective MainAgent model represented by <see cref="ChatClient"/>.
    /// </summary>
    public string EffectiveMainModel
    {
        get => string.IsNullOrWhiteSpace(field) ? Config.Model : field;
        init;
    }

    /// <summary>
    /// Effective provider id represented by <see cref="ChatClient"/>.
    /// </summary>
    public string EffectiveProviderId
    {
        get => string.IsNullOrWhiteSpace(field) ? ChatClientRegistry.ResolveMainProviderId(Config) : field;
        init;
    }

    /// <summary>
    /// Effective provider protocol represented by <see cref="ChatClient"/>.
    /// </summary>
    public string EffectiveProviderProtocol
    {
        get => string.IsNullOrWhiteSpace(field)
            ? ChatClientRegistry.ResolveMainRuntime(Config, EffectiveProviderId, EffectiveMainModel).Protocol
            : field;
        init;
    }

    /// <summary>
    /// Effective reasoning configuration represented by this tool context.
    /// Thread-scoped contexts may override <see cref="AppConfig.Reasoning"/>.
    /// </summary>
    public AppConfig.ReasoningConfig EffectiveReasoning
    {
        get => field ?? Config.Reasoning;
        init;
    }

    /// <summary>
    /// True when this context uses an OpenAI-compatible provider protocol.
    /// </summary>
    public bool IsOpenAICompatible =>
        ModelProviderProtocols.IsOpenAIProtocol(EffectiveProviderProtocol);

    /// <summary>
    /// The workspace path.
    /// </summary>
    public required string WorkspacePath { get; init; }

    /// <summary>
    /// When set (e.g. local automation), absolute path to the task directory containing <c>task.md</c>.
    /// </summary>
    public string? AutomationTaskDirectory { get; init; }

    /// <summary>
    /// When set, overrides <see cref="Configuration.AppConfig.Tools.File.RequireApprovalOutsideWorkspace"/> for file/shell tools.
    /// </summary>
    public bool? RequireApprovalOutsideWorkspace { get; init; }

    /// <summary>
    /// The bot path for configuration and memory storage.
    /// </summary>
    public required string BotPath { get; init; }

    /// <summary>
    /// The memory store for context persistence.
    /// </summary>
    public required MemoryStore MemoryStore { get; init; }

    /// <summary>
    /// Optional Dreams store for passive workspace memory context.
    /// </summary>
    public DreamStore? DreamStore { get; init; }

    /// <summary>
    /// The skills loader for skill-based tools.
    /// </summary>
    public required SkillsLoader SkillsLoader { get; init; }

    /// <summary>
    /// Optional per-process manager for prompt-cache-stable context pages.
    /// </summary>
    public IContextPageManager? ContextPageManager { get; init; }

    /// <summary>
    /// Optional thread-scoped providers that append audited, cache-stable system prompt sections.
    /// </summary>
    public IReadOnlyList<IThreadSystemPromptContextProvider> ThreadSystemPromptContextProviders { get; init; } = [];

    /// <summary>
    /// Applies workspace skill mutations for optional self-learning tools.
    /// Defaults to the direct workspace-file implementation.
    /// </summary>
    public ISkillMutationApplier SkillMutationApplier
    {
        get => field ??= new WorkspaceFileSkillMutationApplier(SkillsLoader);
        init;
    }

    /// <summary>
    /// The approval service for sensitive operations.
    /// </summary>
    public required IApprovalService ApprovalService { get; init; }

    /// <summary>
    /// Optional path blacklist for security restrictions.
    /// </summary>
    public PathBlacklist? PathBlacklist { get; init; }

    /// <summary>
    /// Optional process manager for host shell commands that may continue running
    /// after a tool call returns.
    /// </summary>
    public IBackgroundTerminalService? BackgroundTerminalService { get; init; }

    /// <summary>
    /// Optional cron tools for scheduled tasks.
    /// </summary>
    public CronTools? CronTools { get; init; }

    /// <summary>
    /// Optional MCP client manager for external tool integration.
    /// </summary>
    public McpClientManager? McpClientManager { get; init; }

    /// <summary>
    /// Optional LSP server manager for language-intelligence tools.
    /// </summary>
    public LspServerManager? LspServerManager { get; init; }

    /// <summary>
    /// Registry for deferred MCP tools. Populated by <see cref="DeferredToolProvider"/>
    /// when deferred loading is active. Read by <see cref="DotCraft.Agents.AgentFactory"/>
    /// to wire <c>FunctionInvokingChatClient.AdditionalTools</c> and insert
    /// <c>DynamicToolInjectionChatClient</c> into the pipeline.
    /// </summary>
    public DeferredToolRegistry? DeferredToolRegistry { get; set; }

    /// <summary>
    /// Optional trace collector for debugging and monitoring.
    /// </summary>
    public TraceCollector? TraceCollector { get; init; }

    /// <summary>
    /// Optional thread-scoped store for external CLI session ids used by resumable subagents.
    /// </summary>
    public IExternalCliSessionStore? ExternalCliSessionStore { get; init; }

    /// <summary>
    /// Current session-backed thread id, when tools are being created for a specific thread.
    /// </summary>
    public string? CurrentThreadId { get; init; }

    /// <summary>
    /// Current session-backed thread source, when tools are being created for a specific thread.
    /// </summary>
    public ThreadSource? CurrentThreadSource { get; init; }

    /// <summary>
    /// Controls which DotCraft agent-control tools may be exposed in this tool context.
    /// </summary>
    public AgentControlToolAccess AgentControlToolAccess { get; init; } = AgentControlToolAccess.Full;

    /// <summary>
    /// Optional allow-list of DotCraft agent-control tool names used when
    /// <see cref="AgentControlToolAccess"/> is <see cref="AgentControlToolAccess.AllowList"/>.
    /// </summary>
    public IReadOnlySet<string>? AllowedAgentControlTools { get; init; }

    /// <summary>
    /// Optional exact tool allow-list resolved for the current thread.
    /// </summary>
    public IReadOnlySet<string>? ToolAllowList { get; init; }

    /// <summary>
    /// Optional exact tool deny-list resolved for the current thread.
    /// </summary>
    public IReadOnlySet<string>? ToolDenyList { get; init; }

    /// <summary>
    /// Optional prompt profile for the current thread.
    /// </summary>
    public string? PromptProfile { get; init; }

    /// <summary>
    /// Optional role-specific instructions appended to the current thread prompt.
    /// </summary>
    public string? RoleInstructions { get; init; }

    /// <summary>
    /// Origin channel of the current session-backed thread.
    /// </summary>
    public string? CurrentOriginChannel { get; init; }

    /// <summary>
    /// Channel context of the current session-backed thread.
    /// </summary>
    public string? CurrentChannelContext { get; init; }

    /// <summary>
    /// Optional ACP extension proxy for extension method calls.
    /// Available when running in ACP mode (connected to an editor or IDE client).
    /// </summary>
    public IAcpExtensionProxy? AcpExtensionProxy { get; init; }

    /// <summary>
    /// Optional Node REPL proxy for Desktop-hosted browser automation.
    /// Available only when the current AppServer thread is bound to a client that declared nodeRepl and browserUse support.
    /// </summary>
    public INodeReplProxy? NodeReplProxy { get; init; }

    /// <summary>
    /// Collection of disposable resources created by tool providers.
    /// These resources will be disposed when the application shuts down.
    /// </summary>
    public ConcurrentBag<IAsyncDisposable> DisposableResources { get; } = [];

    /// <summary>
    /// File system abstraction for channel tools that need host-local file access.
    /// Defaults to <see cref="HostAgentFileSystem"/>; overridden to sandbox implementation
    /// when sandbox mode is enabled (see <c>SandboxToolProvider</c>).
    /// </summary>
    public IAgentFileSystem AgentFileSystem
    {
        get => field ??= new HostAgentFileSystem(WorkspacePath);
        set;
    }
}
