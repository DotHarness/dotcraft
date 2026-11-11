using DotCraft.AppServer;
using DotCraft.Channels;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.ExternalChannel;

internal static class ExternalChannelHistoryReader
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
                ExternalChannelWireMapper.ToContract(request),
                cancellationToken,
                Timeout);
            if (response.Result is not { } result)
                return ChannelHistoryResult.Fail("AdapterProtocolViolation", "Adapter returned an invalid history payload.");

            var parsed = ExternalChannelWireMapper.FromContract(result);
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
