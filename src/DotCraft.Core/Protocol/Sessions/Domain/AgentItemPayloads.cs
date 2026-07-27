using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Plugins;

namespace DotCraft.Protocol;

/// <summary>
/// Payload for AgentMessage items (final, after streaming).
/// </summary>
public sealed record AgentMessagePayload
{
    public string Text { get; init; } = string.Empty;
}
/// <summary>
/// Delta payload emitted during AgentMessage streaming (item/delta events).
/// </summary>
public sealed record AgentMessageDelta
{
    public string DeltaKind { get; init; } = "agentMessage";

    public string TextDelta { get; init; } = string.Empty;
}

/// <summary>
/// Payload for ReasoningContent items.
/// </summary>
public sealed record ReasoningContentPayload
{
    public string Text { get; init; } = string.Empty;
}

/// <summary>
/// Delta payload emitted during ReasoningContent streaming.
/// </summary>
public sealed record ReasoningContentDelta
{
    public string DeltaKind { get; init; } = "reasoningContent";

    public string TextDelta { get; init; } = string.Empty;
}
