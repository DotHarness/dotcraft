using DotCraft.Channels;
using DotCraft.Sessions;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using UserMessagePayload = DotCraft.Sessions.UserMessagePayload;

namespace DotCraft.AppBinding;

/// <summary>
/// Delivers the final assistant reply for turns initiated from social-channel app bindings.
/// </summary>
public sealed class SocialChannelDeliveryCoordinator
{
    private readonly AppBindingService controlPlane;
    private readonly IChannelRuntimeRegistry runtimeRegistry;

    public SocialChannelDeliveryCoordinator(AppBindingService controlPlane, IChannelRuntimeRegistry registry)
    { this.controlPlane = controlPlane; runtimeRegistry = registry; }
    private static readonly TimeSpan DefaultObservationTimeout = TimeSpan.FromHours(12);

    public void StartQueuedTurnDelivery(
        ISessionService sessionService,
        string workspaceCraftPath,
        string threadId,
        string bindingId,
        string queuedInputId,
        long? authorityRevision = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(threadId)
            || string.IsNullOrWhiteSpace(bindingId)
            || string.IsNullOrWhiteSpace(queuedInputId))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ObserveQueuedTurnAsync(
                    sessionService,
                    workspaceCraftPath,
                    threadId,
                    bindingId,
                    queuedInputId,
                    authorityRevision,
                    cancellationToken);
            }
            catch (Exception)
            {
                // Delivery is best-effort and must never fault turn execution.
            }
        }, CancellationToken.None);
    }

    internal async Task ObserveQueuedTurnAsync(
        ISessionService sessionService,
        string workspaceCraftPath,
        string threadId,
        string bindingId,
        string queuedInputId,
        long? authorityRevision = null,
        CancellationToken cancellationToken = default)
    {
        using var timeout = new CancellationTokenSource(DefaultObservationTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        string? matchedTurnId = null;
        await foreach (var evt in sessionService
                           .SubscribeThreadAsync(threadId, replayRecent: true, linked.Token)
                           .WithCancellation(linked.Token))
        {
            if (matchedTurnId == null)
            {
                if (evt.EventType != SessionEventType.TurnStarted || !IsMatchingTurn(evt.TurnPayload, bindingId, queuedInputId))
                    continue;

                matchedTurnId = evt.TurnId;
                continue;
            }

            if (!string.Equals(evt.TurnId, matchedTurnId, StringComparison.Ordinal))
                continue;

            switch (evt.EventType)
            {
                case SessionEventType.ItemCompleted
                    when evt.ItemPayload?.Payload is AgentMessagePayload
                    {
                        DeliveryMode: "async",
                        Text: { } asyncText
                    }:
                    await DeliverTextAsync(
                        workspaceCraftPath,
                        bindingId,
                        authorityRevision,
                        threadId,
                        matchedTurnId,
                        asyncText,
                        "socialBindingAsyncMessage",
                        linked.Token);
                    break;
                case SessionEventType.TurnCompleted:
                    await DeliverCompletedTurnAsync(workspaceCraftPath, bindingId, authorityRevision, evt.TurnPayload, linked.Token);
                    return;
                case SessionEventType.TurnFailed:
                    return;
                case SessionEventType.TurnCancelled:
                    return;
            }
        }
    }

    private async Task DeliverCompletedTurnAsync(
        string workspaceCraftPath,
        string bindingId,
        long? authorityRevision,
        SessionTurn? turn,
        CancellationToken cancellationToken)
    {
        if (turn == null)
            return;

        var replyText = string.Concat(turn.Items
            .Where(item => item.Type == ItemType.AgentMessage
                && item.Payload is not AgentMessagePayload { DeliveryMode: "async" })
            .Select(item => (item.Payload as AgentMessagePayload)?.Text)
            .Where(text => !string.IsNullOrEmpty(text)));
        if (string.IsNullOrWhiteSpace(replyText))
            return;

        await DeliverTextAsync(
            workspaceCraftPath,
            bindingId,
            authorityRevision,
            turn.ThreadId,
            turn.Id,
            replyText,
            "socialBindingReply",
            cancellationToken);
    }

    private async Task DeliverTextAsync(
        string workspaceCraftPath,
        string bindingId,
        long? authorityRevision,
        string threadId,
        string turnId,
        string text,
        string deliveryKind,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var binding = controlPlane.GetBinding(workspaceCraftPath, bindingId);
        var target = binding.State == AppBindingStates.Active
                     && (!authorityRevision.HasValue || binding.AuthorityRevision == authorityRevision.Value)
            ? binding.SocialTarget : null;
        if (target == null)
            return;

        if (!runtimeRegistry.TryGet(target.ChannelName, out var runtime) || runtime == null || !runtime.IsReady)
            return;

        var result = await runtime.DeliverAsync(
            target.DeliveryTarget,
            new ChannelDeliveryMessage
            {
                Kind = "text",
                Text = text
            },
            metadata: new
            {
                ThreadId = threadId,
                TurnId = turnId,
                BindingId = bindingId,
                AppId = $"com.dotharness.channel.{target.ChannelName.Trim().ToLowerInvariant()}",
                DeliveryKind = deliveryKind
            },
            cancellationToken);

        _ = result;
    }

    private static bool IsMatchingTurn(SessionTurn? turn, string bindingId, string queuedInputId) =>
        turn?.Input?.Payload is UserMessagePayload input
        && string.Equals(input.DeliveryBindingId, bindingId, StringComparison.Ordinal)
        && string.Equals(input.QueuedInputId, queuedInputId, StringComparison.Ordinal);
}
