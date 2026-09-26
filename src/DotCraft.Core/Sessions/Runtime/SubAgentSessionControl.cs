using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using DotCraft.Configuration;
using DotCraft.Hooks;
using Microsoft.Extensions.AI;
using ModelPreference = DotCraft.Configuration.ModelPreference;

namespace DotCraft.Sessions;

public sealed class SubAgentSessionContext
{
    public required ISessionService SessionService { get; init; }

    public required SessionThread ParentThread { get; init; }

    public required string ParentTurnId { get; init; }

    public required string RootThreadId { get; init; }

    public int Depth { get; init; }

    internal IReadOnlyList<ChatMessage> ParentModelHistory { get; init; } = [];

    public Func<SubAgentLifecycleHookRequest, CancellationToken, Task>? LifecycleHook { get; init; }
}

public sealed class SubAgentLifecycleHookRequest
{
    public required HookEvent Event { get; init; }

    public required SessionThread ChildThread { get; init; }

    public string? Status { get; init; }

    public string? Message { get; init; }
}

public static class SubAgentSessionScope
{
    private static readonly AsyncLocal<SubAgentSessionContext?> CurrentContext = new();

    public static SubAgentSessionContext? Current => CurrentContext.Value;

    public static IDisposable Set(SubAgentSessionContext context)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new Scope(() => CurrentContext.Value = previous);
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

public sealed class SubAgentSpawnOptions
{
    /// <summary>Optional stable purpose used by host integrations to specialize child behavior.</summary>
    public string? Purpose { get; set; }

    /// <summary>Optional callback invoked after child creation and before its first Turn starts.</summary>
    public Func<SessionThread, CancellationToken, Task>? ChildCreated { get; set; }

    /// <summary>Observes durable acceptance of the initial input.</summary>
    public Func<SessionThread, CancellationToken, Task>? ChildStarted { get; set; }

    /// <summary>Releases preparation resources before an unadmitted child is deleted.</summary>
    public Func<SessionThread, CancellationToken, Task>? StartupFailed { get; set; }

    public string AgentPrompt { get; set; } = string.Empty;

    public string TaskName { get; set; } = string.Empty;

    public string? AgentNickname { get; set; }

    public string? AgentRole { get; set; }

    public string? ProfileName { get; set; }

    public string? WorkingDirectory { get; set; }

    public IReadOnlyList<SubAgentRoleConfig>? RoleConfigs { get; set; }

    public ModelPreference? SubAgentPreference { get; set; }

    /// <summary>
    /// Gets or sets the model selection applied to this invocation only.
    /// </summary>
    public SubAgentInvocationModelOverride? InvocationModelOverride { get; set; }

    /// <summary>
    /// Gets or sets the calling thread snapshot that authorized a model-visible invocation override.
    /// Internal callers may omit it when their own policy is authoritative.
    /// </summary>
    public SubAgentModelCatalogSnapshot? InvocationModelCatalogSnapshot { get; set; }

    /// <summary>
    /// Gets or sets the runtime configuration inherited by the child invocation.
    /// </summary>
    public AppConfig? RuntimeConfig { get; set; }

    public int MaxDepth { get; set; } = 1;

    /// <summary>
    /// Maximum number of open (resident) SubAgents allowed within the root thread
    /// subtree. Exceeding it auto-closes the oldest idle SubAgent before spawning.
    /// </summary>
    public int MaxConcurrentSubAgents { get; set; } = 16;

    public string? ForkTurns { get; set; }
}

/// <summary>
/// Selects a model and reasoning effort for one subagent invocation.
/// </summary>
public sealed class SubAgentInvocationModelOverride
{
    /// <summary>Gets the optional model identifier.</summary>
    public string? Model { get; init; }

    /// <summary>Gets the optional reasoning effort.</summary>
    public ModelReasoningEffort? Effort { get; init; }
}

public sealed class SubAgentControlResult
{
    [JsonIgnore]
    public string ChildThreadId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TaskName { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? Message { get; set; }

    public string? AgentNickname { get; set; }

    public string? AgentRole { get; set; }

    public string? ProfileName { get; set; }

    public string? RuntimeType { get; set; }

    [JsonIgnore]
    public bool SupportsSendInput { get; set; }

    [JsonIgnore]
    public bool SupportsResume { get; set; }

    public bool SupportsSendMessage { get; set; }

    public bool SupportsFollowupTask { get; set; }

    public bool SupportsClose { get; set; } = true;
}

public sealed class SubAgentWaitResult
{
    public string Status { get; set; } = string.Empty;

    public bool TimedOut { get; set; }
}

public sealed class SubAgentListResult
{
    public IReadOnlyList<SubAgentListItem> Data { get; set; } = [];
}

public sealed class SubAgentListItem
{
    public string AgentPath { get; set; } = DotCraft.Sessions.AgentPath.Root;

    public string Status { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? LastTaskMessage { get; set; }
}

public sealed class SubAgentRunResult
{
    public string ThreadId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Controls how <see cref="SubAgentSessionControl.FollowupTaskAsync"/> handles a target that already has an active turn.
/// </summary>
public enum SubAgentFollowupDeliveryMode
{
    /// <summary>Append the task to the target thread's FIFO queue.</summary>
    Queue,

    /// <summary>Promote the task into same-turn guidance for a running native SubAgent.</summary>
    Steer
}

public static partial class SubAgentSessionControl
{
    private static readonly TimeSpan CloseAgentCancellationWait = TimeSpan.FromSeconds(5);
    private const string SubAgentFollowupTriggerKind = "subagentFollowupTask";
    private const string SubAgentInputTriggerKind = "subagentInput";

    private sealed class RunningChild(
        string parentThreadId,
        CancellationTokenSource cancellation,
        Task<SubAgentRunResult> completion)
    {
        public string ParentThreadId { get; } = parentThreadId;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task<SubAgentRunResult> Completion { get; } = completion;
    }

    private static readonly ConcurrentDictionary<string, RunningChild> RunningChildren = new(StringComparer.Ordinal);
    private sealed record ResolvedAgentTarget(
        string ThreadId,
        AgentPath Path,
        SessionThread Thread,
        ThreadSpawnEdge? Edge);

    private sealed record QueuedFollowupTaskResult(
        SubAgentControlResult Result,
        QueuedTurnInput QueuedInput);

    private static string NormalizeRequired(string value, string name)
    {
        var normalized = NormalizeOptional(value);
        if (normalized == null)
            throw new ArgumentException($"{name} is required.", name);
        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeNickname(string? nickname, string prompt)
    {
        var normalized = NormalizeOptional(nickname);
        if (normalized != null)
            return normalized.Length <= 48 ? normalized : normalized[..48];

        var firstLine = prompt.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
            ?? "Subagent";
        return firstLine.Length <= 48 ? firstLine : firstLine[..48];
    }

    private sealed record SubAgentCapabilities(
        bool SupportsSendInput,
        bool SupportsResume,
        bool SupportsClose);
}
