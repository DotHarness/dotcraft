using System.Runtime.CompilerServices;
using DotCraft.Sdk;

namespace DotCraft.Oratorio.Tests;

internal sealed partial class FakeAppServerClient
{
    public Action TurnStartedProcessed { get; init; } = () => { };
    public Action<string, string> TurnEnded { get; init; } = (_, _) => { };

    public async IAsyncEnumerable<DotCraftRunEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var events = outcome is FakeAppServerOutcome.TimeoutAfterTurnStarted or FakeAppServerOutcome.TimeoutAfterTurnStartedWithoutTerminal ||
            outcome == FakeAppServerOutcome.HoldInterruptWithoutTerminal && _interrupted
                ? ReadEventsThenCancelAsync(ct)
                : _notifications.Reader.ReadAllAsync(ct);
        await foreach (var notification in events.WithCancellation(ct))
        {
            if (notification.Type is "turn/completed" or "turn/failed" or "turn/cancelled" && notification.TurnId is { } turnId)
            {
                if (_activeTurnId == turnId) _activeTurnId = null;
                TurnEnded(notification.ThreadId, turnId);
            }
            yield return notification;
            if (notification.Type == "turn/started")
                TurnStartedProcessed();
        }
    }
}
