using System.Text.Json;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed partial class ThreadRequestHandler
{
    private const int ThreadTimelineDefaultPageLimit = 100;
    private const int ThreadTimelineMaxPageLimit = 500;

    private async Task<AppServerTypedResult<Contract.ThreadTimelineListResult>> HandleThreadTimelineListAsync(
        AppServerTypedRequest<Contract.ThreadTimelineListParams> request, CancellationToken ct)
    {
        var p = request.Params;
        var cursor = ThreadTimelineCursorCodec.Decode(p.Cursor, p.ThreadId);
        var limit = NormalizePageLimit(p.Limit, ThreadTimelineDefaultPageLimit, ThreadTimelineMaxPageLimit, "limit");
        ThreadTimelinePage page;
        try
        {
            page = await sessionService.ListThreadTimelineAsync(p.ThreadId, cursor, limit, ct);
        }
        catch (NotSupportedException ex)
        {
            throw AppServerErrors.Unsupported(ex.Message);
        }
        var data = new List<Contract.ThreadTimelineEntry>(page.Data.Count);
        foreach (var entry in page.Data)
        {
            Contract.SessionItem? item = null;
            if (entry.Item is { } sourceItem)
            {
                var projected = threadProjector.ProjectHistoryItems([new(entry.TurnId, sourceItem)]);
                if (projected.Count == 0)
                    continue;
                item = AppServerContractMapper.ToContract(projected[0].Item);
            }
            var completed = entry.Kind == ThreadTimelineEntryKind.TurnCompleted;
            data.Add(new()
            {
                Kind = entry.Kind switch
                {
                    ThreadTimelineEntryKind.Item => "item",
                    ThreadTimelineEntryKind.TurnStarted => "turnStarted",
                    ThreadTimelineEntryKind.TurnCompleted => "turnCompleted",
                    _ => throw new InvalidOperationException("Unknown timeline entry kind.")
                },
                Position = entry.Position,
                TurnId = entry.TurnId,
                Item = item,
                StartedAt = entry.Turn?.StartedAt,
                CompletedAt = completed ? entry.Turn!.CompletedAt : null,
                Status = completed ? JsonNamingPolicy.CamelCase.ConvertName(entry.Turn!.Status.ToString()) : null,
                Error = completed ? entry.Turn!.Error : null,
                ProviderError = completed ? entry.Turn!.ProviderError : null
            });
        }
        return AppServerTypedResult<Contract.ThreadTimelineListResult>.FromResult(new()
        {
            Data = data,
            NextCursor = page.NextCursor is { } next ? ThreadTimelineCursorCodec.Encode(p.ThreadId, next) : null
        });
    }
}
