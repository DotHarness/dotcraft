using System.Text;
using Microsoft.EntityFrameworkCore;
using Oratorio.Server.Data;

namespace Oratorio.Server.Services;

public sealed class TaskShortIdAllocator(OratorioDbContext db, IClock clock)
{
    public static string DerivePrefix(string? workspaceId)
    {
        var builder = new StringBuilder(capacity: 3);
        foreach (var ch in workspaceId ?? "")
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                builder.Append(char.ToUpperInvariant(ch));
                if (builder.Length == 3)
                {
                    break;
                }
            }
        }

        return builder.Length == 0 ? "WS" : builder.ToString();
    }

    public async Task ReserveAsync(OratorioItem item, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(item.ShortId) && item.ShortIdInteger is not null)
        {
            return;
        }

        var workspaceId = string.IsNullOrWhiteSpace(item.WorkspaceId) ? "default" : item.WorkspaceId.Trim();
        var prefix = DerivePrefix(workspaceId);
        var now = clock.UtcNow;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var counter = await db.TaskShortIdCounters.FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct);
        if (counter is null)
        {
            counter = new OratorioTaskShortIdCounter
            {
                WorkspaceId = workspaceId,
                Prefix = prefix,
                NextValue = 1,
                UpdatedAt = now
            };
            db.TaskShortIdCounters.Add(counter);
            await db.SaveChangesAsync(ct);
        }

        var value = counter.NextValue;
        counter.Prefix = prefix;
        counter.NextValue = value + 1;
        counter.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        item.WorkspaceId = workspaceId;
        item.ShortIdInteger = value;
        item.ShortId = $"{prefix}-{value}";
    }
}
