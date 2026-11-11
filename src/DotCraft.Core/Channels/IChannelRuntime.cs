using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;

namespace DotCraft.Channels;

public interface IChannelRuntime
{
    string Name { get; }

    string DisplayName => Name;

    ChannelDeliveryCapabilitySnapshot? GetDeliveryCapabilities() => null;

    ChannelChatDescriptor? GetThreadChat(string threadId) => null;

    IReadOnlyList<ChannelToolSpec> GetChannelTools() => [];

    /// <summary>False until an external channel finishes its handshake; bound Turns wait for it so they get the channel's tools.</summary>
    bool IsReady => true;

    ChannelToolInvocationContext ResolveExecutionContext(SessionThread thread, SessionTurn turn)
        => new()
        {
            ChannelName = Name,
            ChannelContext = turn.Initiator?.ChannelContext ?? thread.ChannelContext,
            SenderId = turn.Initiator?.UserId,
            GroupId = turn.Initiator?.GroupId
        };

    Task<ChannelDeliveryResult> DeliverAsync(
        string target,
        ChannelDeliveryMessage message,
        object? metadata = null,
        string? deliveryId = null,
        CancellationToken cancellationToken = default);

    Task<ChannelHistoryResult> ReadHistoryAsync(
        ChannelHistoryRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ChannelHistoryResult.Fail(
            "HistoryUnavailable",
            $"Channel '{Name}' cannot read chat history."));

    Task<ChannelToolInvocationResult> ExecuteToolAsync(
        ChannelToolInvocationRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new ChannelToolInvocationResult
        {
            Success = false,
            ErrorCode = "UnsupportedChannelTool",
            ErrorMessage = $"Channel '{Name}' does not expose runtime tools."
        });
}
