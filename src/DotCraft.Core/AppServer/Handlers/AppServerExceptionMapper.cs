namespace DotCraft.AppServer;

internal static class AppServerExceptionMapper
{
    public static AppServerException MapOperationException(InvalidOperationException ex)
    {
        var msg = ex.Message;
        var id = ExtractQuotedId(msg);

        if (msg.Contains("archived and cannot be resumed") || msg.Contains("is not Active"))
            return AppServerErrors.ThreadNotActive(id);

        if (msg.Contains("already has a running Turn")
            || msg.Contains("has a running Turn")
            || msg.Contains("has active thread maintenance"))
            return AppServerErrors.TurnInProgress(id);

        if (msg.Contains("client-managed history")
            || msg.Contains("server-managed history")
            || msg.Contains("SubAgent child thread")
            || msg.Contains("deliveryMode")
            || msg.Contains("has no goal")
            || msg.Contains("already has a goal")
            || msg.Contains("has no history")
            || msg.Contains("has no completed turn")
            || msg.Contains("has no model-visible history"))
        {
            return AppServerErrors.InvalidParams(msg);
        }

        return AppServerErrors.InternalError(msg);
    }

    public static string ExtractQuotedId(string message)
    {
        var start = message.IndexOf('\'');
        if (start < 0) return string.Empty;
        var end = message.IndexOf('\'', start + 1);
        return end > start ? message[(start + 1)..end] : string.Empty;
    }
}
