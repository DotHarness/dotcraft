using System.Text;
using System.Text.Json;
using DotCraft.Sessions;

internal static class SmokeAssertions
{
    public static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public static async Task AssertEcho(
        ISessionService sessions, string thread, string workspace, string toolNamespace, string value)
    {
        var observed = await RunToolTurn(sessions, thread, toolNamespace, "Echo", value);
        var result = observed.Result;
        Ensure(result.Success, "Typed echo failed: " + result.Result);
        Ensure(result.Source?.PluginId == (toolNamespace == "PackageBinary" ? "package.binary" : "package.source"),
            "The Session result lost plugin provenance.");
        using var document = JsonDocument.Parse(result.Result);
        var payload = document.RootElement;
        Ensure(payload.GetProperty("value").GetString() == value.ToUpperInvariant() + value.ToUpperInvariant(),
            "The DTO, enum, or optional parameter was not bound correctly.");
        Ensure(payload.GetProperty("threadId").GetString() == thread, "The plugin received another thread's context.");
        Ensure(payload.GetProperty("turnId").GetString() == observed.TurnId, "The plugin received a stale Turn context.");
        Ensure(payload.GetProperty("callId").GetString() == result.CallId, "The live call identity was not injected.");
        Ensure(Path.GetFullPath(payload.GetProperty("workspacePath").GetString()!) == Path.GetFullPath(workspace),
            "The live workspace was not injected.");
    }

    public static async Task AssertEnvelope(ISessionService sessions, string thread, string toolNamespace, string pluginId)
    {
        var observed = await RunToolTurn(sessions, thread, toolNamespace, "Envelope", "unknown");
        var result = observed.Result;
        Ensure(!result.Success && result.ErrorCode == "package_outcome_unknown",
            "The full result was flattened into a successful serialized envelope.");
        Ensure(result.Result.Contains("package-outcome-unknown:" + thread, StringComparison.Ordinal),
            "The explicit result content or context was lost.");
        Ensure(result.Source?.PluginId == pluginId, "The failed result lost plugin provenance.");
        Ensure(result.Meta is null, "The plugin crossed the host-private result boundary.");
    }

    public static async Task<ObservedToolResult> RunToolTurn(
        ISessionService sessions, string thread, string toolNamespace, string method, string value)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var results = new Dictionary<string, ObservedToolResult>(StringComparer.Ordinal);
        var response = new StringBuilder();
        var completed = false;
        await foreach (var sessionEvent in sessions.SubmitInputAsync(
            thread, $"package-smoke|{toolNamespace}|{method}|{value}", ct: timeout.Token))
        {
            if (sessionEvent.DeltaPayload?.TextDelta is { } delta)
                response.Append(delta);
            if (sessionEvent.ItemPayload?.AsToolResult is { } result)
                results[result.CallId] = new ObservedToolResult(result, sessionEvent.TurnId);
            if (sessionEvent.EventType == SessionEventType.TurnCompleted)
                completed = true;
            if (sessionEvent.EventType == SessionEventType.TurnFailed)
                throw new InvalidOperationException(sessionEvent.TurnFailedPayload?.Error ?? "The smoke-test Turn failed.");
        }
        Ensure(completed, "The smoke-test Turn did not complete.");
        Ensure(response.ToString().Contains("package-smoke-ok", StringComparison.Ordinal),
            "The model did not receive the tool result or the plugin supplied a host-private turn directive.");
        var expected = results.Values.Where(item => item.Result.Namespace == toolNamespace && item.Result.ToolName == method).ToArray();
        Ensure(expected.Length == 1, $"Expected one Session ToolResult for {toolNamespace}.{method}; observed {results.Count}.");
        return expected[0];
    }

    internal sealed record ObservedToolResult(ToolResultPayload Result, string? TurnId);
}
