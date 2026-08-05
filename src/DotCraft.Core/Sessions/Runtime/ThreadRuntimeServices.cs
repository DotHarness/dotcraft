using DotCraft.Agents;
using DotCraft.Mcp;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using System.Text.Json.Nodes;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;

namespace DotCraft.Sessions;

/// <summary>
/// Optional Session Core extension for hosts that bind thread-scoped client capabilities
/// after the base thread lifecycle call has completed.
/// </summary>
public interface IThreadAgentRefreshService
{
    /// <summary>
    /// Rebuilds the cached agent for a thread so dynamic, thread-bound tools are reflected
    /// in the next turn.
    /// </summary>
    Task RefreshThreadAgentAsync(string threadId, CancellationToken ct = default);

    /// <summary>
    /// Invalidates cached thread agents so they are rebuilt before their next turn.
    /// </summary>
    void InvalidateThreadAgents();
}

/// <summary>
/// Session Core extension for trusted hosts that invoke a thread tool through the same
/// frozen snapshot and dispatcher used by model-generated calls.
/// </summary>
public interface IThreadToolDispatchService
{
    /// <summary>Dispatches one canonical tool call in the authority context of a thread.</summary>
    Task<ToolExecutionResult> DispatchThreadToolAsync(
        string threadId,
        ToolName toolName,
        JsonObject arguments,
        string callId,
        ToolInvocationAudience audience = ToolInvocationAudience.Host,
        CancellationToken cancellationToken = default,
        ToolInvocationOrigin? origin = null);
}

/// <summary>
/// Session Core extension for resolving the frozen effective tool snapshot used by trusted hosts.
/// </summary>
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

/// <summary>
/// Optional Session Core extension that announces publication of a new effective tool snapshot.
/// Consumers use this signal to revoke capabilities derived from an older snapshot without polling.
/// </summary>
public interface IThreadToolSnapshotChangeSource
{
    /// <summary>Raised after a new immutable snapshot becomes effective for a thread.</summary>
    event EventHandler<EffectiveToolSnapshotChangedEventArgs>? EffectiveToolSnapshotChanged;
}

/// <summary>Identifies the thread and revision of a newly published effective tool snapshot.</summary>
public sealed class EffectiveToolSnapshotChangedEventArgs(string threadId, long revision) : EventArgs
{
    public string ThreadId { get; } = threadId;

    public long Revision { get; } = revision;
}

/// <summary>Internal Session Core capability for snapshotting live tool bindings onto a forked thread.</summary>
internal interface IThreadForkToolBindingService
{
    /// <summary>Copies all currently available inheritable bindings and reports whether any were copied.</summary>
    bool TryForkThreadToolBindings(string parentThreadId, string childThreadId);
}

/// <summary>Session Core extension for resolving the effective MCP runtime of a thread.</summary>
public interface IThreadMcpRuntimeService
{
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

/// <summary>
/// Internal Session Core extension used by profile-backed SubAgents whose runtime
/// is not the native agent loop but still needs persisted turn/item history.
/// </summary>
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

/// <summary>
/// Internal Session Core extension used by SubAgent controls that need to apply
/// child-thread lifecycle changes while preserving the public rule that child
/// threads cannot be archived directly by clients.
/// </summary>
public interface ISubAgentThreadLifecycleService
{
    /// <summary>
    /// Archives a SubAgent child thread and its descendants after the incoming
    /// parent/child edge has been closed.
    /// </summary>
    Task ArchiveSubAgentTreeForCloseAsync(string childThreadId, CancellationToken ct = default);
}
