using DotCraft.AppBinding;
using DotCraft.Automations;
using DotCraft.Channels;
using DotCraft.Sessions;
using DotCraft.Workspaces;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.ExternalChannel.Messaging;

internal sealed record ChatBinding(
    string ChannelName,
    string Target,
    ChannelChatDescriptor? Chat,
    bool RefusesOtherChats = false,
    bool CanDeliver = true)
{
    public string Address => ChatAddress.Format(ChannelName, Target);
}

internal static class ChatAddress
{
    public static string Format(string channelName, string target) => $"{channelName}:{target}";

    public static bool TryParse(string? address, out string channelName, out string target)
    {
        channelName = string.Empty;
        target = string.Empty;
        var value = address?.Trim();
        var separator = value?.IndexOf(':') ?? -1;
        if (value == null || separator <= 0 || separator == value.Length - 1)
            return false;

        channelName = value[..separator].Trim();
        target = value[(separator + 1)..].Trim();
        return channelName.Length > 0 && target.Length > 0;
    }
}

internal sealed class ChatBindingResolver(
    IChannelRuntimeRegistry registry,
    DotCraftPaths? paths = null,
    AppBindingService? appBindings = null,
    AutomationService? automations = null)
{
    private static readonly HashSet<string> BindsChat = new(StringComparer.Ordinal)
    {
        AppBindingStates.Active,
        AppBindingStates.Offline,
        AppBindingStates.Syncing,
        AppBindingStates.NeedsConfirmation
    };

    public IChannelRuntimeRegistry Registry => registry;

    public static bool IsRootThread(SessionThread thread) =>
        thread.Source?.SubAgent is null
        && !string.Equals(thread.Source?.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(thread.OriginChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase)
        && !ThreadVisibility.IsHidden(thread);

    public ChatBinding? Resolve(string threadId, string? originChannel, string? channelContext)
    {
        if (automations?.FindRunDefinition(originChannel, channelContext) is { } automation
            && ResolveAutomationRun(automation) is { } run)
        {
            return run;
        }

        if (!string.IsNullOrWhiteSpace(originChannel)
            && !string.IsNullOrWhiteSpace(channelContext)
            && registry.TryGet(originChannel, out var runtime)
            && runtime != null)
        {
            return new ChatBinding(runtime.Name, channelContext.Trim(), runtime.GetThreadChat(threadId));
        }

        return ResolveAppBinding(threadId);
    }

    private ChatBinding? ResolveAutomationRun(AutomationDefinition automation)
    {
        if (automation.Origin is not { IsChat: true } origin
            || !registry.TryGet(origin.Channel, out var runtime)
            || runtime == null)
        {
            return null;
        }

        return new ChatBinding(
            runtime.Name,
            origin.DeliveryTarget!,
            origin.Chat,
            RefusesOtherChats: automation.ApprovalPolicy != "fullAuto");
    }

    private ChatBinding? ResolveAppBinding(string threadId)
    {
        if (paths == null || appBindings == null)
            return null;

        IReadOnlyList<AppBindingSnapshot> bindings;
        try
        {
            bindings = appBindings.ListThreadBindings(paths.Data.RootPath, threadId);
        }
        catch (AppBindingException)
        {
            return null;
        }

        var binding = bindings
            .Where(candidate => BindsChat.Contains(candidate.State)
                && candidate.ChannelTarget is { } target
                && !string.IsNullOrWhiteSpace(target.ChannelName)
                && !string.IsNullOrWhiteSpace(target.DeliveryTarget))
            .OrderByDescending(candidate => candidate.State == AppBindingStates.Active)
            .FirstOrDefault();
        if (binding == null)
            return null;

        var target = binding.ChannelTarget!;

        var kind = string.Equals(target.ConversationKind, "user", StringComparison.OrdinalIgnoreCase)
            ? ChannelChatKinds.Direct
            : ChannelChatKinds.Group;
        var label = string.IsNullOrWhiteSpace(target.DisplayName) ? null : target.DisplayName.Trim();
        return new ChatBinding(
            target.ChannelName,
            target.DeliveryTarget,
            new ChannelChatDescriptor(kind, label, ChannelChatMemberships.Shared),
            CanDeliver: binding.State == AppBindingStates.Active);
    }
}
