namespace DotCraft.Sessions;

public sealed record BoardChannelView(string Name, string? Description, string CreatedBy, DateTimeOffset CreatedAt, int TopicCount);

public sealed record BoardPostView(
    string MessageId,
    string ChannelName,
    string TopicId,
    string Author,
    DateTimeOffset CreatedAt,
    string Text,
    IReadOnlyList<string> Notified);

public sealed record BoardTopicView(string TopicId, string ChannelName, BoardPostView RootPost, int ReplyCount, DateTimeOffset LastActivityAt);

public sealed record BoardTopicPage(IReadOnlyList<BoardChannelView> Channels, IReadOnlyList<BoardTopicView> Topics, string? NextCursor);

public sealed record BoardTopicPosts(BoardTopicView? Topic, IReadOnlyList<BoardPostView> Posts, bool HasMore);
