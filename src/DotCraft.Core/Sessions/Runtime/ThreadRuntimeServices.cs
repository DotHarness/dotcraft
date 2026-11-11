using DotCraft.Agents;
using DotCraft.Mcp;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using System.Text.Json.Nodes;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;

namespace DotCraft.Sessions;

public interface IThreadAgentRefreshService
{
    Task RefreshThreadAgentAsync(string threadId, CancellationToken ct = default);

    /// <summary>Invalidates cached thread agents so they are rebuilt before their next turn.</summary>
    void InvalidateThreadAgents();
}

/// <summary>Reloads every thread's skills catalog before its next sampling request.</summary>
public interface ISkillReloadService
{
    void ReloadSkills();
}

/// <summary>
/// Session Core extension for trusted hosts that invoke a thread tool through the same
/// frozen snapshot and dispatcher used by model-generated calls.
/// </summary>
public interface IThreadToolDispatchService
{
    Task<ToolExecutionResult> DispatchThreadToolAsync(
        string threadId,
        ToolName toolName,
        JsonObject arguments,
        string callId,
        ToolInvocationAudience audience = ToolInvocationAudience.Host,
        CancellationToken cancellationToken = default,
        ToolInvocationOrigin? origin = null);
}

public interface IThreadToolSnapshotService
{
    /// <summary>
    /// Returns the active Turn snapshot when one exists, otherwise the latest thread snapshot,
    /// building it on demand when required.
    /// </summary>
    Task<EffectiveToolSnapshot> GetEffectiveToolSnapshotAsync(
        string threadId,
        CancellationToken cancellationToken = default);
}

public interface IThreadToolSnapshotChangeSource
{
    event EventHandler<EffectiveToolSnapshotChangedEventArgs>? EffectiveToolSnapshotChanged;
}

public sealed class EffectiveToolSnapshotChangedEventArgs(string threadId, long revision) : EventArgs
{
    public string ThreadId { get; } = threadId;

    public long Revision { get; } = revision;
}

internal interface IThreadForkToolBindingService
{
    /// <summary>Copies all currently available inheritable bindings and reports whether any were copied.</summary>
    bool TryForkThreadToolBindings(string parentThreadId, string childThreadId);
}

internal interface INativeSubAgentForkMaterializationService
{
    Task<bool> MaterializeNativeSubAgentForkAsync(
        SessionThread parentThread,
        SessionThread childThread,
        IReadOnlyList<ChatMessage> parentModelHistory,
        CancellationToken ct);
}

public interface IThreadSystemNoticeService
{
    /// <summary>
    /// Queues a notice for the thread's running Turn, or its latest completed Turn when none is running.
    /// Returns without waiting for a running Turn to release the thread.
    /// </summary>
    void AppendSystemNotice(string threadId, SystemNoticePayload payload);
}

public interface IThreadMcpRuntimeService
{
    /// <summary>Requests background MCP refresh for loaded active threads.</summary>
    Task RefreshMcpRuntimesAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the runtime composed from thread-selected user configuration and additive binding sessions.</summary>
    Task<McpClientManager?> GetEffectiveMcpRuntimeAsync(
        string threadId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the MCP servers contributed by one live binding. An empty list removes that
    /// binding contribution without changing the thread's inherited/disabled/replacement choice.
    /// </summary>
    Task SetBindingMcpServersAsync(
        string threadId,
        string bindingId,
        IReadOnlyList<McpServerConfig> servers,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Binding MCP sessions are not supported by this session service.");
}

/// <summary>Records persisted Turn history for profile-backed SubAgents outside the native agent loop.</summary>
public interface ISubAgentSyntheticTurnService
{
    Task<SessionTurn> StartSubAgentSyntheticTurnAsync(
        string threadId,
        IList<AIContent> content,
        string runtimeType,
        string? profileName,
        CancellationToken ct = default);

    Task<SessionTurn> CompleteSubAgentSyntheticTurnAsync(
        string threadId,
        string turnId,
        string text,
        bool isError,
        SubAgentTokenUsage? tokensUsed,
        CancellationToken ct = default);

    Task<SessionTurn> CancelSubAgentSyntheticTurnAsync(
        string threadId,
        string turnId,
        string reason,
        CancellationToken ct = default);
}

public interface ISubAgentThreadLifecycleService
{
    /// <summary>
    /// Archives a SubAgent child thread and its descendants after the incoming
    /// parent/child edge has been closed.
    /// </summary>
    Task ArchiveSubAgentTreeForCloseAsync(string childThreadId, CancellationToken ct = default);
}
