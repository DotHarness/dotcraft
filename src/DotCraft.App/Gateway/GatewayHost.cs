using DotCraft.Agents;
using DotCraft.Channels;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Cron;
using DotCraft.ExternalChannel;
using DotCraft.Tracing;
using DotCraft.Heartbeat;
using DotCraft.Hooks;
using DotCraft.Hosting;
using DotCraft.Lsp;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Tools.Sandbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using DotCraft.AppServer;
using DotCraft.Sessions;

namespace DotCraft.Gateway;

/// <summary>
/// Hosts multiple channel services concurrently through a shared channel runner and workspace services.
/// </summary>
public sealed class GatewayHost : IDotCraftHost
{
    private readonly IServiceProvider _sp;
    private readonly AppConfig _config;
    private readonly DotCraftPaths _paths;
    private readonly SkillsLoader _skillsLoader;
    private readonly CronService _cronService;
    private readonly IReadOnlyList<IChannelService> _channels;
    private readonly MessageRouter _router;
    private readonly ModuleRegistry _moduleRegistry;
    private readonly ExternalChannelRegistry _externalChannelRegistry;
    private AgentFactory? _sharedAgentFactory;
    private ISessionService? _sharedSessionService;

    public GatewayHost(
        IServiceProvider sp,
        AppConfig config,
        DotCraftPaths paths,
        SkillsLoader skillsLoader,
        CronService cronService,
        IEnumerable<IChannelService> channels,
        MessageRouter router,
        ModuleRegistry moduleRegistry,
        ExternalChannelRegistry externalChannelRegistry)
    {
        _sp = sp;
        _config = config;
        _paths = paths;
        _skillsLoader = skillsLoader;
        _cronService = cronService;
        _channels = channels.ToList();
        _router = router;
        _moduleRegistry = moduleRegistry;
        _externalChannelRegistry = externalChannelRegistry;

        foreach (var ch in _channels)
        {
            _router.RegisterChannel(ch);
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // Scan for tool icons at startup
        ToolSourceCollector.ScanToolIcons(_moduleRegistry, _config);

        await using var channelRunner = ChannelRunner.CreateForGateway(
            _sp, _config, _paths, _moduleRegistry, _externalChannelRegistry, _router, _channels);

        channelRunner.BuildPoolThroughBuildAll();

        var sharedAgentRunner = BuildSharedAgentRunner();

        using var heartbeatService = new HeartbeatService(
            _paths.CraftPath,
            onHeartbeat: sharedAgentRunner,
            intervalSeconds: _config.Heartbeat.IntervalSeconds,
            enabled: _config.Heartbeat.Enabled,
            logger: _sp.GetService<ILoggerFactory>()?.CreateLogger<HeartbeatService>());

        if (_config.Heartbeat.NotifyAdmin)
        {
            heartbeatService.OnResult = async result =>
                await _router.BroadcastToAdminsAsync($"[Heartbeat] {result}");
        }

        _cronService.OnJob = async job =>
        {
            var sessionKey = $"cron:{job.Id}";
            var approvalContext = BuildApprovalContext(job.Payload);

            AgentRunResult? run;
            if (approvalContext != null)
            {
                using var _ = ApprovalContextScope.Set(approvalContext);
                run = await sharedAgentRunner(job.Payload.Message, sessionKey, job.Name, cancellationToken);
            }
            else
            {
                run = await sharedAgentRunner(job.Payload.Message, sessionKey, job.Name, cancellationToken);
            }

            var deliverText = run?.Result;
            if (job.Payload.Deliver && deliverText != null)
            {
                var channel = job.Payload.Channel ?? "unknown";
                var target = job.Payload.To ?? job.Payload.CreatorId ?? "";
                try
                {
                    await _router.DeliverAsync(
                        channel,
                        target,
                        new ChannelDeliveryMessage
                        {
                            Kind = "text",
                            Text = deliverText
                        });
                }
                catch (Exception ex)
                {
                    await Console.Error.WriteLineAsync($"[Cron] Delivery failed: {ex.Message}");
                }
            }

            var ok = run != null && run.Error == null;
            return new CronOnJobResult(
                run?.ThreadId,
                run?.Result,
                run?.Error,
                ok,
                run?.InputTokens,
                run?.OutputTokens);
        };

        channelRunner.CompleteAfterSession(_sharedSessionService!, heartbeatService, _cronService);

        await channelRunner.StartWebPoolAsync();

        if (_config.Heartbeat.Enabled)
        {
            heartbeatService.Start();
            AnsiConsole.MarkupLine($"[green][[Gateway]][/] Heartbeat started (interval: {_config.Heartbeat.IntervalSeconds}s)");
        }

        if (_config.Cron.Enabled)
        {
            _cronService.Start();
            AnsiConsole.MarkupLine($"[green][[Gateway]][/] Cron service started ({_cronService.ListJobs().Count} jobs)");
        }

        PrintStartupSummary();

        channelRunner.BeginChannelLoops(cancellationToken);

        await WaitForShutdownSignalAsync(cancellationToken);

        AnsiConsole.MarkupLine("[yellow][[Gateway]] Shutting down...[/]");

        await channelRunner.StopAsync();

        heartbeatService.Stop();
        _cronService.Stop();

        AnsiConsole.MarkupLine("[grey][[Gateway]] All channels stopped.[/]");
    }

    private AgentRunSessionDelegate BuildSharedAgentRunner()
    {
        var memoryStore = _sp.GetRequiredService<MemoryStore>();
        var pathBlacklist = _sp.GetRequiredService<PathBlacklist>();
        var mcpClientManager = _sp.GetRequiredService<Mcp.McpClientManager>();
        var lspServerManager = _sp.GetRequiredService<LspServerManager>();
        var cronTools = _sp.GetService<CronTools>();
        var traceCollector = _sp.GetService<TraceCollector>();
        var hookRunner = _sp.GetService<HookRunner>();

        // Build a routing approval service that delegates to the originating channel's
        // approval service based on ApprovalContext.Source (channel name), with Console as fallback.
        var channelServiceMap = _channels
            .Where(ch => ch.ApprovalService != null)
            .ToDictionary(ch => ch.Name, ch => ch.ApprovalService!);
        var channelRouting = new ChannelRoutingApprovalService(
            channelServiceMap,
            fallback: new ConsoleApprovalService());
        // Wrap with SessionScopedApprovalService so that per-turn wire protocol approvals
        // (from ExternalChannelHost / AppServerEventDispatcher) can install a SessionApprovalService
        // override via SessionService. Without this wrapper the override has no effect and all
        // approvals fall through to ConsoleApprovalService.
        var approvalService = new SessionScopedApprovalService(channelRouting);
        _sp.GetRequiredService<CommonToolApprovalEvaluator>().Bind(approvalService);

        // Collect tool providers from modules

        var planStore = new PlanStore(_paths.CraftPath);
        var chatClientRegistry = _sp.GetRequiredService<ChatClientRegistry>();
        var mainRuntime = chatClientRegistry.ResolveMainRuntime(_config);
        var mainModel = mainRuntime.Model;
        var contextPageManager = new ContextPageManager();
        var threadSystemPromptContextProviders = _sp.GetServices<IThreadSystemPromptContextProvider>().ToArray();
        var toolSources = new ToolSourceCollector(_moduleRegistry, _sp, _config).Collect().ToList();
        toolSources.Add(new CoreToolSource(
            _config,
            chatClientRegistry,
            _skillsLoader,
            approvalService,
            pathBlacklist,
            lspServerManager,
            _sp.GetService<IBackgroundTerminalService>(),
            traceCollector,
            _sp.GetService<ISkillMutationApplier>(),
            contextPageManager));
        if (_config.Tools.Sandbox.Enabled)
        {
            toolSources.Add(new SandboxToolSource(
                _config,
                chatClientRegistry,
                _skillsLoader,
                approvalService,
                pathBlacklist,
                traceCollector,
                _sp.GetService<ISkillMutationApplier>(),
                contextPageManager));
        }

        _sharedAgentFactory = new AgentFactory(
            _paths.CraftPath, _paths.WorkspacePath, _config,
            memoryStore, _skillsLoader, approvalService, pathBlacklist,
            runtimeContext: new AgentRuntimeContext
            {
                Config = _config,
                ChatClient = chatClientRegistry.GetChatClient(mainRuntime),
                ChatClientRegistry = chatClientRegistry,
                EffectiveProviderId = mainRuntime.ProviderId,
                EffectiveProviderProtocol = mainRuntime.Protocol,
                EffectiveMainModel = mainModel,
                WorkspacePath = _paths.WorkspacePath,
                BotPath = _paths.CraftPath,
                MemoryStore = memoryStore,
                SkillsLoader = _skillsLoader,
                ContextPageManager = contextPageManager,
                ThreadSystemPromptContextProviders = threadSystemPromptContextProviders,
                ApprovalService = approvalService,
                PathBlacklist = pathBlacklist,
                CronTools = cronTools,
                McpClientManager = mcpClientManager,
                LspServerManager = lspServerManager,
                TraceCollector = traceCollector
            },
            traceCollector: traceCollector,
            customCommandLoader: _sp.GetService<CustomCommandLoader>(),
            onConsolidatorStatus: AnsiConsole.MarkupLine,
            planStore: planStore,
            hookRunner: hookRunner,
            chatClientRegistry: chatClientRegistry,
            toolDispatcher: _sp.GetRequiredService<IToolDispatcher>(),
            toolSources: toolSources);

        var agent = _sharedAgentFactory.CreateAgentForMode(AgentMode.Agent);
        _sharedSessionService = SessionServiceFactory.Create(_sharedAgentFactory, agent, _sp);
        var runner = new AgentRunner(_paths.WorkspacePath, _sharedSessionService);
        return runner.RunAsync;
    }

    private void PrintStartupSummary()
    {
        AnsiConsole.MarkupLine("[green][[Gateway]][/] Starting with channels:");
        foreach (var ch in _channels)
        {
            AnsiConsole.MarkupLine($"[grey]  - {ch.Name}[/]");
        }
        AnsiConsole.MarkupLine("[grey]Press Ctrl+C to stop...[/]");
    }

    private static async Task WaitForShutdownSignalAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            tcs.TrySetResult();
        };
        await using var reg = cancellationToken.Register(() => tcs.TrySetResult());
        await tcs.Task;
    }

    private static ApprovalContext? BuildApprovalContext(CronPayload payload)
    {
        if (string.IsNullOrEmpty(payload.CreatorSource) || string.IsNullOrEmpty(payload.CreatorId))
            return null;

        var groupId = !string.IsNullOrEmpty(payload.CreatorGroupId) && long.TryParse(payload.CreatorGroupId, out var gid) ? gid : 0L;
        return new ApprovalContext { UserId = payload.CreatorId, Source = payload.CreatorSource, GroupId = groupId };
    }

    public async ValueTask DisposeAsync()
    {
        // Channels are disposed by ChannelRunner at end of RunAsync (await using).
        if (_sharedAgentFactory != null)
            await _sharedAgentFactory.DisposeAsync();
    }
}
