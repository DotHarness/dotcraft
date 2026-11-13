using System.Globalization;
using System.Text;
using DotCraft.Sessions;

namespace DotCraft.AppServer;

internal sealed partial class ThreadRequestHandler
{
    private static string EncodeThreadListCursor(ThreadListCursor cursor)
    {
        var raw = $"{ThreadListCursorKind}:{cursor.LastActiveAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}:{cursor.ThreadId}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static ThreadListCursor? DecodeThreadListCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;
        try
        {
            var token = cursor.Trim().Replace('-', '+').Replace('_', '/');
            token = token.PadRight(token.Length + (4 - token.Length % 4) % 4, '=');
            var fields = Encoding.UTF8.GetString(Convert.FromBase64String(token)).Split(':', 3);
            if (fields.Length == 3 && fields[0] == ThreadListCursorKind
                && long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks >= DateTimeOffset.MinValue.UtcTicks && ticks <= DateTimeOffset.MaxValue.UtcTicks
                && !string.IsNullOrWhiteSpace(fields[2]))
                return new ThreadListCursor(new DateTimeOffset(ticks, TimeSpan.Zero), fields[2]);
        }
        catch (FormatException)
        {
        }
        throw AppServerErrors.InvalidParams("'cursor' is invalid.");
    }
}
