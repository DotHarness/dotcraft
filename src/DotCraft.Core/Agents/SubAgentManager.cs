using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Tracing;
using DotCraft.Diagnostics;
using DotCraft.GeneratedTools.Core;
using DotCraft.Security;
using DotCraft.Tools;
using DotCraft.Tools.Sandbox;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// Manages subagent execution using the AIFunction pattern.
/// Subagents are lightweight agent instances that handle specific tasks and return results directly to the main agent.
/// </summary>
/// <remarks>
/// Implementation uses AIAgent.AsAIFunction() for native framework support.
/// Subagents have restricted tool access for security.
/// A <see cref="SemaphoreSlim"/> throttles concurrent subagent executions to avoid exceeding API rate limits.
/// </remarks>
public sealed class SubAgentManager
{
    private const int SubAgentFileMaxSize = 100000;

    private readonly IChatClient _chatClient;

    private readonly string _workspaceRoot;

    private readonly IReadOnlyList<string> _workspaceRoots;

    private readonly SemaphoreSlim _concurrencyGate;

    private readonly SandboxShellTools? _sandboxShellTools;

    private readonly SandboxFileTools? _sandboxFileTools;

    private readonly WebTools _webTools;

    private readonly bool _useSandbox;

    private readonly AppConfig.ReasoningConfig _reasoningConfig;

    private readonly AppConfig.PromptCachingConfig _promptCachingConfig;

    private readonly AppConfig _config;

    private readonly string _model;

    private readonly string _providerProtocol;

    private readonly string? _endpoint;

    private readonly int? _maxOutputTokens;

    private readonly TraceCollector? _traceCollector;

    private readonly IApprovalService? _approvalService;

    private readonly PathBlacklist? _blacklist;

    private readonly int _shellTimeout;

    private readonly bool _requireApprovalOutsideWorkspace;

    private readonly string? _ripgrepPath;

    private readonly TimeSpan _fileSearchTimeout;

    public SubAgentManager(
        IChatClient chatClient,
        string workspaceRoot,
        int maxConcurrency = 3,
        int shellTimeout = 60,
        bool requireApprovalOutsideWorkspace = true,
        AppConfig.ReasoningConfig? reasoningConfig = null,
        AppConfig.PromptCachingConfig? promptCachingConfig = null,
        string? model = null,
        string? providerProtocol = null,
        PathBlacklist? blacklist = null,
        SandboxSessionManager? sandboxManager = null,
        IApprovalService? approvalService = null,
        TraceCollector? traceCollector = null,
        string? ripgrepPath = null,
        string? endpoint = null,
        int? maxOutputTokens = null,
        AppConfig? config = null,
        IReadOnlyList<string>? workspaceRoots = null)
    {
        _chatClient = chatClient;
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _workspaceRoots = workspaceRoots ?? [_workspaceRoot];
        _concurrencyGate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _useSandbox = sandboxManager != null;
        _reasoningConfig = reasoningConfig ?? config?.Reasoning ?? new AppConfig.ReasoningConfig();
        _promptCachingConfig = promptCachingConfig ?? config?.PromptCaching ?? new AppConfig.PromptCachingConfig();
        _config = config ?? new AppConfig { Reasoning = _reasoningConfig, PromptCaching = _promptCachingConfig };
        _model = string.IsNullOrWhiteSpace(model) ? string.Empty : model.Trim();
        _providerProtocol = string.IsNullOrWhiteSpace(providerProtocol) ? ModelProviderProtocols.OpenAI : providerProtocol.Trim();
        _endpoint = string.IsNullOrWhiteSpace(endpoint) ? null : endpoint.Trim();
        _maxOutputTokens = maxOutputTokens;
        _traceCollector = traceCollector;
        _approvalService = approvalService;
        _blacklist = blacklist;
        _shellTimeout = shellTimeout;
        _requireApprovalOutsideWorkspace = requireApprovalOutsideWorkspace;
        _ripgrepPath = ripgrepPath;
        _fileSearchTimeout = TimeSpan.FromSeconds(Math.Max(1, _config.Tools.File.SearchTimeoutSeconds));

        if (sandboxManager != null)
        {
            // Sandbox mode: subagents execute inside containers
            _sandboxShellTools = new SandboxShellTools(sandboxManager, shellTimeout);
            _sandboxFileTools = new SandboxFileTools(sandboxManager);
        }

        _webTools = new WebTools(
            maxChars: 50000,  // Limit web content size for subagents
            timeoutSeconds: 30
        );
    }

    /// <summary>
    /// Creates an AIFunction that wraps a subagent for the given task.
    /// This allows the main agent to invoke the subagent as a tool and receive results directly.
    /// </summary>
    public AIFunction CreateSubAgentFunction(string taskDescription)
    {
        // Create the subagent with restricted tools
        var subagent = CreateSubAgent(taskDescription);

        // Wrap as AIFunction - the framework handles execution and result passing
        return subagent.AsAIFunction(
            options: new AIFunctionFactoryOptions
            {
                Name = "execute_subagent_task",
                Description = $"Execute a subagent to handle the following task: {taskDescription}"
            }
        );
    }

    /// <summary>
    /// Derives the display label for a SubAgent using the same truncation logic as
    /// <c>CoreToolDisplays.SpawnAgent</c> so that the progress bridge key
    /// matches the Live Table entry label exactly.
    /// </summary>
    internal static string NormalizeLabel(string? label, string task)
        => ToolDisplayHelpers.Truncate(label ?? task, 60);

    /// <summary>
    /// Spawn a subagent to execute a task and return its result text.
    /// Automatically registers in <see cref="SubAgentProgressBridge"/> for Live Table display
    /// and sets up a child tracing session when <see cref="TraceCollector"/> is available.
    /// </summary>
    public async Task<string> SpawnAsync(
        string task,
        string? label = null,
        IApprovalService? approvalService = null,
        ApprovalContext? approvalContext = null,
        CancellationToken cancellationToken = default)
    {
        var taskId = Guid.NewGuid().ToString("N")[..8];
        var bridgeKey = NormalizeLabel(label, task);
        var progressEntry = SubAgentProgressBridge.GetOrCreate(bridgeKey);
        var effectiveApprovalService = BuildSubAgentApprovalService(
            bridgeKey,
            approvalService ?? _approvalService);

        // Resolve parent session and create child session key for tracing
        var parentSessionKey = TracingChatClient.GetActiveSessionKey();
        string? childSessionKey = null;
        if (_traceCollector != null && !string.IsNullOrEmpty(parentSessionKey))
        {
            childSessionKey = $"{parentSessionKey}:sub:{taskId}";
            var rootThreadId = _traceCollector.ResolveRootThreadId(parentSessionKey);
            if (!string.IsNullOrWhiteSpace(rootThreadId))
                _traceCollector.BindChildSession(childSessionKey, rootThreadId, parentSessionKey);
        }

        try
        {
            await _concurrencyGate.WaitAsync(cancellationToken);
            try
            {
                if (childSessionKey != null)
                    TracingChatClient.CurrentSessionKey = childSessionKey;

                using var approvalContextScope = approvalContext != null
                    ? ApprovalContextScope.Set(approvalContext)
                    : null;
                var subagent = CreateSubAgent(task, progressEntry, effectiveApprovalService);
                var result = await subagent.RunAsync(task, session: null, options: null, cancellationToken);
                return result.Text;
            }
            finally
            {
                _concurrencyGate.Release();

                if (childSessionKey != null)
                {
                    TracingChatClient.ResetCallState(childSessionKey);
                    // Restore parent session key on this async context
                    TracingChatClient.CurrentSessionKey = parentSessionKey;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
        finally
        {
            progressEntry.IsCompleted = true;
            TokenTracker.Current?.AddSubAgentTokens(
                progressEntry.InputTokens,
                    progressEntry.OutputTokens,
                    progressEntry.CachedInputTokens,
                    progressEntry.CacheWriteInputTokens,
                    progressEntry.ReasoningOutputTokens,
                    progressEntry.LlmCallCount);
        }
    }

    /// <summary>
    /// Create a subagent with restricted tools for a specific task.
    /// </summary>
    private ChatClientAgent CreateSubAgent(
        string task,
        SubAgentProgressBridge.ProgressEntry? progressEntry = null,
        IApprovalService? approvalService = null)
    {
        var systemPrompt = BuildSubAgentPrompt(task);

        var tools = new List<AITool>();

        if (_useSandbox && _sandboxFileTools != null && _sandboxShellTools != null)
        {
            tools.Add(GeneratedToolFunctions.SandboxFileTools_ReadFile(_sandboxFileTools));
            tools.Add(GeneratedToolFunctions.SandboxFileTools_WriteFile(_sandboxFileTools));
            tools.Add(GeneratedToolFunctions.SandboxFileTools_GrepFiles(_sandboxFileTools));
            tools.Add(GeneratedToolFunctions.SandboxFileTools_FindFiles(_sandboxFileTools));
            tools.Add(GeneratedToolFunctions.SandboxShellTools_Exec(_sandboxShellTools));
        }
        else
        {
            var fileTools = new FileTools(
                workspaceRoot: _workspaceRoot,
                requireApprovalOutsideWorkspace: _requireApprovalOutsideWorkspace,
                maxFileSize: SubAgentFileMaxSize,
                approvalService: approvalService,
                blacklist: _blacklist,
                ripgrepPath: _ripgrepPath,
                searchTimeout: _fileSearchTimeout,
                workspaceRoots: _workspaceRoots
            );

            var shellTools = new ShellTools(
                workingDirectory: _workspaceRoot,
                timeoutSeconds: _shellTimeout,
                requireApprovalOutsideWorkspace: _requireApprovalOutsideWorkspace,
                maxOutputLength: 10000,
                approvalService: approvalService,
                blacklist: _blacklist,
                workspaceRoots: _workspaceRoots
            );

            tools.Add(GeneratedToolFunctions.FileTools_ReadFile(fileTools));
            tools.Add(GeneratedToolFunctions.FileTools_WriteFile(fileTools));
            tools.Add(GeneratedToolFunctions.FileTools_GrepFiles(fileTools));
            tools.Add(GeneratedToolFunctions.FileTools_FindFiles(fileTools));
            tools.Add(GeneratedToolFunctions.ShellTools_Exec(shellTools));
        }

        tools.Add(GeneratedToolFunctions.WebTools_WebSearch(_webTools));
        tools.Add(GeneratedToolFunctions.WebTools_WebFetch(_webTools));

        // ChatClientBuilder applies middleware in reverse registration order:
        // first Use(...) is outermost. Register function invocation first so its internal
        // LLM rounds pass through progress/tracing clients.
        // Effective pipeline: TracingChatClient -> SubAgentProgressChatClient
        // -> StreamingFunctionInvokingChatClient -> PromptCachingChatClient -> base LLM client.
        var chatClientBuilder = new ChatClientBuilder(_chatClient);
        chatClientBuilder.Use(inner =>
        {
            var fic = new StreamingFunctionInvokingChatClient(inner)
            {
                AllowConcurrentInvocation = true
            };
            if (progressEntry != null)
            {
                fic.FunctionInvoker = async (context, ct) =>
                {
                    var toolName = context.Function.Name;
                    progressEntry.CurrentTool = toolName;
                    progressEntry.LastTool = toolName;

                    // Generate human-readable display text via ToolRegistry
                    IDictionary<string, object?> args = context.Arguments;
                    var display = ToolRegistry.FormatToolCall(toolName, args);
                    progressEntry.CurrentToolDisplay = display;
                    progressEntry.LastToolDisplay = display;

                    try
                    {
                        return await context.Function.InvokeAsync(context.Arguments, ct);
                    }
                    finally
                    {
                        progressEntry.CurrentTool = null;
                        progressEntry.CurrentToolDisplay = null;
                    }
                };
            }
            return fic;
        });
        if (progressEntry != null)
            chatClientBuilder.Use(inner => new SubAgentProgressChatClient(inner, progressEntry));
        if (_traceCollector != null)
        {
            var tc = _traceCollector;
            chatClientBuilder.Use(inner => new TracingChatClient(inner, tc));
        }
        ProviderChatClientAdapters.UseProviderAdapters(
            chatClientBuilder,
            _config,
            _providerProtocol,
            _model,
            _endpoint,
            _maxOutputTokens,
            _reasoningConfig,
            _config.Speed,
            _promptCachingConfig,
            _traceCollector);
        var configuredChatClient = chatClientBuilder.Build();

        var options = new ChatClientAgentOptions
        {
            Name = "SubAgent",
            UseProvidedChatClientAsIs = true,  // Use our custom-configured chat client as-is
            ChatOptions = new ChatOptions
            {
                Instructions = systemPrompt,
                Tools = tools,
                Reasoning = _reasoningConfig.ToOptions()
            }
        };

        return configuredChatClient.AsAIAgent(options);
    }

    private static IApprovalService? BuildSubAgentApprovalService(string label, IApprovalService? approvalService)
    {
        if (approvalService == null)
            return null;

        return new PrefixedApprovalService(approvalService, $"[subagent:{label}] ");
    }

    private string BuildSubAgentPrompt(string task)
    {
        var workspaceRoots = _workspaceRoots.Count == 0
            ? "(none)"
            : string.Join(Environment.NewLine, _workspaceRoots.Select(root => $"- {root}"));
        return
$"""
# Subagent

You are a subagent spawned by the main agent to complete a specific task.

## Your Task
{task}

## Rules
1. Stay focused - complete only the assigned task, nothing else
2. Your final response will be reported back to the main agent
3. Do not initiate conversations or take on side tasks
4. Be concise but informative in your findings

## What You Can Do
- Read files and list directory contents in the workspace
- Write files in the workspace
- Search file contents with regex (GrepFiles)
- Find files by name pattern (FindFiles)
- Execute shell commands
- Access files or run commands outside workspace when channel policy allows it through approval
- Search the web
- Fetch web content
- Use these tools to complete your task thoroughly

## What You Cannot Do
- Delete files or directories (security restriction)

## Workspace
Your workspace is at: {_workspaceRoot}
Runtime workspace roots:
{workspaceRoots}

When you have completed the task, provide a clear summary of your findings or actions.
""";
    }
}
