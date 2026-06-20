using DotCraft.Abstractions;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.AppBinding;

/// <summary>
/// Delivers the final assistant reply for turns initiated from social-channel app bindings.
/// </summary>
public sealed class SocialChannelDeliveryCoordinator(
    AppBindingService appBindingService,
    IChannelRuntimeRegistry runtimeRegistry)
{
    private static readonly TimeSpan DefaultObservationTimeout = TimeSpan.FromHours(12);

    public void StartQueuedTurnDelivery(
        ISessionService sessionService,
        string workspaceCraftPath,
        string threadId,
        string bindingId,
        string queuedInputId,
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
                    cancellationToken);
            }
            catch (Exception ex)
            {
                // Delivery is best-effort and must never fault turn execution or AppServer request handling.
                appBindingService.RecordSocialDelivery(
                    workspaceCraftPath,
                    bindingId,
                    queuedInputId,
                    delivered: false,
                    diagnostic: $"deliveryObserverFailed:{ex.GetType().Name}");
            }
        }, CancellationToken.None);
    }

    internal async Task ObserveQueuedTurnAsync(
        ISessionService sessionService,
        string workspaceCraftPath,
        string threadId,
        string bindingId,
        string queuedInputId,
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
                case SessionEventType.TurnCompleted:
                    await DeliverCompletedTurnAsync(workspaceCraftPath, bindingId, evt.TurnPayload, linked.Token);
                    return;
                case SessionEventType.TurnFailed:
                    appBindingService.RecordSocialDelivery(
                        workspaceCraftPath,
                        bindingId,
                        matchedTurnId,
                        delivered: false,
                        diagnostic: "turnFailed");
                    return;
                case SessionEventType.TurnCancelled:
                    appBindingService.RecordSocialDelivery(
                        workspaceCraftPath,
                        bindingId,
                        matchedTurnId,
                        delivered: false,
                        diagnostic: "turnCancelled");
                    return;
            }
        }
    }

    private async Task DeliverCompletedTurnAsync(
        string workspaceCraftPath,
        string bindingId,
        SessionTurn? turn,
        CancellationToken cancellationToken)
    {
        if (turn == null)
            return;

        var replyText = string.Concat(turn.Items
            .Where(item => item.Type == ItemType.AgentMessage)
            .Select(item => (item.Payload as AgentMessagePayload)?.Text)
            .Where(text => !string.IsNullOrEmpty(text)));
        if (string.IsNullOrWhiteSpace(replyText))
        {
            appBindingService.RecordSocialDelivery(workspaceCraftPath, bindingId, turn.Id, delivered: true, "empty");
            return;
        }

        var target = appBindingService.GetActiveSocialTarget(workspaceCraftPath, bindingId);
        if (target == null)
        {
            appBindingService.RecordSocialDelivery(workspaceCraftPath, bindingId, turn.Id, delivered: false, "bindingUnavailable");
            return;
        }

        if (!runtimeRegistry.TryGet(target.ChannelName, out var runtime) || runtime == null || !runtime.IsReady)
        {
            appBindingService.RecordSocialDelivery(workspaceCraftPath, bindingId, turn.Id, delivered: false, "channelOffline");
            return;
        }

        var result = await runtime.DeliverAsync(
            target.DeliveryTarget,
            new ChannelOutboundMessage
            {
                Kind = "text",
                Text = replyText
            },
            metadata: new
            {
                turn.ThreadId,
                TurnId = turn.Id,
                BindingId = bindingId,
                AppId = SocialChannelAppBindingRuntime.AppIdForChannel(target.ChannelName),
                DeliveryKind = "socialBindingReply"
            },
            cancellationToken);

        appBindingService.RecordSocialDelivery(
            workspaceCraftPath,
            bindingId,
            turn.Id,
            result.Delivered,
            result.Delivered ? result.RemoteMessageId : result.ErrorCode ?? result.ErrorMessage ?? "deliveryFailed");
    }

    private static bool IsMatchingTurn(SessionTurn? turn, string bindingId, string queuedInputId) =>
        turn?.Input?.Payload is UserMessagePayload input
        && string.Equals(input.DeliveryBindingId, bindingId, StringComparison.Ordinal)
        && string.Equals(input.QueuedInputId, queuedInputId, StringComparison.Ordinal);
}
