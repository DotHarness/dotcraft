using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// Custom AIContent injected from streaming raw provider updates to surface
/// partial tool-call argument chunks before FunctionCallContent is assembled.
/// </summary>
public sealed class ToolCallArgumentsDeltaContent : AIContent
{
    /// <summary>
    /// Tool call index in the current provider response.
    /// </summary>
    public int ToolCallIndex { get; init; }

    /// <summary>
    /// Tool name for the call. Usually present on the first chunk.
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// Correlation call id for the tool invocation. Usually present on the first chunk.
    /// </summary>
    public string? CallId { get; init; }

    /// <summary>
    /// Raw json arguments delta chunk from the provider stream.
    /// </summary>
    public string ArgumentsDelta { get; init; } = string.Empty;
}
