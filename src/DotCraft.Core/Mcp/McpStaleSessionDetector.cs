using ModelContextProtocol.Client;
using System.Net;
using System.Net.Http;

namespace DotCraft.Mcp;

internal static class McpStaleSessionDetector
{
    public static bool IsStaleSessionFailure(Exception exception, bool requestHadSessionId = false)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var exceptions = EnumerateExceptionChain(exception).ToArray();
        var message = string.Join('\n', exceptions.Select(static ex => ex.Message));
        var compactMessage = new string(message.Where(static ch => !char.IsWhiteSpace(ch)).ToArray());

        if (!HasNotFoundStatus(exceptions, message))
            return false;

        return requestHadSessionId || HasStaleSessionMarker(message, compactMessage);
    }

    private static bool HasNotFoundStatus(IEnumerable<Exception> exceptions, string message) =>
        exceptions.OfType<HttpRequestException>().Any(static ex => ex.StatusCode == HttpStatusCode.NotFound) ||
        exceptions.OfType<ClientTransportClosedException>().Any(static ex =>
            ex.Details is HttpClientCompletionDetails { HttpStatusCode: HttpStatusCode.NotFound }) ||
        message.Contains("404", StringComparison.OrdinalIgnoreCase);

    private static bool HasStaleSessionMarker(string message, string compactMessage) =>
        message.Contains("Session not found", StringComparison.OrdinalIgnoreCase) ||
        compactMessage.Contains("\"code\":-32001", StringComparison.OrdinalIgnoreCase) ||
        compactMessage.Contains("code:-32001", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("-32001", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<Exception> EnumerateExceptionChain(Exception exception)
    {
        var current = exception;
        while (current != null)
        {
            yield return current;

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.Flatten().InnerExceptions)
                {
                    foreach (var nested in EnumerateExceptionChain(inner))
                        yield return nested;
                }
            }

            current = current.InnerException;
        }
    }
}
