using Microsoft.EntityFrameworkCore;
using Oratorio.Server.Data;
using BoardTaskStatus = Oratorio.Server.Domain.TaskStatus;

namespace Oratorio.Server.Services;

public sealed class TaskBoardPlacementService(
    OratorioDbContext db,
    TaskShortIdAllocator shortIdAllocator)
{
    public async Task AssignNewItemProjectionAsync(OratorioItem item, BoardTaskStatus status, CancellationToken ct)
    {
        item.BoardSortOrder = await NextBoardSortOrderAsync(status, ct);
        await shortIdAllocator.ReserveAsync(item, ct);
    }

    private async Task<long> NextBoardSortOrderAsync(BoardTaskStatus status, CancellationToken ct)
    {
        var columnIndex = BoardColumnIndex(status);
        var columnStart = ComposeBoardSortOrder(columnIndex, 0);
        var columnEnd = ComposeBoardSortOrder(columnIndex + 1, 0);
        var maxOrder = await db.Items
            .Where(x => x.BoardSortOrder >= columnStart && x.BoardSortOrder < columnEnd)
            .MaxAsync(x => (long?)x.BoardSortOrder, ct);

        return maxOrder is null ? columnStart : maxOrder.Value + 1;
    }

    private static long ComposeBoardSortOrder(int columnIndex, int position) => (columnIndex * 1000L) + position;

    private static int BoardColumnIndex(BoardTaskStatus status) =>
        status switch
        {
            BoardTaskStatus.Todo => 0,
            BoardTaskStatus.InProgress => 1,
            BoardTaskStatus.InReview => 2,
            BoardTaskStatus.Done => 3,
            BoardTaskStatus.Cancelled => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported task status.")
        };
}
