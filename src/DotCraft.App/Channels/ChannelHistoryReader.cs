using DotCraft.AppServer;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.Channels;

internal static class ChannelHistoryReader
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public static async Task<ChannelHistoryResult> ReadAsync(
        IAppServerTransport transport,
        ChannelHistoryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await transport.RequestAsync(
                Contract.AppServerRpc.ExtChannelHistory,
                ChannelWireMapper.ToContract(request),
                cancellationToken,
                Timeout);
            if (response.Result is not { } result)
                return ChannelHistoryResult.Fail("AdapterProtocolViolation", "Adapter returned an invalid history payload.");

            var parsed = ChannelWireMapper.FromContract(result);
            if (parsed.ErrorCode != null || parsed.Messages.Count <= request.Limit)
                return parsed;
            return new ChannelHistoryResult
            {
                Messages = parsed.Messages.Skip(parsed.Messages.Count - request.Limit).ToList(),
                Before = parsed.Before
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ChannelHistoryResult.Fail("AdapterHistoryFailed", ex.Message);
        }
    }
}
