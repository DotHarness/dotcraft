using DotCraft.Sessions;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public partial class TestableSessionService
{
    public Func<string, ThreadTaskReport, bool>? ThreadNotificationHandler { get; set; }

    public async Task<bool> NotifyThreadAsync(string threadId, ThreadTaskReport report, CancellationToken ct = default)
    {
        await GetThreadAsync(threadId, ct);
        return ThreadNotificationHandler?.Invoke(threadId, report) ?? true;
    }
}
