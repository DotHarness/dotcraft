using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DotCraft.Tools;

internal static class ToolFailure
{
    public static ToolError FromException(Exception exception, string operation)
    {
        ToolInvocationDiagnostics.Report(exception, operation);
        var code = exception switch
        {
            ArgumentException or JsonException => ToolErrorCodes.InputInvalid,
            UnauthorizedAccessException => ToolErrorCodes.AccessDenied,
            TimeoutException => ToolErrorCodes.Timeout,
            _ => ToolErrorCodes.ExecutionFailed
        };
        return new ToolError(code, Message(exception, operation));
    }

    private static string Message(Exception exception, string operation) =>
        string.IsNullOrWhiteSpace(exception.Message)
            ? $"{operation} failed ({exception.GetType().Name})."
            : exception.Message;
}

internal static class ToolInvocationDiagnostics
{
    private static readonly AsyncLocal<Action<Exception, string>?> Sink = new();

    public static IDisposable Enter(Action<Exception, string>? sink)
    {
        var previous = Sink.Value;
        Sink.Value = sink ?? previous;
        return new Scope(previous);
    }

    public static IDisposable Enter(ToolInvocationContext context, ILogger? logger) => Enter(
        logger is null ? null : (exception, operation) => logger.LogWarning(exception,
            "Tool {ToolName} call {CallId} in thread {ThreadId} failed during {Operation}.",
            context.ToolName, context.CallId, context.ThreadId, operation));

    public static void Report(Exception exception, string operation)
    {
        try { Sink.Value?.Invoke(exception, operation); }
        catch { /* Diagnostics must not replace the original tool outcome. */ }
    }

    private sealed class Scope(Action<Exception, string>? previous) : IDisposable
    {
        public void Dispose() => Sink.Value = previous;
    }
}
