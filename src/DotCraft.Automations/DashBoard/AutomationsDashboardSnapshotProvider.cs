using DotCraft.Automations.Orchestrator;
using DotCraft.Automations.Protocol;
using DotCraft.DashBoard;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.Logging;

namespace DotCraft.Automations.DashBoard;

/// <summary>
/// Exposes <see cref="AutomationOrchestrator"/> state to the web Dashboard at
/// <c>/dashboard/api/orchestrators/automations/state</c>.
/// </summary>
public sealed class AutomationsDashboardSnapshotProvider : IOrchestratorSnapshotProvider
{
    private readonly AutomationOrchestrator _orchestrator;
    private readonly ILogger<AutomationsDashboardSnapshotProvider> _logger;

    public AutomationsDashboardSnapshotProvider(
        AutomationOrchestrator orchestrator,
        ILogger<AutomationsDashboardSnapshotProvider> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "automations";

    /// <inheritdoc />
    public object GetSnapshot()
    {
        try
        {
            var tasks = _orchestrator.GetAllTasksAsync(CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
            var ordered = tasks
                .OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt ?? DateTimeOffset.MinValue)
                .ToList();

            var wires = ordered.Select(AutomationsRequestHandler.ToNotificationWire).ToList();

            var countsByStatus = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in wires)
            {
                var key = string.IsNullOrEmpty(w.Status) ? "unknown" : w.Status;
                countsByStatus[key] = countsByStatus.GetValueOrDefault(key) + 1;
            }

            return new AutomationsDashboardSnapshot
            {
                Tasks = wires,
                CountsByStatus = countsByStatus,
                GeneratedAt = DateTimeOffset.UtcNow.ToString("o")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automations dashboard snapshot failed");
            throw;
        }
    }

    /// <inheritdoc />
    public void TriggerRefresh()
    {
        _ = _orchestrator.TriggerImmediatePollAsync(CancellationToken.None).ContinueWith(
            t =>
            {
                if (t.IsFaulted && t.Exception != null)
                    _logger.LogWarning(t.Exception.GetBaseException(), "Automations TriggerImmediatePoll failed");
            },
            TaskScheduler.Default);
    }
}

/// <summary>JSON shape for <c>GET .../orchestrators/automations/state</c>.</summary>
public sealed class AutomationsDashboardSnapshot
{
    public List<AutomationTaskWire> Tasks { get; set; } = [];

    public Dictionary<string, int> CountsByStatus { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string GeneratedAt { get; set; } = string.Empty;
}
