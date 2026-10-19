using System.Text;
using System.Text.Json.Nodes;
using DotCraft.Channels;
using DotCraft.Context.WorldState;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.ExternalChannel.Messaging;

internal sealed class ChannelChatSection(ChatBindingResolver resolver) : IWorldStateSection
{
    public const string SectionId = "channel_chat";

    private const string Heading = "## Channel";

    public string Id => SectionId;

    public JsonNode? Snapshot(WorldStateContext context) =>
        Read(context.Thread) switch
        {
            { Bound: false } => new JsonObject(),
            { Chat: { } chat } => chat.ToSnapshot(),
            _ => null
        };

    public string? RenderDiff(WorldStateContext context, PreviousSectionState previous)
    {
        var state = Read(context.Thread);
        if (state.Chat is { } chat)
            return Render(chat);
        if (state.Bound)
            return null;

        var previouslyShown = previous.Kind switch
        {
            PreviousSectionKind.Known => previous.Value is JsonObject values && values.ContainsKey("address"),
            PreviousSectionKind.Unknown => true,
            _ => false
        };
        return previouslyShown ? $"{Heading}\nThis conversation is no longer connected to a chat." : null;
    }

    private ChatState Read(SessionThread thread)
    {
        if (!ChatBindingResolver.IsRootThread(thread))
            return new ChatState(true, null);

        var binding = resolver.Resolve(thread.Id, thread.OriginChannel, thread.ChannelContext);
        if (binding == null)
            return new ChatState(false, null);
        if (!binding.CanDeliver
            || binding.Chat is not { } chat
            || !resolver.Registry.TryGet(binding.ChannelName, out var runtime)
            || runtime is not { IsReady: true }
            || runtime.GetDeliveryCapabilities() is not { } capabilities)
        {
            return new ChatState(true, null);
        }

        return new ChatState(true, new ChatFacts(
            runtime.Name,
            runtime.DisplayName,
            binding.Address,
            chat,
            capabilities.Text.Format,
            AttachmentKinds(capabilities.Media),
            capabilities.Targets));
    }

    private static string Render(ChatFacts facts)
    {
        var kind = facts.Chat.Kind switch
        {
            ChannelChatKinds.Direct => "direct chat",
            ChannelChatKinds.Group => "group",
            ChannelChatKinds.Topic => "topic",
            _ => "chat"
        };
        var label = facts.Chat.Label is { Length: > 0 } value ? $" \"{value}\"" : string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine(Heading);
        sb.AppendLine($"Default chat delivery destination: {facts.DisplayName} {kind}{label} ({facts.Address}).");
        if (facts.Chat.Kind != ChannelChatKinds.Direct)
        {
            if (facts.Chat.Membership == ChannelChatMemberships.Shared)
                sb.AppendLine("Messages from every member arrive here with the sender's name.");
            else if (facts.Chat.Membership == ChannelChatMemberships.Sender)
                sb.AppendLine($"This conversation holds only one member's messages in that {kind}; other members' messages are not here.");
        }

        var format = facts.Format == ChannelTextFormats.Markdown ? "Markdown" : "plain text";
        var attachments = facts.Attachments.Count == 0
            ? "no attachments"
            : JoinList(facts.Attachments.Select(Describe).ToArray());
        sb.AppendLine($"The chat renders {format} and accepts {attachments}.");
        if (DescribeReach(facts.DisplayName, facts.Targets) is { } reach)
            sb.AppendLine(reach);

        return sb.ToString().TrimEnd();
    }

    private static IReadOnlyList<string> AttachmentKinds(ChannelMediaCapabilitySnapshot? media)
    {
        var kinds = new List<string>();
        if (media?.Image != null) kinds.Add("image");
        if (media?.File != null) kinds.Add("file");
        if (media?.Audio != null) kinds.Add("audio");
        if (media?.Video != null) kinds.Add("video");
        return kinds;
    }

    private static string Describe(string kind) => kind switch
    {
        "image" => "images",
        "file" => "files",
        _ => kind
    };

    private static string? DescribeReach(string displayName, ChannelTargetCapabilitySnapshot targets)
    {
        var direct = targets.Direct switch
        {
            ChannelDirectReach.Any => "direct chats",
            ChannelDirectReach.KnownOnly => "direct chats with people who have messaged this bot",
            _ => null
        };
        var reachable = targets.Group
            ? direct == null ? "groups" : $"groups and {direct}"
            : direct;
        return reachable == null ? null : $"You can send to other {displayName} {reachable} by address.";
    }

    private static string JoinList(IReadOnlyList<string> items) =>
        items.Count == 1 ? items[0] : $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}";

    private sealed record ChatState(bool Bound, ChatFacts? Chat);

    private sealed record ChatFacts(
        string ChannelName,
        string DisplayName,
        string Address,
        ChannelChatDescriptor Chat,
        string Format,
        IReadOnlyList<string> Attachments,
        ChannelTargetCapabilitySnapshot Targets)
    {
        public JsonObject ToSnapshot() => new()
        {
            ["channel"] = ChannelName,
            ["displayName"] = DisplayName,
            ["address"] = Address,
            ["kind"] = Chat.Kind,
            ["label"] = Chat.Label,
            ["membership"] = Chat.Membership,
            ["format"] = Format,
            ["attachments"] = new JsonArray(Attachments.Select(static kind => (JsonNode)kind).ToArray()),
            ["targets"] = new JsonObject { ["group"] = Targets.Group, ["direct"] = Targets.Direct }
        };
    }
}
