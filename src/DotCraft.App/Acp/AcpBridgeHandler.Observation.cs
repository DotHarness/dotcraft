using System.Collections.Concurrent;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.Acp;

public sealed partial class AcpBridgeHandler
{
    private readonly ConcurrentDictionary<string, SessionObservation> _knownSessions = new();

    private async Task ObserveSessionAsync(string sessionId, CancellationToken sessionLifetime)
    {
        var session = _knownSessions.GetOrAdd(sessionId, _ => new SessionObservation(sessionLifetime));
        await session.Gate.WaitAsync(sessionLifetime);
        try
        {
            session.Observers++;
        }
        finally
        {
            session.Gate.Release();
        }
    }

    private async Task ReleaseSessionObservationAsync(string sessionId)
    {
        if (!_knownSessions.TryGetValue(sessionId, out var session))
            return;
        await session.Gate.WaitAsync();
        try
        {
            if (--session.Observers != 0)
                return;
            using var response = await wire.SendRequestAsync(
                Contract.AppServerMethodNames.ThreadUnsubscribe, new { threadId = sessionId });
            ThrowIfWireError(response, "thread/unsubscribe");
        }
        catch (Exception ex)
        {
            logger?.LogError($"Could not release ACP observation [session={sessionId}]", ex);
        }
        finally
        {
            session.Gate.Release();
        }
    }

    private sealed class SessionObservation(CancellationToken lifetime)
    {
        public CancellationToken Lifetime { get; } = lifetime;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int Observers { get; set; }
    }
}
