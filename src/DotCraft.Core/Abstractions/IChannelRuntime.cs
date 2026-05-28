using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Abstractions;

/// <summary>
/// Unified runtime contract for built-in and external channels.
/// </summary>
public interface IChannelRuntime
{
    /// <summary>
    /// Gets the unique runtime name of this channel.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Returns the structured delivery capabilities exposed by this channel.
    /// </summary>
    ChannelDeliveryCapabilities? GetDeliveryCapabilities() => null;

    /// <summary>
    /// Returns the platform-native tools exposed by this channel.
    /// </summary>
    IReadOnlyList<ChannelToolDescriptor> GetChannelTools() => [];

    /// <summary>
    /// Whether this runtime has completed the handshake required to expose its tools and
    /// accept turn execution. Native in-proc channels are always ready once registered;
    /// external (adapter-backed) channels return <c>true</c> only after the wire handshake
    /// finished (i.e. <c>AppServerConnection.IsClientReady</c>).
    /// </summary>
    /// <remarks>
    /// Consumers (e.g. the automations orchestrator) use this signal to defer triggering
    /// bound turns on a thread whose channel is not yet ready — otherwise the agent would
    /// build its turn without channel-specific tools.
    /// </remarks>
    bool IsReady => true;

    /// <summary>
    /// Resolves the execution context for a channel tool call against the current thread/turn.
    /// </summary>
    ExtChannelToolCallContext ResolveExecutionContext(SessionThread thread, SessionTurn turn)
        => new()
        {
            ChannelName = Name,
            ChannelContext = turn.Initiator?.ChannelContext ?? thread.ChannelContext,
            SenderId = turn.Initiator?.UserId,
            GroupId = turn.Initiator?.GroupId
        };

    /// <summary>
    /// Delivers a structured outbound message to a specific target.
    /// </summary>
    Task<ExtChannelSendResult> DeliverAsync(
        string target,
        ChannelOutboundMessage message,
        object? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a channel-native tool call.
    /// </summary>
    Task<ExtChannelToolCallResult> ExecuteToolAsync(
        ExtChannelToolCallParams request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new ExtChannelToolCallResult
        {
            Success = false,
            ErrorCode = "UnsupportedChannelTool",
            ErrorMessage = $"Channel '{Name}' does not expose runtime tools."
        });
}
