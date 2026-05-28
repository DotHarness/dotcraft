using DotCraft.Cron;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Automations.Abstractions;

/// <summary>
/// Representation of a local automation task.
/// </summary>
public abstract class AutomationTask : IAutomationTaskEventPayload
{
    private AutomationTaskStatus _status;
    private readonly object _statusLock = new();

    /// <summary>Stable unique identifier.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable title for display in the Desktop Automations view.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>Current lifecycle state of the task. Thread-safe via lock.</summary>
    public AutomationTaskStatus Status
    {
        get
        {
            lock (_statusLock)
            {
                return _status;
            }
        }
        set
        {
            lock (_statusLock)
            {
                _status = value;
            }
        }
    }

    /// <summary>
    /// Thread identifier of the active agent session for this task, if any.
    /// Null when no agent session has been started.
    /// </summary>
    public string? ThreadId { get; set; }

    /// <summary>Markdown description / body of the task.</summary>
    public string? Description { get; set; }

    /// <summary>Summary written by the agent upon completion.</summary>
    public string? AgentSummary { get; set; }

    /// <summary>UTC creation time.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>UTC last update time.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Optional schedule for recurring dispatch. When null, the task runs once on <see cref="AutomationTaskStatus.Pending"/>.
    /// When set, the orchestrator only dispatches the task when the schedule is due, and automatically
    /// re-enters <see cref="AutomationTaskStatus.Pending"/> after completion to await the next tick.
    /// </summary>
    public CronSchedule? Schedule { get; set; }

    /// <summary>
    /// Optional binding to a pre-existing thread. When set, the orchestrator submits workflow turns directly into
    /// <see cref="AutomationThreadBinding.ThreadId"/> rather than creating a synthesized automation thread.
    /// </summary>
    public AutomationThreadBinding? ThreadBinding { get; set; }

    /// <summary>
    /// Next scheduled run time (UTC). Computed at load / after each tick from <see cref="Schedule"/>.
    /// Null means "ready to dispatch now" (one-shot pending tasks) or "no schedule configured".
    /// </summary>
    public DateTimeOffset? NextRunAt { get; set; }
}
