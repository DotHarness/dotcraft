using DotCraft.Agents;
using DotCraft.Channels;
using DotCraft.Tools;

namespace DotCraft.Sessions;

/// <summary>
/// Immutable inputs selected when a Turn is admitted. Later Thread configuration changes
/// update the next Turn and cannot change these choices underneath an active execution.
/// </summary>
internal sealed record TurnExecutionContext(
    long RuntimeGeneration,
    Task<TurnExecutionResources> Resources,
    ThreadConfiguration Configuration,
    ThreadWorkspaceContext Workspace,
    ChannelSessionInfo? Channel,
    TurnTriggerInfo? Trigger,
    bool SupportsCommandExecutionStreaming,
    bool SupportsToolExecutionLifecycle);

internal sealed record TurnExecutionResources(
    ChatClientAgent Agent,
    EffectiveToolSnapshot? ToolSnapshot);
