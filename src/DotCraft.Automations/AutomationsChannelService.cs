using DotCraft.Abstractions;
using DotCraft.Automations.Abstractions;
using DotCraft.Automations.Orchestrator;
using DotCraft.Automations.Protocol;
using DotCraft.Cron;
using DotCraft.Heartbeat;
using DotCraft.Hosting;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Security;
using Microsoft.Extensions.Logging;

namespace DotCraft.Automations;

/// <summary>
/// Channel service that runs the Automations orchestrator as a long-lived service.
/// </summary>
public sealed class AutomationsChannelService(
    AutomationOrchestrator orchestrator,
    DotCraftPaths paths,
    ILogger<AutomationsChannelService> logger)
    : ISessionServiceConsumer, IAutomationsChannelService
{
    private ISessionService? _sessionService;

    public string Name => "automations";

    public HeartbeatService? HeartbeatService { get; set; }

    public CronService? CronService { get; set; }

    public IApprovalService? ApprovalService => null;

    /// <inheritdoc />
    public void SetSessionService(ISessionService service) => _sessionService = service;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_sessionService == null)
            throw new InvalidOperationException(
                "Session service was not set. Automations requires GatewayHost to supply the shared ISessionService.");

        logger.LogInformation(
            "Automations channel service starting (workspace: {WorkspacePath}, craft: {CraftPath})",
            paths.WorkspacePath,
            paths.CraftPath);

        var client = new AutomationSessionClient(_sessionService, paths);
        orchestrator.SetSessionClient(client);
        await orchestrator.StartAsync(cancellationToken);

        var tcs = new TaskCompletionSource();
        await using var reg = cancellationToken.Register(() => tcs.TrySetResult());
        await tcs.Task;

        await StopAsync();
    }

    public Task StopAsync() => orchestrator.StopAsync();

    public Task<ExtChannelSendResult> DeliverAsync(
        string target,
        ChannelOutboundMessage message,
        object? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var content = message.Text ?? message.Caption ?? message.FileName ?? message.Kind;
        var preview = string.IsNullOrEmpty(content) ? "" : content[..Math.Min(content.Length, 200)];
        logger.LogInformation("[Automations -> {Target}] {Content}", target, preview);
        return Task.FromResult(new ExtChannelSendResult { Delivered = true });
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
