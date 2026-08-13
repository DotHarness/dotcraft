using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Tracing;
using Microsoft.Data.Sqlite;

namespace DotCraft.AppServerTestClient;

internal sealed record DeferredLoadingSmokeTraceEvent(
    long Id,
    string SessionKey,
    string Type,
    string EventJson);

internal static class DeferredLoadingSmokeTraceReader
{
    public static IReadOnlyList<DeferredLoadingSmokeTraceEvent> ReadThreadEvents(
        string traceDbPath,
        string threadId)
    {
        if (!File.Exists(traceDbPath))
            return [];

        var result = new List<DeferredLoadingSmokeTraceEvent>();
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = traceDbPath,
            Mode = SqliteOpenMode.ReadOnly
        };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, session_key, type, event_json
            FROM trace_events
            WHERE session_key = $session_key
            ORDER BY timestamp, id
            """;
        command.Parameters.AddWithValue("$session_key", threadId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new DeferredLoadingSmokeTraceEvent(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return result;
    }
}

internal static class DeferredLoadingSmokeTraceValidator
{
    public static DeferredLoadingSmokeValidationResult Validate(
        IReadOnlyList<DeferredLoadingSmokeTraceEvent> events,
        string protocol,
        string targetToolName)
    {
        var expectedWireShape = ExpectedWireShape(protocol);
        var deferredEvents = events
            .Where(static e => e.Type == nameof(TraceEventType.DeferredToolLoading))
            .ToArray();

        if (deferredEvents.Length == 0)
            return DeferredLoadingSmokeValidationResult.Fail("deferred_loading_missing");

        var matchingDeferredEvent = deferredEvents
            .Select(ReadDeferredLoadingEvent)
            .FirstOrDefault(evt => evt != null
                                   && string.Equals(evt.ProviderProtocol, protocol, StringComparison.Ordinal)
                                   && string.Equals(evt.WireShape, expectedWireShape, StringComparison.Ordinal));

        if (deferredEvents.Any(IsPromptCacheDiagnosticMarked))
            return DeferredLoadingSmokeValidationResult.Fail(
                "deferred_loading_marked_prompt_cache_extension",
                deferredToolLoadingObserved: true,
                wireShape: matchingDeferredEvent?.WireShape);

        if (matchingDeferredEvent == null)
        {
            var first = deferredEvents
                .Select(ReadDeferredLoadingEvent)
                .FirstOrDefault(evt => evt != null);
            if (first != null && !string.Equals(first.ProviderProtocol, protocol, StringComparison.Ordinal))
            {
                return DeferredLoadingSmokeValidationResult.Fail(
                    "deferred_loading_provider_protocol_mismatch",
                    deferredToolLoadingObserved: true,
                    wireShape: first.WireShape);
            }

            return DeferredLoadingSmokeValidationResult.Fail(
                "deferred_loading_wire_shape_mismatch",
                deferredToolLoadingObserved: true,
                wireShape: first?.WireShape);
        }

        if (!matchingDeferredEvent.Tools.Contains(targetToolName, StringComparer.Ordinal))
        {
            return DeferredLoadingSmokeValidationResult.Fail(
                "deferred_loading_target_tool_not_activated",
                deferredToolLoadingObserved: true,
                wireShape: matchingDeferredEvent.WireShape);
        }

        if (!HasToolEvent(events, nameof(TraceEventType.ToolCallStarted), targetToolName)
            || !HasToolEvent(events, nameof(TraceEventType.ToolCallCompleted), targetToolName))
        {
            return DeferredLoadingSmokeValidationResult.Fail(
                "deferred_loading_target_tool_not_called",
                deferredToolLoadingObserved: true,
                wireShape: matchingDeferredEvent.WireShape);
        }

        if (!HasFinalSuccessToken(events))
        {
            return DeferredLoadingSmokeValidationResult.Fail(
                "deferred_loading_success_token_missing",
                deferredToolLoadingObserved: true,
                wireShape: matchingDeferredEvent.WireShape);
        }

        return DeferredLoadingSmokeValidationResult.Pass(
            $"deferred loading activated {targetToolName} via {matchingDeferredEvent.WireShape}",
            matchingDeferredEvent.WireShape,
            targetToolName);
    }

    public static string ExpectedWireShape(string protocol) =>
        protocol switch
        {
            ModelProviderProtocols.Anthropic => "anthropic_tool_reference",
            ModelProviderProtocols.OpenAIResponses => "openai_responses_tool_search_output",
            _ => throw new ArgumentException($"Unsupported deferred loading smoke protocol: {protocol}", nameof(protocol))
        };

    private static bool IsPromptCacheDiagnosticMarked(DeferredLoadingSmokeTraceEvent evt)
    {
        try
        {
            using var doc = JsonDocument.Parse(evt.EventJson);
            var root = doc.RootElement;
            if (TryGetProperty(root, "PromptCacheEventKind", out var kind)
                && kind.ValueKind != JsonValueKind.Null
                && !string.IsNullOrWhiteSpace(kind.GetString()))
            {
                return true;
            }

            if (TryGetProperty(root, "PromptCacheChangedFields", out var changedFields)
                && changedFields.ValueKind == JsonValueKind.Array
                && changedFields.GetArrayLength() > 0)
            {
                return true;
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private static DeferredLoadingTraceSnapshot? ReadDeferredLoadingEvent(DeferredLoadingSmokeTraceEvent evt)
    {
        try
        {
            using var doc = JsonDocument.Parse(evt.EventJson);
            var root = doc.RootElement;
            var metadataJson = TryGetProperty(root, "MetadataJson", out var metadata)
                ? metadata.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(metadataJson))
                return null;

            using var metadataDoc = JsonDocument.Parse(metadataJson);
            var metadataRoot = metadataDoc.RootElement;
            var providerProtocol = ReadString(metadataRoot, "providerProtocol");
            var wireShape = ReadString(metadataRoot, "wireShape");
            var tools = new List<string>();
            if (TryGetProperty(metadataRoot, "tools", out var toolArray)
                && toolArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var tool in toolArray.EnumerateArray())
                {
                    var name = ReadString(tool, "name");
                    if (!string.IsNullOrWhiteSpace(name))
                        tools.Add(name);
                }
            }

            return new DeferredLoadingTraceSnapshot(
                providerProtocol ?? string.Empty,
                wireShape ?? string.Empty,
                tools);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasToolEvent(
        IReadOnlyList<DeferredLoadingSmokeTraceEvent> events,
        string type,
        string targetToolName) =>
        events.Any(evt => evt.Type == type
                          && string.Equals(ReadTopLevelString(evt.EventJson, "ToolName"), targetToolName, StringComparison.Ordinal));

    private static bool HasFinalSuccessToken(IReadOnlyList<DeferredLoadingSmokeTraceEvent> events) =>
        events.Any(evt => evt.Type == nameof(TraceEventType.Response)
                          && (ReadTopLevelString(evt.EventJson, "Content") ?? string.Empty)
                          .Contains(DeferredLoadingSmokeTools.SuccessToken, StringComparison.Ordinal));

    private static string? ReadTopLevelString(string eventJson, string propertyName)
    {
        try
        {
            using var doc = JsonDocument.Parse(eventJson);
            return TryGetProperty(doc.RootElement, propertyName, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private sealed record DeferredLoadingTraceSnapshot(
        string ProviderProtocol,
        string WireShape,
        IReadOnlyList<string> Tools);
}

internal sealed record DeferredLoadingSmokeValidationResult(
    bool Success,
    string? Message = null,
    bool? DeferredToolLoadingObserved = null,
    string? WireShape = null,
    string? TargetToolName = null)
{
    public static DeferredLoadingSmokeValidationResult Pass(
        string message,
        string wireShape,
        string targetToolName) =>
        new(
            true,
            message,
            DeferredToolLoadingObserved: true,
            WireShape: wireShape,
            TargetToolName: targetToolName);

    public static DeferredLoadingSmokeValidationResult Fail(
        string message,
        bool? deferredToolLoadingObserved = null,
        string? wireShape = null,
        string? targetToolName = null) =>
        new(
            false,
            message,
            deferredToolLoadingObserved,
            wireShape,
            targetToolName);
}
