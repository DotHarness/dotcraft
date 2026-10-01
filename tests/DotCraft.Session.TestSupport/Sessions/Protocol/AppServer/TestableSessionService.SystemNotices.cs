using DotCraft.Sessions;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public partial class TestableSessionService : IThreadSystemNoticeService
{
    public List<(string ThreadId, SystemNoticePayload Payload)> AppendedSystemNotices { get; } = [];

    public void AppendSystemNotice(string threadId, SystemNoticePayload payload) =>
        AppendedSystemNotices.Add((threadId, payload));
}
