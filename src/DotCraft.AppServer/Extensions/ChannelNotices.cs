using DotCraft.Sessions;

namespace DotCraft.AppBinding;

internal static class ChannelNotices
{
    public static void Append(ISessionService sessionService, AppBindingSnapshot binding, string reason)
    {
        if (binding.ChannelTarget is not { } target || sessionService is not IThreadSystemNoticeService notices)
            return;
        notices.AppendSystemNotice(binding.ThreadId, new SystemNoticePayload
        {
            Kind = "channel",
            Reason = reason,
            ChannelName = target.ChannelName,
            TargetName = string.IsNullOrWhiteSpace(target.DisplayName) ? target.ConversationId : target.DisplayName
        });
    }
}
