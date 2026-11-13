using DotCraft.Sessions;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public static class CannedTurnAdmission
{
    public static async Task<TurnInputAdmission> CaptureAsync(IAsyncEnumerable<SessionEvent> events, CancellationToken ct)
    {
        var reader = events.GetAsyncEnumerator(ct);
        try
        {
            while (await reader.MoveNextAsync())
            {
                if (reader.Current is { EventType: SessionEventType.TurnStarted, TurnPayload: { } turn })
                    return new TurnInputAdmission(turn, true, ContinueAsync(reader));
            }
            throw new InvalidOperationException("Execution completed without admitting a Turn.");
        }
        catch
        {
            await reader.DisposeAsync();
            throw;
        }
    }

    private static async IAsyncEnumerable<SessionEvent> ContinueAsync(IAsyncEnumerator<SessionEvent> reader)
    {
        try
        {
            while (await reader.MoveNextAsync())
                yield return reader.Current;
        }
        finally { await reader.DisposeAsync(); }
    }
}
