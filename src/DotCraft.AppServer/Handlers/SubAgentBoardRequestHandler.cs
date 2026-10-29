using DotCraft.Sessions;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed class SubAgentBoardRequestHandler(ISessionService sessionService) : IAppServerDomainHandler
{
    private const int DefaultTopicLimit = 50;
    private const int MaxTopicLimit = 100;

    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Contract.AppServerRpc.SubAgentBoardTopicsList, HandleTopicsListAsync);
        table.Map(Contract.AppServerRpc.SubAgentBoardTopicRead, HandleTopicReadAsync);
    }

    private async Task<AppServerTypedResult<Contract.SubAgentBoardTopicsListResult>> HandleTopicsListAsync(
        AppServerTypedRequest<Contract.SubAgentBoardTopicsListParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var limit = Math.Clamp(SubAgentContractMapper.Read(p.Limit) ?? DefaultTopicLimit, 1, MaxTopicLimit);
        var page = await sessionService.ListBoardTopicsAsync(
            RequireThreadId(SubAgentContractMapper.Read(p.ThreadId)),
            SubAgentContractMapper.Read(p.ChannelName),
            SubAgentContractMapper.Read(p.Cursor),
            limit,
            ct);
        return AppServerTypedResult<Contract.SubAgentBoardTopicsListResult>.FromResult(new Contract.SubAgentBoardTopicsListResult
        {
            Channels = page.Channels.Select(ToContract).ToList(),
            Data = page.Topics.Select(ToContract).ToList(),
            NextCursor = page.NextCursor
        });
    }

    private async Task<AppServerTypedResult<Contract.SubAgentBoardTopicReadResult>> HandleTopicReadAsync(
        AppServerTypedRequest<Contract.SubAgentBoardTopicReadParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var topicId = SubAgentContractMapper.Read(p.TopicId);
        if (string.IsNullOrWhiteSpace(topicId))
            throw AppServerErrors.InvalidParams("'topicId' is required.");
        var read = await sessionService.ReadBoardTopicAsync(RequireThreadId(SubAgentContractMapper.Read(p.ThreadId)), topicId, ct);
        return AppServerTypedResult<Contract.SubAgentBoardTopicReadResult>.FromResult(new Contract.SubAgentBoardTopicReadResult
        {
            Topic = read.Topic is null ? null : ToContract(read.Topic),
            Posts = read.Posts.Select(ToContract).ToList(),
            HasMore = read.HasMore
        });
    }

    private static string RequireThreadId(string? threadId) =>
        string.IsNullOrWhiteSpace(threadId) ? throw AppServerErrors.InvalidParams("'threadId' is required.") : threadId.Trim();

    private static Contract.SubAgentBoardChannel ToContract(BoardChannelView channel) => new()
    {
        Name = channel.Name,
        Description = channel.Description,
        CreatedBy = channel.CreatedBy,
        CreatedAt = channel.CreatedAt,
        TopicCount = channel.TopicCount
    };

    private static Contract.SubAgentBoardTopic ToContract(BoardTopicView topic) => new()
    {
        TopicId = topic.TopicId,
        ChannelName = topic.ChannelName,
        RootPost = ToContract(topic.RootPost),
        ReplyCount = topic.ReplyCount,
        LastActivityAt = topic.LastActivityAt
    };

    private static Contract.SubAgentBoardPost ToContract(BoardPostView post) => new()
    {
        MessageId = post.MessageId,
        ChannelName = post.ChannelName,
        TopicId = post.TopicId,
        Author = post.Author,
        CreatedAt = post.CreatedAt,
        Text = post.Text,
        Notified = post.Notified.ToList()
    };
}
